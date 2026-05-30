using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SpeedTranslate.Linux.Models;

namespace SpeedTranslate.Linux.Services;

public class LLMService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public async Task<string> TranslateAsync(string text, AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var (apiUrl, apiKey, modelName) = ResolveModel(config);

        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API 接口地址和 Key 不能为空，请在设置中配置。");

        apiUrl = apiUrl.Trim();
        if (!apiUrl.EndsWith("/chat/completions"))
            apiUrl = apiUrl.TrimEnd('/') + "/chat/completions";

        var targetLangPrompt = GetTargetLanguagePrompt(config.TargetLanguage);
        var stylePrompt = "";
        if (config.TargetLanguage == "English" && config.TranslationStyle != "Standard")
        {
            stylePrompt = config.TranslationStyle switch
            {
                "AmericanColloquial" => "Translation style: Casual American English. Use natural local slang, typical idioms, and contractions (like 'gonna', 'wanna', 'I'd', 'you're') suitable for informal daily messaging.",
                "BritishColloquial" => "Translation style: Conversational British English. Use natural British expressions, phrasing, and idioms suitable for daily UK messaging.",
                "Business" => "Translation style: Professional Business English. Use polite, professional, and formal vocabulary suitable for workplace communications and emails.",
                "Academic" => "Translation style: Academic English. Use high-level vocabulary, varied sentence structures, and a formal tone suitable for IELTS or writing essays.",
                "Concise" => "Translation style: Concise and fluent English. Keep it as short and clear as possible. Eliminate redundancy, use direct and natural phrasing.",
                _ => "",
            };
        }

        var systemPrompt = $@"You are a professional and accurate translator. Translate the text provided by the user into the target language.

Target Language settings:
{targetLangPrompt}
{(string.IsNullOrWhiteSpace(stylePrompt) ? "" : "\n" + stylePrompt)}

CRITICAL RULES:
1. Output ONLY the raw translated text content. Do NOT wrap it in Markdown code blocks (do not use ```), and do NOT add any introductions, explanations, prefixes, or notes.
2. Keep the exact same formatting, paragraphs, spaces, and punctuation of the original text.
3. If the input text is already in the target language (or the main language matching it), translate it back to the other major language (e.g. if target language is Chinese, and the input is Chinese, translate it to English; if target language is English, and input is English, translate it to Chinese).";

        var requestBody = new ChatRequest
        {
            Model = modelName,
            Messages = new[]
            {
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = text },
            },
            Temperature = 0.3f,
        };

        var requestJson = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var response = await HttpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"API 请求失败 (HTTP {(int)response.StatusCode}): {responseContent}");

        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseContent);
        if (chatResponse?.Choices == null || chatResponse.Choices.Length == 0)
            throw new Exception("模型未返回任何结果。");

        var translatedText = chatResponse.Choices[0].Message?.Content
            ?? throw new Exception("模型响应内容为空。");

        return CleanTranslatedText(translatedText);
    }

    public async Task<List<string>> GetAvailableModelsAsync(string apiUrl, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API 接口地址和 Key 不能为空。");

        apiUrl = apiUrl.Trim();
        var modelsUrl = apiUrl;
        if (modelsUrl.EndsWith("/chat/completions"))
            modelsUrl = modelsUrl[..^"/chat/completions".Length];
        modelsUrl = modelsUrl.TrimEnd('/') + "/models";

        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        var response = await HttpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"获取模型失败 (HTTP {(int)response.StatusCode}): {responseContent}");

        var modelsResponse = JsonSerializer.Deserialize<ModelsListResponse>(responseContent);
        var list = new List<string>();
        if (modelsResponse?.Data != null)
        {
            foreach (var model in modelsResponse.Data)
            {
                if (!string.IsNullOrWhiteSpace(model.Id))
                    list.Add(model.Id.Trim());
            }
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static (string apiUrl, string apiKey, string modelName) ResolveModel(AppConfig config) =>
        config.SelectedModel switch
        {
            "DeepSeek" => (config.DeepSeekUrl, config.DeepSeekApiKey, config.DeepSeekModel),
            "XiaoMi" => (config.XiaoMiUrl, config.XiaoMiApiKey, config.XiaoMiModel),
            _ => (config.CustomUrl, config.CustomApiKey, config.CustomModel),
        };

    private static string GetTargetLanguagePrompt(string targetLang) => targetLang switch
    {
        "Auto" => "Automatic (Bilingual translation: translate Chinese to English, and translate non-Chinese languages like English/Japanese/Korean to Chinese).",
        "Chinese" => "Chinese (简体中文).",
        "English" => "English.",
        "Japanese" => "Japanese (日本語).",
        "Korean" => "Korean (한국어).",
        "French" => "French (Français).",
        "German" => "German (Deutsch).",
        "Spanish" => "Spanish (Español).",
        _ => "Chinese.",
    };

    private static string CleanTranslatedText(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewLine = text.IndexOf('\n');
            if (firstNewLine != -1)
                text = text[(firstNewLine + 1)..];
            if (text.EndsWith("```"))
                text = text[..^3];
            text = text.Trim();
        }
        return text;
    }

    private sealed class ModelsListResponse
    {
        [JsonPropertyName("data")] public ModelItem[]? Data { get; set; }
    }

    private sealed class ModelItem
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
        [JsonPropertyName("temperature")] public float Temperature { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public Choice[]? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ResponseMessage? Message { get; set; }
    }

    private sealed class ResponseMessage
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
