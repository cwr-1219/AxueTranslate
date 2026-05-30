import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

class AppLocalizations {
  final Locale locale;

  AppLocalizations(this.locale);

  static AppLocalizations? of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations);
  }

  static const Map<String, Map<String, String>> _localizedValues = {
    'zh': {
      'app_title': '阿雪翻译助手',
      'api_settings': 'API 接口配置',
      'api_url': '接口地址 (URL)',
      'api_key': '密钥 (API Key)',
      'model_name': '模型名称',
      'target_lang': '翻译目标语种',
      'system_prompt': '系统提示词 (Prompt)',
      'system_prompt_hint': '例如：你是一个翻译专家，请直接将输入翻译为目标语言，不要有任何多余的解释。',
      'float_switch': '启用全局悬浮球',
      'test_btn': '测试连接',
      'save_btn': '保存并应用',
      'app_lang': '系统语言 (Language)',
      'lang_zh': '简体中文',
      'lang_en': 'English',
      'perm_title': '需要系统权限',
      'perm_float_desc': '需要“悬浮窗权限”以在屏幕上显示悬浮球。',
      'perm_access_desc': '需要“无障碍服务权限”以获取输入框文字并自动替换。',
      'perm_grant_btn': '去授权',
      'toast_translating': '正在翻译中...',
      'toast_success': '翻译并替换成功',
      'toast_failed': '翻译失败，请检查配置或网络',
      'toast_saved': '配置已保存',
      'toast_test_success': '测试成功！译文：%s',
      'toast_test_failed': '测试失败：%s',
      'test_input_title': '测试翻译输入',
      'test_input_hint': '输入测试文字，如：你好',
      'test_result_title': '测试翻译结果',
      'float_window_status': '悬浮球状态',
      'float_window_running': '运行中',
      'float_window_stopped': '未启用',
      'accessibility_status': '无障碍服务',
      'accessibility_active': '已开启',
      'accessibility_inactive': '未开启',
      'float_permission': '悬浮窗权限',
      'permission_granted': '已授权',
      'permission_denied': '未授权',
      'cancel': '取消',
      'confirm': '确定',
      'translation_style': '翻译风格',
      'paste_btn': '粘贴',
      'fetch_models': '获取模型',
      'fetching': '获取中...',
      'add_provider': '新增供应商',
      'rename_provider': '修改名称',
      'delete_provider': '删除供应商',
      'provider_name': '供应商名称',
      'provider_name_hint': '请输入供应商名称，如：自定义模型',
      'provider_builtin_err': '系统内置供应商无法修改或删除',
      'model_list_title': '选择可用模型',
      'model_list_empty': '未找到可用模型',
      'duplicate_provider_err': '已存在同名供应商',
      'empty_provider_err': '名称不能为空',
      'battery_optimization': '忽略电池优化 (后台保活)',
      'battery_opt_desc': '保持后台活跃，防止无障碍服务或悬浮球在后台被杀掉。',
      'battery_opt_granted': '已忽略电池限制',
      'battery_opt_denied': '已受限',
      'auto_start_setting': '自启动与最近任务加锁',
      'auto_start_desc': '去开启本应用“自启动”并锁定后台任务，可有效提升存活率。',
      'go_setting_btn': '去设置',
    },
    'en': {
      'app_title': 'AxueTranslate',
      'api_settings': 'API Settings',
      'api_url': 'API Base URL',
      'api_key': 'API Key',
      'model_name': 'Model Name',
      'target_lang': 'Target Language',
      'system_prompt': 'System Prompt',
      'system_prompt_hint': 'e.g. You are a professional translator. Translate the text directly to the target language without explanation.',
      'float_switch': 'Enable Float Bubble',
      'test_btn': 'Test Connection',
      'save_btn': 'Save & Apply',
      'app_lang': 'Language',
      'lang_zh': '简体中文',
      'lang_en': 'English',
      'perm_title': 'Permissions Required',
      'perm_float_desc': 'Floating Window permission is required to display the float bubble.',
      'perm_access_desc': 'Accessibility Service is required to retrieve and replace text in other apps.',
      'perm_grant_btn': 'Grant',
      'toast_translating': 'Translating...',
      'toast_success': 'Replaced successfully',
      'toast_failed': 'Translation failed. Check settings or network.',
      'toast_saved': 'Configuration saved',
      'toast_test_success': 'Test Success! Output: %s',
      'toast_test_failed': 'Test Failed: %s',
      'test_input_title': 'Test Translation Input',
      'test_input_hint': 'Enter text to test, e.g. Hello',
      'test_result_title': 'Translation Result',
      'float_window_status': 'Float Bubble Status',
      'float_window_running': 'Running',
      'float_window_stopped': 'Disabled',
      'accessibility_status': 'Accessibility Service',
      'accessibility_active': 'Enabled',
      'accessibility_inactive': 'Disabled',
      'float_permission': 'Overlay Permission',
      'permission_granted': 'Granted',
      'permission_denied': 'Denied',
      'cancel': 'Cancel',
      'confirm': 'Confirm',
      'translation_style': 'Translation Style',
      'paste_btn': 'Paste',
      'fetch_models': 'Fetch Models',
      'fetching': 'Fetching...',
      'add_provider': 'Add Provider',
      'rename_provider': 'Rename',
      'delete_provider': 'Delete',
      'provider_name': 'Provider Name',
      'provider_name_hint': 'Enter provider name, e.g. Custom Model',
      'provider_builtin_err': 'Built-in providers cannot be modified or deleted',
      'model_list_title': 'Select Available Model',
      'model_list_empty': 'No available models found',
      'duplicate_provider_err': 'Provider name already exists',
      'empty_provider_err': 'Name cannot be empty',
      'battery_optimization': 'Battery Optimization (Keep Alive)',
      'battery_opt_desc': 'Ignore battery optimization to prevent background services from being killed.',
      'battery_opt_granted': 'Ignoring restrictions',
      'battery_opt_denied': 'Restricted',
      'auto_start_setting': 'Auto-start & App Lock Settings',
      'auto_start_desc': 'Configure auto-start and lock the app in recent tasks to keep it alive.',
      'go_setting_btn': 'Settings',
    },
  };

  String get(String key) {
    final values = _localizedValues[locale.languageCode] ?? _localizedValues['en']!;
    return values[key] ?? key;
  }

  // 格式化输出的方法（支持 %s 占位符）
  String getWithArgs(String key, List<String> args) {
    String value = get(key);
    for (var arg in args) {
      value = value.replaceFirst('%s', arg);
    }
    return value;
  }

  // 快捷获取
  String get appTitle => get('app_title');
  String get apiSettings => get('api_settings');
  String get apiUrl => get('api_url');
  String get apiKey => get('api_key');
  String get modelName => get('model_name');
  String get targetLang => get('target_lang');
  String get systemPrompt => get('system_prompt');
  String get systemPromptHint => get('system_prompt_hint');
  String get floatSwitch => get('float_switch');
  String get testBtn => get('test_btn');
  String get saveBtn => get('save_btn');
  String get appLang => get('app_lang');
  String get langZh => get('lang_zh');
  String get langEn => get('lang_en');
  String get permTitle => get('perm_title');
  String get permFloatDesc => get('perm_float_desc');
  String get permAccessDesc => get('perm_access_desc');
  String get permGrantBtn => get('perm_grant_btn');
  String get toastTranslating => get('toast_translating');
  String get toastSuccess => get('toast_success');
  String get toastFailed => get('toast_failed');
  String get toastSaved => get('toast_saved');
  String get testInputTitle => get('test_input_title');
  String get testInputHint => get('test_input_hint');
  String get testResultTitle => get('test_result_title');
  String get floatWindowStatus => get('float_window_status');
  String get floatWindowRunning => get('float_window_running');
  String get floatWindowStopped => get('float_window_stopped');
  String get accessibilityStatus => get('accessibility_status');
  String get accessibilityActive => get('accessibility_active');
  String get accessibilityInactive => get('accessibility_inactive');
  String get floatPermission => get('float_permission');
  String get permissionGranted => get('permission_granted');
  String get permissionDenied => get('permission_denied');
  String get cancel => get('cancel');
  String get confirm => get('confirm');
  String get translationStyle => get('translation_style');
  String get pasteBtn => get('paste_btn');
  String get fetchModels => get('fetch_models');
  String get fetching => get('fetching');
  String get addProvider => get('add_provider');
  String get renameProvider => get('rename_provider');
  String get deleteProvider => get('delete_provider');
  String get providerName => get('provider_name');
  String get providerNameHint => get('provider_name_hint');
  String get providerBuiltinErr => get('provider_builtin_err');
  String get modelListTitle => get('model_list_title');
  String get modelListEmpty => get('model_list_empty');
  String get duplicateProviderErr => get('duplicate_provider_err');
  String get emptyProviderErr => get('empty_provider_err');
  String get batteryOptimization => get('battery_optimization');
  String get batteryOptDesc => get('battery_opt_desc');
  String get batteryOptGranted => get('battery_opt_granted');
  String get batteryOptDenied => get('battery_opt_denied');
  String get autoStartSetting => get('auto_start_setting');
  String get autoStartDesc => get('auto_start_desc');
  String get goSettingBtn => get('go_setting_btn');
}

class AppLocalizationsDelegate extends LocalizationsDelegate<AppLocalizations> {
  const AppLocalizationsDelegate();

  @override
  bool isSupported(Locale locale) => ['zh', 'en'].contains(locale.languageCode);

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(AppLocalizations(locale));
  }

  @override
  bool shouldReload(AppLocalizationsDelegate old) => false;
}
