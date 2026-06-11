using System;

namespace SpeedTranslate.Linux.Models;

public sealed class TranslationHistoryEntry
{
    public string Id { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string SourceText { get; set; } = "";
    public string ResultText { get; set; } = "";
    public string TargetLanguage { get; set; } = "Auto";
    public string TranslationStyle { get; set; } = "Standard";
    public string ModelProvider { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string Mode { get; set; } = "";
    public bool IsFavorite { get; set; }
}
