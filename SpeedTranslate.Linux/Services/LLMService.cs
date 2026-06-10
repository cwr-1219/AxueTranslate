using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SpeedTranslate.Linux.Models;
using SpeedTranslate.Linux.Rendering;

namespace SpeedTranslate.Linux.Services;

public class LLMService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };
    private readonly HttpClient _httpClient;

    public LLMService()
        : this(SharedHttpClient)
    {
    }

    public LLMService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> TranslateAsync(
        string text,
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var targetLangPrompt = GetTargetLanguagePrompt(config.TargetLanguage);
        var stylePrompt = GetStylePrompt(config.TargetLanguage, config.TranslationStyle);
        var renderingPrompt = BuildMarkdownRenderingPrompt(config);

        var systemPrompt = $@"You are a professional and accurate translator. Translate the text provided by the user into the target language.

Target Language settings:
{targetLangPrompt}
{(string.IsNullOrWhiteSpace(stylePrompt) ? "" : "\n" + stylePrompt)}
{(string.IsNullOrWhiteSpace(renderingPrompt) ? "" : "\n\nRendering settings:\n" + renderingPrompt)}

CRITICAL RULES:
1. Output ONLY the translated text content. Do NOT wrap it in Markdown code blocks (do not use ```), and do NOT add any introductions, explanations, prefixes, or notes.
2. Keep the exact same formatting, paragraphs, spaces, Markdown structure, and punctuation of the original text, except for semantic color tags explicitly required by Rendering settings.
3. Keep mathematical formulas as LaTeX math using $...$, \(...\), $$...$$, or \[...\]. Translate surrounding prose, not formula symbols.
4. If the input text is already in the target language (or the main language matching it), translate it back to the other major language (e.g. if target language is Chinese, and the input is Chinese, translate it to English; if target language is English, and input is English, translate it to Chinese).";

        return await SendChatCompletionAsync(text, config, systemPrompt, 0.3f, cancellationToken);
    }

    public async Task<string> SummarizeAsync(
        string text,
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var targetLangPrompt = GetTargetLanguagePrompt(config.TargetLanguage);
        var renderingPrompt = BuildMarkdownRenderingPrompt(config);
        var systemPrompt = $@"You are a precise reading assistant. Summarize the user's text into the target language.

Target Language settings:
{targetLangPrompt}
{(string.IsNullOrWhiteSpace(renderingPrompt) ? "" : "\n\nRendering settings:\n" + renderingPrompt)}

CRITICAL RULES:
1. Output concise Markdown only. Do NOT wrap the answer in code fences and do NOT add explanations outside the summary.
2. Use short headings and bullet points in the target language.
3. Preserve key names, numbers, terms, conclusions, warnings, and mathematical formulas. Do not invent facts.
4. Prefer this structure when useful:
   - A one-sentence overview.
   - 3 to 6 key bullet points.
   - A short conclusion or next action if the source clearly contains one.";

        return await SendChatCompletionAsync(text, config, systemPrompt, 0.2f, cancellationToken);
    }

    private async Task<string> SendChatCompletionAsync(
        string text,
        AppConfig config,
        string systemPrompt,
        float temperature,
        CancellationToken cancellationToken)
    {
        var (apiUrl, apiKey, modelName) = ResolveModel(config);

        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API 接口地址和 Key 不能为空，请在设置中配置。");

        apiUrl = apiUrl.Trim();
        if (!apiUrl.EndsWith("/chat/completions"))
            apiUrl = apiUrl.TrimEnd('/') + "/chat/completions";

        var requestBody = new ChatRequest
        {
            Model = modelName,
            Messages = new[]
            {
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = text },
            },
            Temperature = temperature,
        };

        var requestJson = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var timeout = GetRequestTimeout(text);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        HttpResponseMessage response;
        string responseContent;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutCts.Token);
            responseContent = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"API 请求超时（已等待 {timeout.TotalSeconds:0} 秒）。请稍后重试，或减少一次翻译的文本长度。");
        }

        if (!response.IsSuccessStatusCode)
            throw new Exception($"API 请求失败 (HTTP {(int)response.StatusCode}): {responseContent}");

        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseContent);
        if (chatResponse?.Choices == null || chatResponse.Choices.Length == 0)
            throw new Exception("模型未返回任何结果。");

        var translatedText = chatResponse.Choices[0].Message?.Content
            ?? throw new Exception("模型响应内容为空。");

        return CleanTranslatedText(translatedText);
    }

    public static TimeSpan GetRequestTimeout(string text)
    {
        var length = text?.Length ?? 0;
        return length switch
        {
            <= 600 => TimeSpan.FromSeconds(15),
            <= 2000 => TimeSpan.FromSeconds(45),
            <= 5000 => TimeSpan.FromSeconds(90),
            _ => TimeSpan.FromSeconds(120),
        };
    }

    public static string BuildMarkdownRenderingPrompt(AppConfig config)
    {
        var parts = new List<string>();
        if (config.EnableMarkdownMathRendering)
        {
            parts.Add("""
                Math rendering:
                - Preserve existing mathematical formulas in LaTeX delimiters such as $...$, \(...\), $$...$$, or \[...\].
                - When a summary needs a formula, write it in compact LaTeX math delimiters.
                - Do not translate variable names or formula operators.
                """);
        }

        var colorRenderer = MarkdownColorRendererFactory.Create(config);
        if (!string.IsNullOrWhiteSpace(colorRenderer.PromptInstructions))
            parts.Add(colorRenderer.PromptInstructions);

        return string.Join("\n\n", parts);
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

        var response = await _httpClient.SendAsync(request);
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

    private static string GetStylePrompt(string targetLang, string style)
    {
        if (targetLang == "Auto" || style == "Standard")
            return "";

        if (targetLang == "English")
        {
            return style switch
            {
                "AmericanColloquial" => "Translation style: Casual American English. Use natural local slang, typical idioms, and contractions (like 'gonna', 'wanna', 'I'd', 'you're') suitable for informal daily messaging.",
                "BritishColloquial" => "Translation style: Conversational British English. Use natural British expressions, phrasing, and idioms suitable for daily UK messaging.",
                "Business" => "Translation style: Professional Business English. Use polite, professional, and formal vocabulary suitable for workplace communications and emails.",
                "Academic" => "Translation style: Academic English. Use high-level vocabulary, varied sentence structures, and a formal tone suitable for IELTS or writing essays.",
                "Concise" => "Translation style: Concise and fluent English. Keep it as short and clear as possible. Eliminate redundancy, use direct and natural phrasing.",
                _ => "",
            };
        }

        var langName = targetLang switch
        {
            "Chinese" => "Chinese (简体中文)",
            "Japanese" => "Japanese (日本語)",
            "Korean" => "Korean (한국어)",
            "French" => "French (Français)",
            "German" => "German (Deutsch)",
            "Spanish" => "Spanish (Español)",
            _ => targetLang,
        };

        return style switch
        {
            "Colloquial" => $"Translation style: Casual conversational {langName}. Use natural everyday phrasing, common idioms, and informal vocabulary suitable for daily chats and instant messaging.",
            "Business" => $"Translation style: Professional business {langName}. Use polite, formal vocabulary suitable for workplace communications, emails, and meetings. For Japanese/Korean, prefer the appropriate honorific register (敬語 / 존댓말).",
            "Academic" => $"Translation style: Formal academic / written {langName}. Use precise vocabulary, varied sentence structures, and a formal written tone suitable for papers, reports, or essays.",
            "Concise" => $"Translation style: Concise fluent {langName}. Keep it as short and clear as possible. Eliminate redundancy, use direct phrasing.",
            _ => "",
        };
    }

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
