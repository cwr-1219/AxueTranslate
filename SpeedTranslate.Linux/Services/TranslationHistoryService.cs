using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SpeedTranslate.Linux.Models;

namespace SpeedTranslate.Linux.Services;

public static class TranslationHistoryService
{
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string HistoryPath => Path.Combine(ConfigManager.ConfigDir, "history.json");

    public static IReadOnlyList<TranslationHistoryEntry> LoadEntries()
    {
        lock (SyncRoot)
        {
            return LoadEntriesUnsafe();
        }
    }

    public static IReadOnlyList<TranslationHistoryEntry> Search(string query, bool favoritesOnly)
    {
        var entries = LoadEntries();
        if (favoritesOnly)
            entries = entries.Where(e => e.IsFavorite).ToList();

        if (string.IsNullOrWhiteSpace(query))
            return entries;

        var needle = query.Trim();
        return entries
            .Where(e =>
                Contains(e.SourceText, needle)
                || Contains(e.ResultText, needle)
                || Contains(e.ModelName, needle)
                || Contains(e.TargetLanguage, needle)
                || Contains(e.TranslationStyle, needle))
            .ToList();
    }

    public static void AddEntry(TranslationHistoryEntry entry, AppConfig config)
    {
        if (!config.EnableTranslationHistory)
            return;

        try
        {
            lock (SyncRoot)
            {
                var entries = LoadEntriesUnsafe().ToList();
                entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id;
                entry.CreatedAt = entry.CreatedAt == default ? DateTimeOffset.Now : entry.CreatedAt;
                entries.Insert(0, entry);
                SaveEntriesUnsafe(Prune(entries, config));
            }
        }
        catch (Exception ex)
        {
            ConfigManager.WriteErrorLog("Save translation history", ex);
        }
    }

    public static TranslationHistoryEntry CreateEntry(
        string sourceText,
        string resultText,
        AppConfig config,
        string mode) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.Now,
            SourceText = sourceText,
            ResultText = resultText,
            TargetLanguage = config.TargetLanguage,
            TranslationStyle = config.TranslationStyle,
            ModelProvider = config.SelectedModel,
            ModelName = ResolveModelName(config),
            Mode = mode,
        };

    public static bool ToggleFavorite(string id)
    {
        lock (SyncRoot)
        {
            var entries = LoadEntriesUnsafe().ToList();
            var entry = entries.FirstOrDefault(e => e.Id == id);
            if (entry == null)
                return false;

            entry.IsFavorite = !entry.IsFavorite;
            SaveEntriesUnsafe(entries);
            return true;
        }
    }

    public static bool Delete(string id)
    {
        lock (SyncRoot)
        {
            var entries = LoadEntriesUnsafe().ToList();
            var removed = entries.RemoveAll(e => e.Id == id) > 0;
            if (removed)
                SaveEntriesUnsafe(entries);
            return removed;
        }
    }

    public static void ClearAll()
    {
        lock (SyncRoot)
        {
            SaveEntriesUnsafe(Array.Empty<TranslationHistoryEntry>());
        }
    }

    public static IReadOnlyList<TranslationHistoryEntry> Prune(
        IEnumerable<TranslationHistoryEntry> entries,
        AppConfig config)
    {
        var retentionDays = Math.Clamp(config.HistoryRetentionDays, 1, 3650);
        var maxItems = Math.Clamp(config.MaxHistoryItems, 1, 10000);
        var cutoff = DateTimeOffset.Now.AddDays(-retentionDays);

        var retained = entries
            .Where(e => e.IsFavorite || e.CreatedAt >= cutoff)
            .OrderByDescending(e => e.CreatedAt)
            .ToList();

        if (retained.Count <= maxItems)
            return retained;

        // Favorites are user's explicit memory. Keep them before trimming ordinary history.
        return retained
            .OrderByDescending(e => e.IsFavorite)
            .ThenByDescending(e => e.CreatedAt)
            .Take(maxItems)
            .OrderByDescending(e => e.CreatedAt)
            .ToList();
    }

    private static List<TranslationHistoryEntry> LoadEntriesUnsafe()
    {
        try
        {
            if (!File.Exists(HistoryPath))
                return new List<TranslationHistoryEntry>();

            var json = File.ReadAllText(HistoryPath);
            var entries = JsonSerializer.Deserialize<List<TranslationHistoryEntry>>(json, JsonOptions);
            return entries?
                .Where(e => !string.IsNullOrWhiteSpace(e.Id))
                .OrderByDescending(e => e.CreatedAt)
                .ToList() ?? new List<TranslationHistoryEntry>();
        }
        catch (Exception ex)
        {
            ConfigManager.WriteErrorLog("Load translation history", ex);
            return new List<TranslationHistoryEntry>();
        }
    }

    private static void SaveEntriesUnsafe(IEnumerable<TranslationHistoryEntry> entries)
    {
        Directory.CreateDirectory(ConfigManager.ConfigDir);
        var tempPath = HistoryPath + ".tmp";
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, HistoryPath, overwrite: true);
    }

    private static bool Contains(string value, string needle) =>
        value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string ResolveModelName(AppConfig config) => config.SelectedModel switch
    {
        "DeepSeek" => config.DeepSeekModel,
        "XiaoMi" => config.XiaoMiModel,
        _ => config.CustomModel,
    };
}
