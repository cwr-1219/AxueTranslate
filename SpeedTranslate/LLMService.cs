using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SpeedTranslate
{
    public class LLMService
    {
        private static readonly HttpClient HttpClient = new HttpClient(new HttpClientHandler
        {
            // 忽略 SSL 证书验证错误，以完美兼容各类自建 IP 中转站、反代和自签名证书
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        })
        {
            Timeout = TimeSpan.FromSeconds(15) // 设置 15 秒超时
        };

        /// <summary>
        /// 调用大模型翻译文本
        /// </summary>
        /// <param name="text">待翻译的文本</param>
        /// <param name="config">配置项</param>
        /// <returns>翻译后的文本</returns>
        public async Task<string> TranslateAsync(string text, AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // 1. 获取对应模型的配置信息
            string apiUrl = "";
            string apiKey = "";
            string modelName = "";

            ModelConfig activeConfig = null;
            if (config.ModelConfigs != null)
            {
                activeConfig = config.ModelConfigs.Find(m => m.DisplayName == config.SelectedModel);
            }

            if (activeConfig != null)
            {
                apiUrl = activeConfig.ApiUrl;
                apiKey = activeConfig.ApiKey;
                modelName = activeConfig.ModelName;
            }
            else
            {
                // 向下兼容历史配置字段
                if (config.SelectedModel == "DeepSeek" || config.SelectedModel == "DeepSeek 大模型")
                {
                    apiUrl = config.DeepSeekUrl;
                    apiKey = config.DeepSeekApiKey;
                    modelName = config.DeepSeekModel;
                }
                else if (config.SelectedModel == "XiaoMi" || config.SelectedModel == "小米大模型")
                {
                    apiUrl = config.XiaoMiUrl;
                    apiKey = config.XiaoMiApiKey;
                    modelName = config.XiaoMiModel;
                }
                else
                {
                    apiUrl = config.CustomUrl;
                    apiKey = config.CustomApiKey;
                    modelName = config.CustomModel;
                }
            }

            if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API 接口地址和 Key 不能为空，请在设置中配置。");
            }

            // 构建候选 URL 终结点，支持自动 fallback 以兼容不需要 /v1 或需要 /v1 的情况
            apiUrl = apiUrl.Trim();
            var candidateUrls = new System.Collections.Generic.List<string>();
            if (apiUrl.EndsWith("/chat/completions"))
            {
                candidateUrls.Add(apiUrl);
            }
            else
            {
                string tempUrl = apiUrl.TrimEnd('/');
                if (tempUrl.EndsWith("/v1") || tempUrl.Contains("/v1/") || tempUrl.Contains("xiaomimimo.com"))
                {
                    candidateUrls.Add(tempUrl + "/chat/completions");
                }
                else
                {
                    // 默认优先尝试自建中转站的带 /v1 地址，然后再尝试直连不带 /v1 地址
                    candidateUrls.Add(tempUrl + "/v1/chat/completions");
                    candidateUrls.Add(tempUrl + "/chat/completions");
                }
            }

            // 2. 构造 System Prompt (根据目标语种和口语等风格定制)
            string targetLangPrompt = GetTargetLanguagePrompt(config.TargetLanguage);
            
            string stylePrompt = "";
            if (config.TargetLanguage == "English" && config.TranslationStyle != "Standard")
            {
                stylePrompt = config.TranslationStyle switch
                {
                    "AmericanColloquial" => "Translation style: Casual American English. Use natural local slang, typical idioms, and contractions (like 'gonna', 'wanna', 'I'd', 'you're') suitable for informal daily messaging.",
                    "BritishColloquial" => "Translation style: Conversational British English. Use natural British expressions, phrasing, and idioms suitable for daily UK messaging.",
                    "Business" => "Translation style: Professional Business English. Use polite, professional, and formal vocabulary suitable for workplace communications and emails.",
                    "Academic" => "Translation style: Academic English. Use high-level vocabulary, varied sentence structures, and a formal tone suitable for IELTS or writing essays.",
                    "Concise" => "Translation style: Concise and fluent English. Keep it as short and clear as possible. Eliminate redundancy, use direct and natural phrasing.",
                    _ => ""
                };
            }

            string systemPrompt = $@"You are a professional and accurate translator. Translate the text provided by the user into the target language.

Target Language settings:
{targetLangPrompt}
{(string.IsNullOrWhiteSpace(stylePrompt) ? "" : "\n" + stylePrompt)}

CRITICAL RULES:
1. Output ONLY the raw translated text content. Do NOT wrap it in Markdown code blocks (do not use ```), and do NOT add any introductions, explanations, prefixes, or notes.
2. Keep the exact same formatting, paragraphs, spaces, and punctuation of the original text.
3. If the input text is already in the target language (or the main language matching it), translate it back to the other major language (e.g. if target language is Chinese, and the input is Chinese, translate it to English; if target language is English, and input is English, translate it to Chinese).";

            // 3. 构建请求体
            var requestBody = new ChatRequest
            {
                Model = modelName,
                Messages = new[]
                {
                    new ChatMessage { Role = "system", Content = systemPrompt },
                    new ChatMessage { Role = "user", Content = text }
                },
                Temperature = 0.3f
            };

            string requestJson = JsonSerializer.Serialize(requestBody);

            // 4. 发送请求（带 404 自适应重试）
            HttpResponseMessage? response = null;
            string responseContent = "";
            string lastErrorMsg = "";

            for (int i = 0; i < candidateUrls.Count; i++)
            {
                string currentUrl = candidateUrls[i];
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, currentUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                    response = await HttpClient.SendAsync(request);
                    responseContent = await response.Content.ReadAsStringAsync();

                    // 如果返回 404 (NotFound)，且还有其他备选 URL，则自动重试下一个 URL
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound && i < candidateUrls.Count - 1)
                    {
                        response.Dispose();
                        response = null;
                        continue;
                    }
                    
                    // 其它状态码或请求成功，说明找到了正确的终结点，停止重试
                    break;
                }
                catch (Exception ex)
                {
                    lastErrorMsg = ex.Message;
                    if (i < candidateUrls.Count - 1)
                    {
                        continue;
                    }
                    throw;
                }
            }

            if (response == null)
            {
                throw new Exception($"无法连接到 API 服务: {lastErrorMsg}");
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API 请求失败 (HTTP {response.StatusCode}): {responseContent}");
                }

                try
                {
                    var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseContent);
                    if (chatResponse?.Choices == null || chatResponse.Choices.Length == 0)
                    {
                        throw new Exception("模型未返回任何结果。");
                    }

                    string translatedText = chatResponse.Choices[0].Message?.Content;
                    if (translatedText == null)
                    {
                        throw new Exception("模型响应内容为空。");
                    }

                    // 移除前后空白和多余的包裹字符
                    translatedText = CleanTranslatedText(translatedText);

                    return translatedText;
                }
                catch (JsonException jsonEx)
                {
                    // 检测是否返回了 HTML (首字母为 '<')
                    string trimmedResponse = responseContent.Trim();
                    if (trimmedResponse.StartsWith("<") || trimmedResponse.Contains("<!DOCTYPE") || trimmedResponse.Contains("<html"))
                    {
                        throw new Exception("接口返回了网页而非 JSON 数据，请检查接口地址是否需要补全 /v1 后缀或端口是否正确。", jsonEx);
                    }
                    else
                    {
                        throw new Exception($"模型响应解析失败。请确保填写的地址正确。原始响应: {responseContent}", jsonEx);
                    }
                }
            }
        }

        private string GetTargetLanguagePrompt(string targetLang)
        {
            return targetLang switch
            {
                "Auto" => "Automatic (Bilingual translation: translate Chinese to English, and translate non-Chinese languages like English/Japanese/Korean to Chinese).",
                "Chinese" => "Chinese (简体中文).",
                "English" => "English.",
                "Japanese" => "Japanese (日本語).",
                "Korean" => "Korean (한국어).",
                "French" => "French (Français).",
                "German" => "German (Deutsch).",
                "Spanish" => "Spanish (Español).",
                _ => "Chinese."
            };
        }

        private string CleanTranslatedText(string text)
        {
            text = text.Trim();
            // 去除大模型可能误加的 Markdown 代码块前缀和后缀
            if (text.StartsWith("```"))
            {
                int firstNewLine = text.IndexOf('\n');
                if (firstNewLine != -1)
                {
                    text = text.Substring(firstNewLine + 1);
                }
                if (text.EndsWith("```"))
                {
                    text = text.Substring(0, text.Length - 3);
                }
                text = text.Trim();
            }
            return text;
        }

        /// <summary>
        /// 从接口获取所有可用模型列表
        /// </summary>
        public async Task<System.Collections.Generic.List<string>> GetAvailableModelsAsync(string apiUrl, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API 接口地址和 Key 不能为空。");
            }

            apiUrl = apiUrl.Trim();
            string baseApiUrl = apiUrl;
            if (baseApiUrl.EndsWith("/chat/completions"))
            {
                baseApiUrl = baseApiUrl.Substring(0, baseApiUrl.Length - "/chat/completions".Length);
            }
            baseApiUrl = baseApiUrl.TrimEnd('/');

            var candidateModelUrls = new System.Collections.Generic.List<string>();
            if (baseApiUrl.EndsWith("/v1") || baseApiUrl.Contains("/v1/") || baseApiUrl.Contains("xiaomimimo.com"))
            {
                candidateModelUrls.Add(baseApiUrl + "/models");
            }
            else
            {
                // 默认优先尝试带 /v1/models 的路径，如果失败再重试 /models 路径以适应各种反代
                candidateModelUrls.Add(baseApiUrl + "/v1/models");
                candidateModelUrls.Add(baseApiUrl + "/models");
            }

            HttpResponseMessage? response = null;
            string responseContent = "";
            string lastErrorMsg = "";

            for (int i = 0; i < candidateModelUrls.Count; i++)
            {
                string currentUrl = candidateModelUrls[i];
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());

                    response = await HttpClient.SendAsync(request);
                    responseContent = await response.Content.ReadAsStringAsync();

                    // 如果返回 404 (NotFound)，且还有其他候选路径，则自动重试
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound && i < candidateModelUrls.Count - 1)
                    {
                        response.Dispose();
                        response = null;
                        continue;
                    }
                    break;
                }
                catch (Exception ex)
                {
                    lastErrorMsg = ex.Message;
                    if (i < candidateModelUrls.Count - 1)
                    {
                        continue;
                    }
                    throw;
                }
            }

            if (response == null)
            {
                throw new Exception($"无法连接到 API 服务获取模型列表: {lastErrorMsg}");
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"获取模型失败 (HTTP {response.StatusCode}): {responseContent}");
                }

                var modelsResponse = JsonSerializer.Deserialize<ModelsListResponse>(responseContent);
                var list = new System.Collections.Generic.List<string>();
                if (modelsResponse?.Data != null)
                {
                    foreach (var model in modelsResponse.Data)
                    {
                        if (!string.IsNullOrWhiteSpace(model.Id))
                        {
                            list.Add(model.Id.Trim());
                        }
                    }
                }
                list.Sort();
                return list;
            }
        }

        // --- Data Transfer Objects ---

        private class ModelsListResponse
        {
            [JsonPropertyName("data")]
            public ModelItem[] Data { get; set; }
        }

        private class ModelItem
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }
        }

        private class ChatRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("messages")]
            public ChatMessage[] Messages { get; set; }

            [JsonPropertyName("temperature")]
            public float Temperature { get; set; }
        }

        private class ChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
        }

        private class ChatResponse
        {
            [JsonPropertyName("choices")]
            public Choice[] Choices { get; set; }
        }

        private class Choice
        {
            [JsonPropertyName("message")]
            public ResponseMessage Message { get; set; }
        }

        private class ResponseMessage
        {
            [JsonPropertyName("content")]
            public string Content { get; set; }
        }
    }
}
