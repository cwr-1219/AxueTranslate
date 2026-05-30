using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpeedTranslate.Linux.Models;
using SpeedTranslate.Linux.Services;

namespace SpeedTranslate.Linux.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly LLMService _llmService = new();
    private AppConfig _config;
    private string _lastSelectedModelKey = "DeepSeek";

    [ObservableProperty] private int _selectedModelIndex;

    [ObservableProperty] private string _apiUrl = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _modelName = "";

    [ObservableProperty] private int _languageIndex;
    [ObservableProperty] private int _styleIndex;
    [ObservableProperty] private bool _isStyleVisible;

    [ObservableProperty] private bool _enableSelectionMode;
    [ObservableProperty] private bool _enableAllTextMode;

    [ObservableProperty] private HotkeyDescriptor _hotkey = new();
    public string HotkeyDisplay => Hotkey.DisplayText;

    [ObservableProperty] private bool _isFetchingModels;
    [ObservableProperty] private string _fetchModelsButtonText = "切换模型";

    public event EventHandler? RequestSaveAndHide;
    public event EventHandler<string>? RequestShowError;
    public event EventHandler<List<string>>? RequestShowModelPicker;
    public event EventHandler<HotkeyDescriptor>? HotkeyChanged;

    private static readonly string[] LanguageTags =
        { "Auto", "English", "Chinese", "Japanese", "Korean", "French", "German", "Spanish" };

    private static readonly string[] StyleTags =
        { "Standard", "AmericanColloquial", "BritishColloquial", "Business", "Academic", "Concise" };

    public MainWindowViewModel()
    {
        _config = ConfigManager.LoadConfig();
        ApplyConfigToUI(_config);
    }

    public AppConfig CurrentConfig => _config;

    private void ApplyConfigToUI(AppConfig config)
    {
        _lastSelectedModelKey = config.SelectedModel;
        SelectedModelIndex = config.SelectedModel switch
        {
            "DeepSeek" => 0,
            "XiaoMi" => 1,
            _ => 2,
        };

        LoadModelInputsFromConfig(config.SelectedModel);

        var langIdx = Array.IndexOf(LanguageTags, config.TargetLanguage);
        LanguageIndex = langIdx < 0 ? 0 : langIdx;

        var styleIdx = Array.IndexOf(StyleTags, config.TranslationStyle);
        StyleIndex = styleIdx < 0 ? 0 : styleIdx;

        IsStyleVisible = config.TargetLanguage == "English";

        EnableSelectionMode = config.EnableSelectionMode;
        EnableAllTextMode = config.EnableAllTextMode;

        Hotkey = config.Hotkey;
        OnPropertyChanged(nameof(HotkeyDisplay));
    }

    partial void OnSelectedModelIndexChanged(int oldValue, int newValue)
    {
        SaveModelInputsToConfig(_lastSelectedModelKey);
        var newKey = newValue switch
        {
            0 => "DeepSeek",
            1 => "XiaoMi",
            _ => "Custom",
        };
        _lastSelectedModelKey = newKey;
        LoadModelInputsFromConfig(newKey);
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (value < 0 || value >= LanguageTags.Length) return;
        IsStyleVisible = LanguageTags[value] == "English";
    }

    partial void OnHotkeyChanged(HotkeyDescriptor value)
    {
        OnPropertyChanged(nameof(HotkeyDisplay));
        HotkeyChanged?.Invoke(this, value);
    }

    private void LoadModelInputsFromConfig(string modelKey)
    {
        switch (modelKey)
        {
            case "DeepSeek":
                ApiUrl = _config.DeepSeekUrl;
                ApiKey = _config.DeepSeekApiKey;
                ModelName = _config.DeepSeekModel;
                break;
            case "XiaoMi":
                ApiUrl = _config.XiaoMiUrl;
                ApiKey = _config.XiaoMiApiKey;
                ModelName = _config.XiaoMiModel;
                break;
            default:
                ApiUrl = _config.CustomUrl;
                ApiKey = _config.CustomApiKey;
                ModelName = _config.CustomModel;
                break;
        }
    }

    private void SaveModelInputsToConfig(string modelKey)
    {
        switch (modelKey)
        {
            case "DeepSeek":
                _config.DeepSeekUrl = ApiUrl;
                _config.DeepSeekApiKey = ApiKey;
                _config.DeepSeekModel = ModelName;
                break;
            case "XiaoMi":
                _config.XiaoMiUrl = ApiUrl;
                _config.XiaoMiApiKey = ApiKey;
                _config.XiaoMiModel = ModelName;
                break;
            default:
                _config.CustomUrl = ApiUrl;
                _config.CustomApiKey = ApiKey;
                _config.CustomModel = ModelName;
                break;
        }
    }

    public AppConfig BuildConfigFromUI()
    {
        SaveModelInputsToConfig(_lastSelectedModelKey);
        _config.SelectedModel = _lastSelectedModelKey;
        _config.TargetLanguage = LanguageTags[Math.Clamp(LanguageIndex, 0, LanguageTags.Length - 1)];
        _config.TranslationStyle = StyleTags[Math.Clamp(StyleIndex, 0, StyleTags.Length - 1)];
        _config.EnableSelectionMode = EnableSelectionMode;
        _config.EnableAllTextMode = EnableAllTextMode;
        _config.Hotkey = Hotkey;
        return _config;
    }

    public void ApplyHotkey(HotkeyModifiers modifiers, string keyName)
    {
        Hotkey = new HotkeyDescriptor { Modifiers = modifiers, Key = keyName };
    }

    public void ApplyChosenModelName(string modelId)
    {
        ModelName = modelId;
    }

    [RelayCommand]
    private void Save()
    {
        var config = BuildConfigFromUI();
        ConfigManager.SaveConfig(config);
        RequestSaveAndHide?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task FetchModelsAsync()
    {
        if (IsFetchingModels) return;
        if (string.IsNullOrWhiteSpace(ApiUrl) || string.IsNullOrWhiteSpace(ApiKey))
        {
            RequestShowError?.Invoke(this, "请先填入 API 接口地址和 API Key。");
            return;
        }

        IsFetchingModels = true;
        FetchModelsButtonText = "拉取中...";
        try
        {
            var list = await _llmService.GetAvailableModelsAsync(ApiUrl, ApiKey);
            if (list.Count == 0)
            {
                RequestShowError?.Invoke(this, "接口返回模型列表为空。");
                return;
            }
            RequestShowModelPicker?.Invoke(this, list);
        }
        catch (Exception ex)
        {
            ConfigManager.WriteErrorLog("获取可用模型列表失败", ex);
            RequestShowError?.Invoke(this, $"获取模型列表失败: {ex.Message}");
        }
        finally
        {
            IsFetchingModels = false;
            FetchModelsButtonText = "切换模型";
        }
    }
}
