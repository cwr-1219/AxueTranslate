import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'localizations.dart';
import 'dart:convert';
import 'package:http/http.dart' as http;

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const MyApp());
}

// 用于多语言切换的全局通知器
final ValueNotifier<Locale> languageNotifier = ValueNotifier(const Locale('zh'));

// 模型配置实体类
class ModelConfig {
  String displayName;
  String apiUrl;
  String apiKey;
  String modelName;

  ModelConfig({
    required this.displayName,
    required this.apiUrl,
    required this.apiKey,
    required this.modelName,
  });

  Map<String, dynamic> toJson() => {
        'displayName': displayName,
        'apiUrl': apiUrl,
        'apiKey': apiKey,
        'modelName': modelName,
      };

  factory ModelConfig.fromJson(Map<String, dynamic> json) => ModelConfig(
        displayName: json['displayName'] ?? '',
        apiUrl: json['apiUrl'] ?? '',
        apiKey: json['apiKey'] ?? '',
        modelName: json['modelName'] ?? '',
      );
}

// 翻译风格配置实体类
class StyleConfig {
  String name;
  String displayName;
  String prompt;
  bool isBuiltIn;

  StyleConfig({
    required this.name,
    required this.displayName,
    required this.prompt,
    this.isBuiltIn = false,
  });

  Map<String, dynamic> toJson() => {
        'name': name,
        'displayName': displayName,
        'prompt': prompt,
        'isBuiltIn': isBuiltIn,
      };

  factory StyleConfig.fromJson(Map<String, dynamic> json) => StyleConfig(
        name: json['name'] ?? '',
        displayName: json['displayName'] ?? '',
        prompt: json['prompt'] ?? '',
        isBuiltIn: json['isBuiltIn'] ?? false,
      );
}

class MyApp extends StatelessWidget {
  const MyApp({super.key});

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<Locale>(
      valueListenable: languageNotifier,
      builder: (context, currentLocale, _) {
        return MaterialApp(
          title: 'AxueTranslate Mobile',
          theme: ThemeData(
            brightness: Brightness.dark,
            primaryColor: const Color(0xFFA855F7),
            scaffoldBackgroundColor: Colors.transparent,
            fontFamily: 'Roboto',
            colorScheme: const ColorScheme.dark(
              primary: Color(0xFFA855F7),
              secondary: Color(0xFF6366F1),
              surface: Color(0x0F0A0817),
            ),
          ),
          locale: currentLocale,
          localizationsDelegates: const [
            AppLocalizationsDelegate(),
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          supportedLocales: const [
            Locale('zh'),
            Locale('en'),
          ],
          home: const MainConfigPage(),
        );
      },
    );
  }
}

class MainConfigPage extends StatefulWidget {
  const MainConfigPage({super.key});

  @override
  State<MainConfigPage> createState() => _MainConfigPageState();
}

class _MainConfigPageState extends State<MainConfigPage> {
  // Method Channel 定义
  static const _channel = MethodChannel('com.axue.translate/commands');

  // 配置项控制器
  final _apiUrlController = TextEditingController();
  final _apiKeyController = TextEditingController();
  final _modelController = TextEditingController();
  final _promptController = TextEditingController();
  final _testInputController = TextEditingController();

  // 供应商列表状态
  List<ModelConfig> _modelConfigs = [];
  List<StyleConfig> _styleConfigs = [];
  String _selectedModel = 'DeepSeek 大模型';

  String _selectedLang = 'Auto';
  String _selectedStyle = 'Standard'; // 翻译风格，默认标准
  bool _enableFloat = false;

  // 状态变量
  String _testResult = '';
  bool _isTesting = false;
  bool _isFetchingModels = false;

  // Android 原生服务及权限状态
  bool _floatPermGranted = false;
  bool _accessActive = false;
  bool _batteryOptGranted = false;

  @override
  void initState() {
    super.initState();
    _loadSettings();
    _checkSystemPermissions();
  }

  // 初始化默认供应商
  List<ModelConfig> _getDefaultConfigs() {
    return [
      ModelConfig(
        displayName: 'DeepSeek 大模型',
        apiUrl: 'https://api.deepseek.com',
        apiKey: '',
        modelName: 'deepseek-chat',
      ),
      ModelConfig(
        displayName: '小米大模型',
        apiUrl: 'https://token-plan-cn.xiaomimimo.com/v1',
        apiKey: '',
        modelName: 'gpt-4o-mini',
      ),
      ModelConfig(
        displayName: 'OpenAI (ChatGPT)',
        apiUrl: 'https://api.openai.com',
        apiKey: '',
        modelName: 'gpt-4o-mini',
      ),
      ModelConfig(
        displayName: 'Google (Gemini)',
        apiUrl: 'https://generativelanguage.googleapis.com',
        apiKey: '',
        modelName: 'gemini-1.5-flash',
      ),
      ModelConfig(
        displayName: '自定义模型',
        apiUrl: 'https://api.openai.com',
        apiKey: '',
        modelName: 'gpt-4o-mini',
      ),
    ];
  }

  // 将当前 UI 配置缓存回内存中的 _modelConfigs
  void _saveCurrentConfigToMemory(String modelName) {
    int idx = _modelConfigs.indexWhere((m) => m.displayName == modelName);
    if (idx != -1) {
      _modelConfigs[idx].apiUrl = _apiUrlController.text;
      _modelConfigs[idx].apiKey = _apiKeyController.text;
      _modelConfigs[idx].modelName = _modelController.text;
    }
  }

  // 加载指定供应商的配置到 UI 中
  void _loadConfigToUI(String modelName) {
    int idx = _modelConfigs.indexWhere((m) => m.displayName == modelName);
    if (idx != -1) {
      _apiUrlController.text = _modelConfigs[idx].apiUrl;
      _apiKeyController.text = _modelConfigs[idx].apiKey;
      _modelController.text = _modelConfigs[idx].modelName;
    }
  }

  // 初始化默认翻译风格
  List<StyleConfig> _getDefaultStyleConfigs() {
    return [
      StyleConfig(
        name: 'Standard',
        displayName: '标准 (Standard)',
        prompt: '',
        isBuiltIn: true,
      ),
      StyleConfig(
        name: 'AmericanColloquial',
        displayName: '美式口语 (American Colloquial)',
        prompt: "Translation style: Casual American English. Use natural local slang, typical idioms, and contractions (like 'gonna', 'wanna', 'I\\'d', 'you\'re') suitable for informal daily messaging.",
        isBuiltIn: true,
      ),
      StyleConfig(
        name: 'BritishColloquial',
        displayName: '英式口语 (British Colloquial)',
        prompt: "Translation style: Conversational British English. Use natural British expressions, phrasing, and idioms suitable for daily UK messaging.",
        isBuiltIn: true,
      ),
      StyleConfig(
        name: 'Business',
        displayName: '商务职场 (Business)',
        prompt: "Translation style: Professional Business English. Use polite, professional, and formal vocabulary suitable for workplace communications and emails.",
        isBuiltIn: true,
      ),
      StyleConfig(
        name: 'Academic',
        displayName: '学术雅思 (Academic)',
        prompt: "Translation style: Academic English. Use high-level vocabulary, varied sentence structures, and a formal tone suitable for essays and academic writing.",
        isBuiltIn: true,
      ),
      StyleConfig(
        name: 'Concise',
        displayName: '极简流利 (Concise)',
        prompt: "Translation style: Concise and fluent. Keep it as short and clear as possible. Eliminate redundancy, use direct and natural phrasing.",
        isBuiltIn: true,
      ),
    ];
  }

  // 从 SharedPreferences 中读取配置
  Future<void> _loadSettings() async {
    final prefs = await SharedPreferences.getInstance();
    
    // 加载多供应商列表
    String? configsJson = prefs.getString('model_configs_json');
    if (configsJson != null && configsJson.isNotEmpty) {
      try {
        List decoded = jsonDecode(configsJson);
        _modelConfigs = decoded.map((j) => ModelConfig.fromJson(j)).toList();
      } catch (e) {
        _modelConfigs = _getDefaultConfigs();
      }
    } else {
      _modelConfigs = _getDefaultConfigs();
    }

    // 静默热修复可能已存在的旧小米大模型 API 域名配置
    bool needsAutoSave = false;
    for (var m in _modelConfigs) {
      if (m.displayName == '小米大模型' && m.apiUrl == 'https://api.xiaomimimo.com/v1') {
        m.apiUrl = 'https://token-plan-cn.xiaomimimo.com/v1';
        needsAutoSave = true;
      }
    }
    if (needsAutoSave) {
      String jsonStr = jsonEncode(_modelConfigs.map((m) => m.toJson()).toList());
      await prefs.setString('model_configs_json', jsonStr);
    }

    // 加载翻译风格列表
    String? stylesJson = prefs.getString('style_configs_json');
    if (stylesJson != null && stylesJson.isNotEmpty) {
      try {
        List decoded = jsonDecode(stylesJson);
        _styleConfigs = decoded.map((j) => StyleConfig.fromJson(j)).toList();
      } catch (e) {
        _styleConfigs = _getDefaultStyleConfigs();
      }
    } else {
      _styleConfigs = _getDefaultStyleConfigs();
    }

    setState(() {
      _selectedModel = prefs.getString('selected_model') ?? 'DeepSeek 大模型';
      
      // 如果保存的供应商不存在于列表中，切回默认
      if (!_modelConfigs.any((m) => m.displayName == _selectedModel)) {
        _selectedModel = 'DeepSeek 大模型';
      }

      // 回填 UI 字段
      _loadConfigToUI(_selectedModel);

      _promptController.text = prefs.getString('system_prompt') ?? '';
      _selectedLang = prefs.getString('target_language') ?? 'Auto';
      _selectedStyle = prefs.getString('translation_style') ?? 'Standard';
      
      // 校验翻译风格有效性
      if (!_styleConfigs.any((s) => s.name == _selectedStyle)) {
        _selectedStyle = 'Standard';
      }

      _enableFloat = prefs.getBool('enable_float') ?? false;

      // 语言环境
      String savedLang = prefs.getString('app_language') ?? '';
      if (savedLang.isEmpty) {
        final systemLocale = PlatformDispatcher.instance.locale.languageCode;
        savedLang = systemLocale == 'zh' ? 'zh' : 'en';
      }
      languageNotifier.value = Locale(savedLang);
    });
  }

  // 保存设置到 SharedPreferences 并且同步给 Android 原生
  Future<void> _saveSettings() async {
    final prefs = await SharedPreferences.getInstance();
    
    // 先把当前正在编辑的供应商参数暂存回内存
    _saveCurrentConfigToMemory(_selectedModel);

    // 1. 持久化存储
    await prefs.setString('selected_model', _selectedModel);
    
    // 序列化供应商配置
    String configsJson = jsonEncode(_modelConfigs.map((m) => m.toJson()).toList());
    await prefs.setString('model_configs_json', configsJson);

    // 序列化翻译风格配置
    String stylesJson = jsonEncode(_styleConfigs.map((s) => s.toJson()).toList());
    await prefs.setString('style_configs_json', stylesJson);

    await prefs.setString('api_url', _apiUrlController.text);
    await prefs.setString('api_key', _apiKeyController.text);
    await prefs.setString('model_name', _modelController.text);
    await prefs.setString('system_prompt', _promptController.text);
    await prefs.setString('target_language', _selectedLang);
    await prefs.setString('translation_style', _selectedStyle);
    await prefs.setBool('enable_float', _enableFloat);
    await prefs.setString('app_language', languageNotifier.value.languageCode);

    // 自动合并生成实际最终使用的 System Prompt 并同步原生
    String finalSystemPrompt = _promptController.text.trim();
    if (finalSystemPrompt.isEmpty) {
      StyleConfig curStyle = _styleConfigs.firstWhere(
        (s) => s.name == _selectedStyle,
        orElse: () => StyleConfig(name: 'Standard', displayName: '标准 (Standard)', prompt: '', isBuiltIn: true),
      );
      
      String targetLangPrompt = LLMApiClient._getTargetLanguagePrompt(_selectedLang);
      String stylePrompt = curStyle.prompt.trim();

      finalSystemPrompt = '''You are a professional and accurate translator. Translate the text provided by the user into the target language.

Target Language settings:
$targetLangPrompt
${stylePrompt.isNotEmpty ? '\n$stylePrompt' : ''}

CRITICAL RULES:
1. Output ONLY the raw translated text content. Do NOT wrap it in Markdown code blocks (do not use ```), and do NOT add any introductions, explanations, prefixes, or notes.
2. Keep the exact same formatting, paragraphs, spaces, and punctuation of the original text.
3. If the input text is already in the target language (or the main language matching it), translate it back to the other major language (e.g. if target language is Chinese, and the input is Chinese, translate it to English; if target language is English, and input is English, translate it to Chinese).''';
    }

    // 将配置序列化同步给 Native (以便原生后台 Service 能直接读取当前激活的这套 API 字段)
    try {
      await _channel.invokeMethod('updateSettings', {
        'apiUrl': _apiUrlController.text,
        'apiKey': _apiKeyController.text,
        'modelName': _modelController.text,
        'systemPrompt': finalSystemPrompt, // 这里同步拼接好风格的终极 Prompt
        'targetLanguage': _selectedLang,
        'translationStyle': _selectedStyle,
        'appLanguage': languageNotifier.value.languageCode,
        'enableFloat': _enableFloat,
      });

      // 根据开关决定让原生显示/隐藏悬浮球
      await _channel.invokeMethod('toggleFloatBall', {'enable': _enableFloat});
    } on PlatformException catch (e) {
      debugPrint('Sync settings to native failed: ${e.message}');
    }

    final local = AppLocalizations.of(context)!;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          local.toastSaved,
          style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 14),
        ),
        backgroundColor: const Color(0xFFA855F7),
        behavior: SnackBarBehavior.floating,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
      ),
    );
  }

  // 检查权限
  Future<void> _checkSystemPermissions() async {
    try {
      final Map? result = await _channel.invokeMethod('checkPermissions');
      if (result != null) {
        setState(() {
          _floatPermGranted = result['floatGranted'] ?? false;
          _accessActive = result['accessibilityActive'] ?? false;
        });
      }
      final bool batteryIgnoring = await _channel.invokeMethod('checkBatteryOptimization');
      setState(() {
        _batteryOptGranted = batteryIgnoring;
      });
    } on PlatformException catch (e) {
      debugPrint('Check permissions failed: ${e.message}');
    }
  }

  // 请求权限
  Future<void> _requestPermission(String type) async {
    try {
      await _channel.invokeMethod('requestPermission', {'type': type});
      Future.delayed(const Duration(seconds: 1), _checkSystemPermissions);
    } on PlatformException catch (e) {
      debugPrint('Request permission failed: ${e.message}');
    }
  }

  // 请求忽略电池限制
  Future<void> _requestBatteryOptimization() async {
    try {
      await _channel.invokeMethod('requestIgnoreBatteryOptimization');
      Future.delayed(const Duration(seconds: 1), _checkSystemPermissions);
    } on PlatformException catch (e) {
      debugPrint('Request battery optimization failed: ${e.message}');
    }
  }

  // 打开自启动设置
  Future<void> _openAutoStartSettings() async {
    try {
      await _channel.invokeMethod('openAutoStartSettings');
    } on PlatformException catch (e) {
      debugPrint('Open auto-start settings failed: ${e.message}');
    }
  }

  // 自动根据目标语种和翻译风格拼装终极 System Prompt
  String _assembleSystemPrompt() {
    String finalSystemPrompt = _promptController.text.trim();
    if (finalSystemPrompt.isEmpty) {
      StyleConfig curStyle = _styleConfigs.firstWhere(
        (s) => s.name == _selectedStyle,
        orElse: () => StyleConfig(name: 'Standard', displayName: '标准 (Standard)', prompt: '', isBuiltIn: true),
      );
      
      String targetLangPrompt = LLMApiClient._getTargetLanguagePrompt(_selectedLang);
      String stylePrompt = curStyle.prompt.trim();

      finalSystemPrompt = '''You are a professional and accurate translator. Translate the text provided by the user into the target language.

Target Language settings:
$targetLangPrompt
${stylePrompt.isNotEmpty ? '\n$stylePrompt' : ''}

CRITICAL RULES:
1. Output ONLY the raw translated text content. Do NOT wrap it in Markdown code blocks (do not use ```), and do NOT add any introductions, explanations, prefixes, or notes.
2. Keep the exact same formatting, paragraphs, spaces, and punctuation of the original text.
3. If the input text is already in the target language (or the main language matching it), translate it back to the other major language (e.g. if target language is Chinese, and the input is Chinese, translate it to English; if target language is English, and input is English, translate it to Chinese).''';
    }
    return finalSystemPrompt;
  }

  // 测试翻译接口
  Future<void> _testConnection() async {
    if (_apiUrlController.text.isEmpty || _apiKeyController.text.isEmpty) {
      _showToast(AppLocalizations.of(context)!.toastFailed);
      return;
    }

    setState(() {
      _isTesting = true;
      _testResult = '';
    });

    final local = AppLocalizations.of(context)!;
    try {
      String testText = _testInputController.text.trim();
      if (testText.isEmpty) {
        testText = 'Hello';
      }

      String translated = await LLMApiClient.translate(
        testText,
        apiUrl: _apiUrlController.text,
        apiKey: _apiKeyController.text,
        modelName: _modelController.text,
        targetLang: _selectedLang,
        translationStyle: _selectedStyle,
        systemPrompt: _assembleSystemPrompt(), // 采用统一终极拼装好的提示词测试
      );

      setState(() {
        _testResult = translated;
      });
      _showToast(local.getWithArgs('toast_test_success', [translated]));
    } catch (e) {
      String errStr = e.toString();
      String keyInfo = "";
      String rawKey = _apiKeyController.text.trim();
      if (rawKey.isNotEmpty) {
        String cleanKey = LLMApiClient._cleanApiKey(rawKey);
        int len = cleanKey.length;
        String start = len > 3 ? cleanKey.substring(0, 3) : cleanKey;
        String end = len > 3 ? cleanKey.substring(len - 3) : cleanKey;
        keyInfo = "\n(发送的 Key 长度: $len, 开头: '$start', 结尾: '$end')";
      }
      setState(() {
        _testResult = 'Error: $e$keyInfo';
      });
      _showToast(local.getWithArgs('toast_test_failed', [e.toString() + keyInfo]));
    } finally {
      setState(() {
        _isTesting = false;
      });
    }
  }

  // 获取可用模型列表 (自动探测)
  Future<void> _fetchModelList() async {
    String url = _apiUrlController.text.trim();
    String key = _apiKeyController.text.trim();

    if (url.isEmpty || key.isEmpty) {
      _showToast(AppLocalizations.of(context)!.toastFailed);
      return;
    }

    setState(() {
      _isFetchingModels = true;
    });

    final local = AppLocalizations.of(context)!;
    try {
      List<String> models = await LLMApiClient.fetchAvailableModels(url, key);
      
      if (models.isEmpty) {
        _showToast(local.modelListEmpty);
        return;
      }
      
      // 弹出毛玻璃 BottomSheet 供用户选择
      _showModelPickerBottomSheet(models);
    } catch (e) {
      String keyInfo = "";
      String rawKey = _apiKeyController.text.trim();
      if (rawKey.isNotEmpty) {
        String cleanKey = LLMApiClient._cleanApiKey(rawKey);
        int len = cleanKey.length;
        String start = len > 3 ? cleanKey.substring(0, 3) : cleanKey;
        String end = len > 3 ? cleanKey.substring(len - 3) : cleanKey;
        keyInfo = "\n(发送的 Key 长度: $len, 开头: '$start', 结尾: '$end')";
      }
      _showToast(local.getWithArgs('toast_test_failed', [e.toString() + keyInfo]));
    } finally {
      setState(() {
        _isFetchingModels = false;
      });
    }
  }

  void _showToast(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          message,
          style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 13),
        ),
        backgroundColor: const Color(0xFFA855F7),
        behavior: SnackBarBehavior.floating,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        duration: const Duration(seconds: 3),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final local = AppLocalizations.of(context)!;

    return Scaffold(
      body: Container(
        width: double.infinity,
        height: double.infinity,
        decoration: const BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: [Color(0xFF0F0A1A), Color(0xFF1E1535)],
          ),
        ),
        child: SafeArea(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // 顶部标题和多语言切换
                _buildHeader(local),
                const SizedBox(height: 20),

                // 权限状态卡片
                _buildPermissionCard(local),
                const SizedBox(height: 20),

                // 核心配置卡片 (内含多供应商和模型获取)
                _buildSettingsCard(local),
                const SizedBox(height: 20),

                // 测试翻译卡片
                _buildTestCard(local),
                const SizedBox(height: 40),
              ],
            ),
          ),
        ),
      ),
    );
  }

  // 标题与中英语言切换 Toggle
  Widget _buildHeader(AppLocalizations local) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              local.appTitle,
              style: const TextStyle(
                fontSize: 24,
                fontWeight: FontWeight.bold,
                color: Colors.white,
                letterSpacing: 1.2,
                shadows: [
                  Shadow(color: Color(0xFFA855F7), blurRadius: 10),
                ],
              ),
            ),
            const SizedBox(height: 4),
            Text(
              'v1.0.1 (Android Backend)',
              style: TextStyle(fontSize: 12, color: Colors.white.withOpacity(0.85)),
            ),
          ],
        ),
        _buildGlassButton(
          onPressed: () {
            String current = languageNotifier.value.languageCode;
            String next = current == 'zh' ? 'en' : 'zh';
            languageNotifier.value = Locale(next);
            SharedPreferences.getInstance().then((prefs) {
              prefs.setString('app_language', next);
            });
            // 同步原生
            _channel.invokeMethod('updateSettings', {
              'apiUrl': _apiUrlController.text,
              'apiKey': _apiKeyController.text,
              'modelName': _modelController.text,
              'systemPrompt': _promptController.text,
              'targetLanguage': _selectedLang,
              'translationStyle': _selectedStyle,
              'appLanguage': next,
              'enableFloat': _enableFloat,
            });
          },
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.language, size: 16, color: Colors.white),
              const SizedBox(width: 6),
              Text(
                languageNotifier.value.languageCode == 'zh' ? 'English' : '简体中文',
                style: const TextStyle(fontSize: 12, color: Colors.white),
              ),
            ],
          ),
        ),
      ],
    );
  }

  // 权限状态与开关配置
  Widget _buildPermissionCard(AppLocalizations local) {
    return _buildGlassCard(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const Icon(Icons.security, color: Color(0xFFA855F7)),
                const SizedBox(width: 8),
                Text(
                  local.permTitle,
                  style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold, color: Colors.white),
                ),
              ],
            ),
            const Divider(color: Colors.white10, height: 24),
            _buildPermissionRow(
              title: local.floatPermission,
              granted: _floatPermGranted,
              desc: local.permFloatDesc,
              onGrant: () => _requestPermission('float'),
              local: local,
            ),
            const SizedBox(height: 12),
            _buildPermissionRow(
              title: local.accessibilityStatus,
              granted: _accessActive,
              desc: local.permAccessDesc,
              onGrant: () => _requestPermission('accessibility'),
              local: local,
            ),
            const SizedBox(height: 12),
            _buildPermissionRow(
              title: local.batteryOptimization,
              granted: _batteryOptGranted,
              desc: local.batteryOptDesc,
              onGrant: _requestBatteryOptimization,
              local: local,
              customStatusText: _batteryOptGranted ? local.batteryOptGranted : local.batteryOptDenied,
            ),
            const SizedBox(height: 12),
            _buildPermissionRow(
              title: local.autoStartSetting,
              granted: false,
              desc: local.autoStartDesc,
              onGrant: _openAutoStartSettings,
              local: local,
              showStatus: false,
              btnText: local.goSettingBtn,
            ),
            const Divider(color: Colors.white10, height: 24),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  local.floatSwitch,
                  style: const TextStyle(fontSize: 15, color: Colors.white),
                ),
                Switch(
                  value: _enableFloat,
                  activeColor: const Color(0xFFA855F7),
                  onChanged: (val) {
                    if (val && (!_floatPermGranted || !_accessActive)) {
                      _showPermissionDialog(local);
                    } else {
                      setState(() {
                        _enableFloat = val;
                      });
                      _saveSettings();
                    }
                  },
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  // 权限项详情行
  Widget _buildPermissionRow({
    required String title,
    required bool granted,
    required String desc,
    required VoidCallback onGrant,
    required AppLocalizations local,
    bool showStatus = true,
    String? customStatusText,
    Color? customStatusColor,
    String? btnText,
  }) {
    final statusText = customStatusText ?? (granted ? local.permissionGranted : local.permissionDenied);
    final statusColor = customStatusColor ?? (granted ? Colors.greenAccent : Colors.redAccent);
    final statusBgColor = granted ? Colors.green.withOpacity(0.2) : Colors.red.withOpacity(0.2);

    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(
                    title,
                    style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w500, color: Colors.white),
                  ),
                  if (showStatus) ...[
                    const SizedBox(width: 8),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                      decoration: BoxDecoration(
                        color: statusBgColor,
                        borderRadius: BorderRadius.circular(4),
                      ),
                      child: Text(
                        statusText,
                        style: TextStyle(
                          fontSize: 10,
                          color: statusColor,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                    ),
                  ],
                ],
              ),
              const SizedBox(height: 4),
              Text(
                desc,
                style: TextStyle(fontSize: 11, color: Colors.white.withOpacity(0.8)),
              ),
            ],
          ),
        ),
        if (!granted)
          TextButton(
            onPressed: onGrant,
            style: TextButton.styleFrom(
              foregroundColor: const Color(0xFFA855F7),
              padding: const EdgeInsets.symmetric(horizontal: 12),
            ),
            child: Text(btnText ?? local.permGrantBtn),
          ),
      ],
    );
  }

  // API 核心配置项 (多供应商配置，与 PC 端完全一致)
  Widget _buildSettingsCard(AppLocalizations local) {
    // 校验当前选中是否为系统内置供应商（内置供应商不允许删除或重命名）
    bool isBuiltIn = _selectedModel == 'DeepSeek 大模型' ||
        _selectedModel == '小米大模型' ||
        _selectedModel == 'OpenAI (ChatGPT)' ||
        _selectedModel == 'Google (Gemini)' ||
        _selectedModel == '自定义模型';

    bool isStyleBuiltIn = _styleConfigs.isEmpty || _styleConfigs.firstWhere(
          (s) => s.name == _selectedStyle,
          orElse: () => StyleConfig(name: 'Standard', displayName: 'Standard', prompt: '', isBuiltIn: true),
        ).isBuiltIn;

    return _buildGlassCard(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const Icon(Icons.settings, color: Color(0xFF6366F1)),
                const SizedBox(width: 8),
                Text(
                  local.apiSettings,
                  style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold, color: Colors.white),
                ),
              ],
            ),
            const Divider(color: Colors.white10, height: 24),

            // 1. 大模型供应商切换行
            Text(
              local.providerName,
              style: TextStyle(fontSize: 12, color: Colors.white.withOpacity(0.85)),
            ),
            const SizedBox(height: 6),
            Row(
              children: [
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: 12),
                    height: 38,
                    decoration: BoxDecoration(
                      color: Colors.white.withOpacity(0.04),
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(color: Colors.white10),
                    ),
                    child: DropdownButtonHideUnderline(
                      child: DropdownButton<String>(
                        value: _selectedModel,
                        isExpanded: true,
                        dropdownColor: const Color(0xFF1E1535),
                        items: _modelConfigs.map((m) {
                          return DropdownMenuItem<String>(
                            value: m.displayName,
                            child: Text(
                              m.displayName,
                              style: const TextStyle(color: Colors.white, fontSize: 14),
                            ),
                          );
                        }).toList(),
                        onChanged: (val) {
                          if (val != null && val != _selectedModel) {
                            setState(() {
                              // 先暂存当前编辑内容到旧供应商
                              _saveCurrentConfigToMemory(_selectedModel);
                              // 切换
                              _selectedModel = val;
                              // 加载新供应商内容
                              _loadConfigToUI(val);
                            });
                            _saveSettings();
                          }
                        },
                      ),
                    ),
                  ),
                ),
                const SizedBox(width: 6),
                // 新增按钮
                _buildMiniIconButton(
                  icon: Icons.add,
                  tooltip: local.addProvider,
                  onPressed: _showAddProviderDialog,
                ),
                const SizedBox(width: 4),
                // 修改重命名按钮
                _buildMiniIconButton(
                  icon: Icons.edit,
                  tooltip: local.renameProvider,
                  onPressed: isBuiltIn ? null : _showRenameProviderDialog,
                  opacity: isBuiltIn ? 0.35 : 1.0,
                ),
                const SizedBox(width: 4),
                // 删除按钮
                _buildMiniIconButton(
                  icon: Icons.delete_outline,
                  tooltip: local.deleteProvider,
                  onPressed: isBuiltIn ? null : _showDeleteProviderConfirm,
                  opacity: isBuiltIn ? 0.35 : 1.0,
                ),
              ],
            ),
            const SizedBox(height: 16),

            // 2. 接口地址
            _buildTextField(
              controller: _apiUrlController,
              label: local.apiUrl,
              hint: 'https://api.deepseek.com',
            ),
            const SizedBox(height: 16),

            // 3. API Key 与一键粘贴
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Expanded(
                  child: _buildTextField(
                    controller: _apiKeyController,
                    label: local.apiKey,
                    hint: 'sk-xxxxxxxxxxxxxxxx',
                    obscureText: true,
                  ),
                ),
                const SizedBox(width: 8),
                _buildGlassButton(
                  onPressed: () async {
                    ClipboardData? data = await Clipboard.getData(Clipboard.kTextPlain);
                    if (data != null && data.text != null) {
                      _apiKeyController.text = data.text!;
                      _showToast(local.appLang == 'zh' ? '已粘贴 Key' : 'Key Pasted');
                    }
                  },
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(Icons.paste, size: 14, color: Color(0xFFA855F7)),
                      const SizedBox(width: 4),
                      Text(local.pasteBtn, style: const TextStyle(fontSize: 12, color: Colors.white)),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),

            // 4. 模型名称与自动探测获取模型按钮并列
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Expanded(
                  child: _buildTextField(
                    controller: _modelController,
                    label: local.modelName,
                    hint: 'deepseek-chat',
                  ),
                ),
                const SizedBox(width: 8),
                _buildGlassButton(
                  onPressed: _isFetchingModels ? null : _fetchModelList,
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      _isFetchingModels
                          ? const SizedBox(
                              width: 14,
                              height: 14,
                              child: CircularProgressIndicator(strokeWidth: 1.5, color: Color(0xFFA855F7)),
                            )
                          : const Icon(Icons.autorenew, size: 14, color: Color(0xFFA855F7)),
                      const SizedBox(width: 4),
                      Text(
                        _isFetchingModels ? local.fetching : local.fetchModels,
                        style: const TextStyle(fontSize: 12, color: Colors.white),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),

            // 5. 目标语种下拉框
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  local.targetLang,
                  style: TextStyle(fontSize: 12, color: Colors.white.withOpacity(0.85)),
                ),
                const SizedBox(height: 6),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 12),
                  decoration: BoxDecoration(
                    color: Colors.white.withOpacity(0.04),
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: Colors.white10),
                  ),
                  child: DropdownButtonHideUnderline(
                    child: DropdownButton<String>(
                      value: _selectedLang,
                      isExpanded: true,
                      dropdownColor: const Color(0xFF1E1535),
                      items: const [
                        DropdownMenuItem(value: 'Auto', child: Text('Auto (中英互译)', style: TextStyle(color: Colors.white, fontSize: 14))),
                        DropdownMenuItem(value: 'Chinese', child: Text('Chinese (简体中文)', style: TextStyle(color: Colors.white, fontSize: 14))),
                        DropdownMenuItem(value: 'English', child: Text('English (英文)', style: TextStyle(color: Colors.white, fontSize: 14))),
                        DropdownMenuItem(value: 'Japanese', child: Text('Japanese (日本語)', style: TextStyle(color: Colors.white, fontSize: 14))),
                        DropdownMenuItem(value: 'Korean', child: Text('Korean (한국어)', style: TextStyle(color: Colors.white, fontSize: 14))),
                        DropdownMenuItem(value: 'French', child: Text('French (Français)', style: TextStyle(color: Colors.white, fontSize: 14))),
                        DropdownMenuItem(value: 'German', child: Text('German (Deutsch)', style: TextStyle(color: Colors.white, fontSize: 14))),
                        DropdownMenuItem(value: 'Spanish', child: Text('Spanish (Español)', style: TextStyle(color: Colors.white, fontSize: 14))),
                      ],
                      onChanged: (val) {
                        if (val != null) {
                          setState(() {
                            _selectedLang = val;
                          });
                          _saveSettings();
                        }
                      },
                    ),
                  ),
                ),
              ],
            ),
            
            const SizedBox(height: 16),
            // 翻译风格配置段 (支持像供应商一样的增删改查高级功能)
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  local.translationStyle,
                  style: TextStyle(fontSize: 12, color: Colors.white.withOpacity(0.85)),
                ),
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 12),
                        height: 38,
                        decoration: BoxDecoration(
                          color: Colors.white.withOpacity(0.04),
                          borderRadius: BorderRadius.circular(8),
                          border: Border.all(color: Colors.white10),
                        ),
                        child: DropdownButtonHideUnderline(
                          child: DropdownButton<String>(
                            value: _selectedStyle,
                            isExpanded: true,
                            dropdownColor: const Color(0xFF1E1535),
                            items: _styleConfigs.map((s) {
                              return DropdownMenuItem<String>(
                                value: s.name,
                                child: Text(
                                  s.displayName,
                                  style: const TextStyle(color: Colors.white, fontSize: 14),
                                ),
                              );
                            }).toList(),
                            onChanged: (val) {
                              if (val != null) {
                                setState(() {
                                  _selectedStyle = val;
                                });
                                _saveSettings();
                              }
                            },
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(width: 6),
                    // 新增翻译风格按钮
                    _buildMiniIconButton(
                      icon: Icons.add,
                      tooltip: local.appLang == 'zh' ? '新增翻译风格' : 'Add Style',
                      onPressed: _showAddStyleDialog,
                    ),
                    const SizedBox(width: 4),
                    // 修改翻译风格按钮
                    _buildMiniIconButton(
                      icon: Icons.edit,
                      tooltip: local.appLang == 'zh' ? '编辑翻译风格' : 'Edit Style',
                      onPressed: isStyleBuiltIn ? null : _showEditStyleDialog,
                      opacity: isStyleBuiltIn ? 0.35 : 1.0,
                    ),
                    const SizedBox(width: 4),
                    // 删除翻译风格按钮
                    _buildMiniIconButton(
                      icon: Icons.delete_outline,
                      tooltip: local.appLang == 'zh' ? '删除翻译风格' : 'Delete Style',
                      onPressed: isStyleBuiltIn ? null : _showDeleteStyleConfirm,
                      opacity: isStyleBuiltIn ? 0.35 : 1.0,
                    ),
                  ],
                ),
              ],
            ),

            const SizedBox(height: 16),
            _buildTextField(
              controller: _promptController,
              label: local.systemPrompt,
              hint: local.systemPromptHint,
              maxLines: 3,
            ),
            const SizedBox(height: 24),
            // 保存并应用
            SizedBox(
              width: double.infinity,
              height: 48,
              child: Container(
                decoration: BoxDecoration(
                  gradient: const LinearGradient(
                    colors: [Color(0xFFA855F7), Color(0xFF6366F1)],
                  ),
                  borderRadius: BorderRadius.circular(8),
                  boxShadow: [
                    BoxShadow(
                      color: const Color(0xFFA855F7).withOpacity(0.3),
                      blurRadius: 10,
                      offset: const Offset(0, 4),
                    ),
                  ],
                ),
                child: ElevatedButton(
                  onPressed: _saveSettings,
                  style: ElevatedButton.styleFrom(
                    backgroundColor: Colors.transparent,
                    shadowColor: Colors.transparent,
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                  ),
                  child: Text(
                    local.saveBtn,
                    style: const TextStyle(fontWeight: FontWeight.bold, color: Colors.white),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  // 迷你毛玻璃操作图标按钮
  Widget _buildMiniIconButton({
    required IconData icon,
    required String tooltip,
    required VoidCallback? onPressed,
    double opacity = 1.0,
  }) {
    return Opacity(
      opacity: opacity,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(8),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 5, sigmaY: 5),
          child: Container(
            width: 36,
            height: 36,
            decoration: BoxDecoration(
              color: Colors.white.withOpacity(0.05),
              border: Border.all(color: Colors.white.withOpacity(0.08)),
              borderRadius: BorderRadius.circular(8),
            ),
            child: IconButton(
              icon: Icon(icon, size: 16, color: const Color(0xFFA855F7)),
              padding: EdgeInsets.zero,
              tooltip: tooltip,
              onPressed: onPressed,
            ),
          ),
        ),
      ),
    );
  }

  // 新增翻译风格弹窗
  void _showAddStyleDialog() {
    final local = AppLocalizations.of(context)!;
    final nameController = TextEditingController();
    final promptController = TextEditingController();

    showDialog(
      context: context,
      builder: (ctx) {
        return _buildGlassDialog(
          title: local.appLang == 'zh' ? '新增翻译风格' : 'Add Style',
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextField(
                  controller: nameController,
                  autofocus: true,
                  style: const TextStyle(color: Colors.white, fontSize: 14),
                  decoration: InputDecoration(
                    labelText: local.appLang == 'zh' ? '风格名称' : 'Style Name',
                    labelStyle: const TextStyle(color: Colors.white70, fontSize: 13),
                    hintText: local.appLang == 'zh' ? '例如：古风雅韵' : 'e.g. Ancient Chinese Style',
                    hintStyle: TextStyle(color: Colors.white.withOpacity(0.3), fontSize: 13),
                    filled: true,
                    fillColor: Colors.white.withOpacity(0.03),
                    contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                    enabledBorder: OutlineInputBorder(borderSide: const BorderSide(color: Colors.white10), borderRadius: BorderRadius.circular(8)),
                    focusedBorder: OutlineInputBorder(borderSide: const BorderSide(color: Color(0xFFA855F7)), borderRadius: BorderRadius.circular(8)),
                  ),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: promptController,
                  maxLines: 4,
                  style: const TextStyle(color: Colors.white, fontSize: 14),
                  decoration: InputDecoration(
                    labelText: local.appLang == 'zh' ? '提示词指令 (Prompt)' : 'System Command (Prompt)',
                    labelStyle: const TextStyle(color: Colors.white70, fontSize: 13),
                    hintText: local.appLang == 'zh' ? '例如：请将文字翻译为中国古代诗词或文言文的韵味风格。' : 'e.g. Translate text with a touch of ancient Chinese poetry.',
                    hintStyle: TextStyle(color: Colors.white.withOpacity(0.3), fontSize: 13),
                    filled: true,
                    fillColor: Colors.white.withOpacity(0.03),
                    contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                    enabledBorder: OutlineInputBorder(borderSide: const BorderSide(color: Colors.white10), borderRadius: BorderRadius.circular(8)),
                    focusedBorder: OutlineInputBorder(borderSide: const BorderSide(color: Color(0xFFA855F7)), borderRadius: BorderRadius.circular(8)),
                  ),
                ),
              ],
            ),
          ),
          onConfirm: () {
            String name = nameController.text.trim();
            String prompt = promptController.text.trim();
            if (name.isEmpty || prompt.isEmpty) {
              _showToast(local.appLang == 'zh' ? '风格名称与提示词不能为空' : 'Name and prompt cannot be empty');
              return;
            }
            String styleId = 'custom_${DateTime.now().millisecondsSinceEpoch}';
            if (_styleConfigs.any((s) => s.displayName.toLowerCase() == name.toLowerCase())) {
              _showToast(local.appLang == 'zh' ? '已存在同名翻译风格' : 'Style name already exists');
              return;
            }
            setState(() {
              var newStyle = StyleConfig(
                name: styleId,
                displayName: name,
                prompt: prompt,
                isBuiltIn: false,
              );
              _styleConfigs.add(newStyle);
              _selectedStyle = styleId;
            });
            Navigator.pop(ctx);
            _saveSettings();
          },
          onCancel: () => Navigator.pop(ctx),
          local: local,
        );
      },
    );
  }

  // 编辑翻译风格弹窗
  void _showEditStyleDialog() {
    final local = AppLocalizations.of(context)!;
    int idx = _styleConfigs.indexWhere((s) => s.name == _selectedStyle);
    if (idx == -1 || _styleConfigs[idx].isBuiltIn) return;

    final nameController = TextEditingController(text: _styleConfigs[idx].displayName);
    final promptController = TextEditingController(text: _styleConfigs[idx].prompt);

    showDialog(
      context: context,
      builder: (ctx) {
        return _buildGlassDialog(
          title: local.appLang == 'zh' ? '编辑翻译风格' : 'Edit Style',
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextField(
                  controller: nameController,
                  style: const TextStyle(color: Colors.white, fontSize: 14),
                  decoration: InputDecoration(
                    labelText: local.appLang == 'zh' ? '风格名称' : 'Style Name',
                    labelStyle: const TextStyle(color: Colors.white70, fontSize: 13),
                    filled: true,
                    fillColor: Colors.white.withOpacity(0.03),
                    contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                    enabledBorder: OutlineInputBorder(borderSide: const BorderSide(color: Colors.white10), borderRadius: BorderRadius.circular(8)),
                    focusedBorder: OutlineInputBorder(borderSide: const BorderSide(color: Color(0xFFA855F7)), borderRadius: BorderRadius.circular(8)),
                  ),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: promptController,
                  maxLines: 4,
                  style: const TextStyle(color: Colors.white, fontSize: 14),
                  decoration: InputDecoration(
                    labelText: local.appLang == 'zh' ? '提示词指令 (Prompt)' : 'System Command (Prompt)',
                    labelStyle: const TextStyle(color: Colors.white70, fontSize: 13),
                    filled: true,
                    fillColor: Colors.white.withOpacity(0.03),
                    contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                    enabledBorder: OutlineInputBorder(borderSide: const BorderSide(color: Colors.white10), borderRadius: BorderRadius.circular(8)),
                    focusedBorder: OutlineInputBorder(borderSide: const BorderSide(color: Color(0xFFA855F7)), borderRadius: BorderRadius.circular(8)),
                  ),
                ),
              ],
            ),
          ),
          onConfirm: () {
            String name = nameController.text.trim();
            String prompt = promptController.text.trim();
            if (name.isEmpty || prompt.isEmpty) {
              _showToast(local.appLang == 'zh' ? '风格名称与提示词不能为空' : 'Name and prompt cannot be empty');
              return;
            }
            setState(() {
              _styleConfigs[idx].displayName = name;
              _styleConfigs[idx].prompt = prompt;
            });
            Navigator.pop(ctx);
            _saveSettings();
          },
          onCancel: () => Navigator.pop(ctx),
          local: local,
        );
      },
    );
  }

  // 删除翻译风格二次确认
  void _showDeleteStyleConfirm() {
    final local = AppLocalizations.of(context)!;
    int idx = _styleConfigs.indexWhere((s) => s.name == _selectedStyle);
    if (idx == -1 || _styleConfigs[idx].isBuiltIn) return;

    showDialog(
      context: context,
      builder: (ctx) {
        return AlertDialog(
          backgroundColor: const Color(0xFF1E1535),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
          title: Text(local.appLang == 'zh' ? '删除翻译风格' : 'Delete Style', style: const TextStyle(color: Colors.white)),
          content: Text(
            local.appLang == 'zh' 
                ? '确定要删除翻译风格 ${_styleConfigs[idx].displayName} 吗？' 
                : 'Are you sure you want to delete ${_styleConfigs[idx].displayName}?',
            style: const TextStyle(color: Colors.white70, fontSize: 14),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx), child: Text(local.cancel, style: const TextStyle(color: Colors.white38))),
            TextButton(
              onPressed: () {
                setState(() {
                  _styleConfigs.removeAt(idx);
                  _selectedStyle = 'Standard';
                });
                Navigator.pop(ctx);
                _saveSettings();
              },
              child: Text(local.confirm, style: const TextStyle(color: Colors.redAccent, fontWeight: FontWeight.bold)),
            ),
          ],
        );
      },
    );
  }

  // 新增供应商弹窗
  void _showAddProviderDialog() {
    final local = AppLocalizations.of(context)!;
    final controller = TextEditingController();

    showDialog(
      context: context,
      builder: (ctx) {
        return _buildGlassDialog(
          title: local.addProvider,
          content: TextField(
            controller: controller,
            autofocus: true,
            style: const TextStyle(color: Colors.white, fontSize: 14),
            decoration: InputDecoration(
              hintText: local.providerNameHint,
              hintStyle: TextStyle(color: Colors.white.withOpacity(0.7), fontSize: 13),
              filled: true,
              fillColor: Colors.white.withOpacity(0.03),
              contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
              enabledBorder: OutlineInputBorder(borderSide: const BorderSide(color: Colors.white10), borderRadius: BorderRadius.circular(8)),
              focusedBorder: OutlineInputBorder(borderSide: const BorderSide(color: Color(0xFFA855F7)), borderRadius: BorderRadius.circular(8)),
            ),
          ),
          onConfirm: () {
            String name = controller.text.trim();
            if (name.isEmpty) {
              _showToast(local.emptyProviderErr);
              return;
            }
            if (_modelConfigs.any((m) => m.displayName.toLowerCase() == name.toLowerCase())) {
              _showToast(local.duplicateProviderErr);
              return;
            }
            setState(() {
              // 新建配置，直接置为空白，拒绝残留并继承上一个大模型的垃圾信息
              var newCfg = ModelConfig(
                displayName: name,
                apiUrl: '',
                apiKey: '',
                modelName: 'deepseek-chat',
              );
              _modelConfigs.add(newCfg);
              _selectedModel = name;
              _loadConfigToUI(name); // 优雅地同步将 UI 输入框净空重置
            });
            Navigator.pop(ctx);
            _saveSettings();
          },
          onCancel: () => Navigator.pop(ctx),
          local: local,
        );
      },
    );
  }

  // 重命名供应商弹窗
  void _showRenameProviderDialog() {
    final local = AppLocalizations.of(context)!;
    final controller = TextEditingController(text: _selectedModel);

    showDialog(
      context: context,
      builder: (ctx) {
        return _buildGlassDialog(
          title: local.renameProvider,
          content: TextField(
            controller: controller,
            autofocus: true,
            style: const TextStyle(color: Colors.white, fontSize: 14),
            decoration: InputDecoration(
              hintText: local.providerNameHint,
              hintStyle: TextStyle(color: Colors.white.withOpacity(0.7), fontSize: 13),
              filled: true,
              fillColor: Colors.white.withOpacity(0.03),
              contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
              enabledBorder: OutlineInputBorder(borderSide: const BorderSide(color: Colors.white10), borderRadius: BorderRadius.circular(8)),
              focusedBorder: OutlineInputBorder(borderSide: const BorderSide(color: Color(0xFFA855F7)), borderRadius: BorderRadius.circular(8)),
            ),
          ),
          onConfirm: () {
            String name = controller.text.trim();
            if (name.isEmpty) {
              _showToast(local.emptyProviderErr);
              return;
            }
            if (name == _selectedModel) {
              Navigator.pop(ctx);
              return;
            }
            if (_modelConfigs.any((m) => m.displayName.toLowerCase() == name.toLowerCase())) {
              _showToast(local.duplicateProviderErr);
              return;
            }
            setState(() {
              int idx = _modelConfigs.indexWhere((m) => m.displayName == _selectedModel);
              if (idx != -1) {
                _modelConfigs[idx].displayName = name;
                _selectedModel = name;
              }
            });
            Navigator.pop(ctx);
            _saveSettings();
          },
          onCancel: () => Navigator.pop(ctx),
          local: local,
        );
      },
    );
  }

  // 删除供应商二次确认
  void _showDeleteProviderConfirm() {
    final local = AppLocalizations.of(context)!;
    showDialog(
      context: context,
      builder: (ctx) {
        return AlertDialog(
          backgroundColor: const Color(0xFF1E1535),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
          title: Text(local.deleteProvider, style: const TextStyle(color: Colors.white)),
          content: Text(
            local.appLang == 'zh' ? '确定要删除供应商 $_selectedModel 吗？' : 'Are you sure you want to delete $_selectedModel?',
            style: const TextStyle(color: Colors.white70, fontSize: 14),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx), child: Text(local.cancel, style: const TextStyle(color: Colors.white38))),
            TextButton(
              onPressed: () {
                setState(() {
                  _modelConfigs.removeWhere((m) => m.displayName == _selectedModel);
                  _selectedModel = 'DeepSeek 大模型';
                  _loadConfigToUI(_selectedModel);
                });
                Navigator.pop(ctx);
                _saveSettings();
              },
              child: Text(local.confirm, style: const TextStyle(color: Colors.redAccent, fontWeight: FontWeight.bold)),
            ),
          ],
        );
      },
    );
  }

  // 弹出毛玻璃 BottomSheet 展现模型列表并允许用户点击点选
  void _showModelPickerBottomSheet(List<String> models) {
    final local = AppLocalizations.of(context)!;
    showModalBottomSheet(
      context: context,
      backgroundColor: Colors.transparent,
      isScrollControlled: true,
      builder: (ctx) {
        return ClipRRect(
          borderRadius: const BorderRadius.vertical(top: Radius.circular(20)),
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: 15, sigmaY: 15),
            child: Container(
              height: MediaQuery.of(context).size.height * 0.55,
              decoration: BoxDecoration(
                color: const Color(0xFF1E1535).withOpacity(0.85),
                border: Border(top: BorderSide(color: Colors.white.withOpacity(0.12), width: 1.0)),
              ),
              child: Column(
                children: [
                  // 顶部小横条指示
                  Container(
                    margin: const EdgeInsets.symmetric(vertical: 12),
                    width: 36,
                    height: 4,
                    decoration: BoxDecoration(color: Colors.white24, borderRadius: BorderRadius.circular(2)),
                  ),
                  Text(
                    local.modelListTitle,
                    style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold, color: Colors.white),
                  ),
                  const SizedBox(height: 8),
                  const Divider(color: Colors.white10),
                  Expanded(
                    child: ListView.builder(
                      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                      itemCount: models.length,
                      itemBuilder: (c, idx) {
                        final mName = models[idx];
                        return Container(
                          margin: const EdgeInsets.symmetric(vertical: 4),
                          decoration: BoxDecoration(
                            color: _modelController.text == mName
                                ? const Color(0xFFA855F7).withOpacity(0.2)
                                : Colors.white.withOpacity(0.03),
                            borderRadius: BorderRadius.circular(8),
                            border: Border.all(
                              color: _modelController.text == mName
                                  ? const Color(0xFFA855F7).withOpacity(0.5)
                                  : Colors.transparent,
                            ),
                          ),
                          child: ListTile(
                            title: Text(mName, style: const TextStyle(color: Colors.white, fontSize: 14)),
                            trailing: _modelController.text == mName
                                ? const Icon(Icons.check, color: Color(0xFFA855F7), size: 18)
                                : null,
                            onTap: () {
                              setState(() {
                                _modelController.text = mName;
                              });
                              Navigator.pop(ctx);
                            },
                          ),
                        );
                      },
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }

  // 通用毛玻璃 Dialog 结构
  Widget _buildGlassDialog({
    required String title,
    required Widget content,
    required VoidCallback onConfirm,
    required VoidCallback onCancel,
    required AppLocalizations local,
  }) {
    return AlertDialog(
      backgroundColor: const Color(0xFF1E1535),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      title: Text(title, style: const TextStyle(color: Colors.white)),
      content: content,
      actions: [
        TextButton(onPressed: onCancel, child: Text(local.cancel, style: const TextStyle(color: Colors.white38))),
        TextButton(
          onPressed: onConfirm,
          child: Text(local.confirm, style: const TextStyle(color: Color(0xFFA855F7), fontWeight: FontWeight.bold)),
        ),
      ],
    );
  }

  // 接口测试卡片
  Widget _buildTestCard(AppLocalizations local) {
    return _buildGlassCard(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const Icon(Icons.bolt, color: Colors.amber),
                const SizedBox(width: 8),
                Text(
                  local.testBtn,
                  style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold, color: Colors.white),
                ),
              ],
            ),
            const Divider(color: Colors.white10, height: 24),
            _buildTextField(
              controller: _testInputController,
              label: local.testInputTitle,
              hint: local.testInputHint,
            ),
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              height: 40,
              child: OutlinedButton(
                onPressed: _isTesting ? null : _testConnection,
                style: OutlinedButton.styleFrom(
                  side: const BorderSide(color: Color(0xFFA855F7)),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                ),
                child: _isTesting
                    ? const SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(strokeWidth: 2, color: Color(0xFFA855F7)),
                      )
                    : Text(
                        local.testBtn,
                        style: const TextStyle(color: Color(0xFFA855F7), fontWeight: FontWeight.bold),
                      ),
              ),
            ),
            if (_testResult.isNotEmpty) ...[
              const SizedBox(height: 16),
              Text(
                local.testResultTitle,
                style: TextStyle(fontSize: 12, color: Colors.white.withOpacity(0.85)),
              ),
              const SizedBox(height: 6),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: Colors.white.withOpacity(0.02),
                  border: Border.all(color: Colors.white10),
                  borderRadius: BorderRadius.circular(8),
                ),
                child: SelectableText(
                  _testResult,
                  style: const TextStyle(fontSize: 13, color: Colors.white70, fontFamily: 'monospace'),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  // 通用毛玻璃卡片
  Widget _buildGlassCard({required Widget child}) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(16),
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: 10, sigmaY: 10),
        child: Container(
          decoration: BoxDecoration(
            color: Colors.white.withOpacity(0.04),
            border: Border.all(color: Colors.white.withOpacity(0.08), width: 1.0),
            borderRadius: BorderRadius.circular(16),
          ),
          child: child,
        ),
      ),
    );
  }

  // 通用输入框
  Widget _buildTextField({
    required TextEditingController controller,
    required String label,
    required String hint,
    bool obscureText = false,
    int maxLines = 1,
  }) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: TextStyle(fontSize: 12, color: Colors.white.withOpacity(0.85)),
        ),
        const SizedBox(height: 6),
        TextField(
          controller: controller,
          obscureText: obscureText,
          maxLines: maxLines,
          style: const TextStyle(fontSize: 14, color: Colors.white),
          decoration: InputDecoration(
            hintText: hint,
            hintStyle: TextStyle(fontSize: 13, color: Colors.white.withOpacity(0.80)),
            contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
            filled: true,
            fillColor: Colors.white.withOpacity(0.03),
            enabledBorder: OutlineInputBorder(
              borderSide: const BorderSide(color: Colors.white10),
              borderRadius: BorderRadius.circular(8),
            ),
            focusedBorder: OutlineInputBorder(
              borderSide: const BorderSide(color: Color(0xFFA855F7)),
              borderRadius: BorderRadius.circular(8),
            ),
          ),
        ),
      ],
    );
  }

  // 通用毛玻璃边框按钮
  Widget _buildGlassButton({required VoidCallback? onPressed, required Widget child}) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(20),
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: 5, sigmaY: 5),
        child: Container(
          decoration: BoxDecoration(
            color: Colors.white.withOpacity(0.06),
            border: Border.all(color: Colors.white.withOpacity(0.12), width: 1.0),
            borderRadius: BorderRadius.circular(20),
          ),
          child: Material(
            color: Colors.transparent,
            child: InkWell(
              onTap: onPressed,
              borderRadius: BorderRadius.circular(20),
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                child: child,
              ),
            ),
          ),
        ),
      ),
    );
  }

  // 引导权限弹窗
  void _showPermissionDialog(AppLocalizations local) {
    showDialog(
      context: context,
      builder: (BuildContext ctx) {
        return AlertDialog(
          backgroundColor: const Color(0xFF1E1535),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
          title: Text(local.permTitle, style: const TextStyle(color: Colors.white)),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (!_floatPermGranted) ...[
                Text(local.permFloatDesc, style: const TextStyle(color: Colors.white70, fontSize: 13)),
                const SizedBox(height: 12),
              ],
              if (!_accessActive) ...[
                Text(local.permAccessDesc, style: const TextStyle(color: Colors.white70, fontSize: 13)),
              ],
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx),
              child: Text(local.cancel, style: const TextStyle(color: Colors.white54)),
            ),
            TextButton(
              onPressed: () {
                Navigator.pop(ctx);
                if (!_floatPermGranted) {
                  _requestPermission('float');
                } else if (!_accessActive) {
                  _requestPermission('accessibility');
                }
              },
              child: Text(local.permGrantBtn, style: const TextStyle(color: Color(0xFFA855F7), fontWeight: FontWeight.bold)),
            ),
          ],
        );
      },
    );
  }
}

// 统一的 LLM 大模型翻译客户端
class LLMApiClient {
  
  // 安全过滤清洗 API Key
  static String _cleanApiKey(String key) {
    return key.codeUnits
        .where((cu) => cu >= 33 && cu <= 126)
        .map((cu) => String.fromCharCode(cu))
        .join()
        .trim();
  }

  static Future<String> translate(
    String text, {
    required String apiUrl,
    required String apiKey,
    required String modelName,
    required String targetLang,
    required String translationStyle,
    required String systemPrompt,
  }) async {
    if (text.trim().isEmpty) return '';

    apiUrl = apiUrl.trim();
    final cleanKey = _cleanApiKey(apiKey);

    // 采用底层 HttpClient 进行传输，完美规避 Dart 字符集 FormatException 问题！
    List<String> candidateUrls = [];
    if (apiUrl.endsWith('/chat/completions')) {
      candidateUrls.add(apiUrl);
    } else {
      String tempUrl = apiUrl;
      if (tempUrl.endsWith('/')) {
        tempUrl = tempUrl.substring(0, tempUrl.length - 1);
      }
      if (tempUrl.endsWith('/v1') || tempUrl.contains('/v1/') || tempUrl.contains('xiaomimimo.com')) {
        candidateUrls.add('$tempUrl/chat/completions');
      } else {
        candidateUrls.add('$tempUrl/v1/chat/completions');
        candidateUrls.add('$tempUrl/chat/completions');
      }
    }

    String finalSystemPrompt = systemPrompt.trim();
    if (finalSystemPrompt.isEmpty) {
      String targetLangPrompt = _getTargetLanguagePrompt(targetLang);
      String stylePrompt = '';
      if (targetLang == 'English' && translationStyle != 'Standard') {
        stylePrompt = _getTranslationStylePrompt(translationStyle);
      }

      finalSystemPrompt = '''You are a professional and accurate translator. Translate the text provided by the user into the target language.

Target Language settings:
$targetLangPrompt
${stylePrompt.isNotEmpty ? '\n$stylePrompt' : ''}

CRITICAL RULES:
1. Output ONLY the raw translated text content. Do NOT wrap it in Markdown code blocks (do not use ```), and do NOT add any introductions, explanations, prefixes, or notes.
2. Keep the exact same formatting, paragraphs, spaces, and punctuation of the original text.
3. If the input text is already in the target language (or the main language matching it), translate it back to the other major language (e.g. if target language is Chinese, and the input is Chinese, translate it to English; if target language is English, and input is English, translate it to Chinese).''';
    }

    var requestBody = {
      'model': modelName,
      'messages': [
        {'role': 'system', 'content': finalSystemPrompt},
        {'role': 'user', 'content': text}
      ],
      'temperature': 0.3
    };

    String? lastErrorMsg;
    final client = http.Client();
    for (int i = 0; i < candidateUrls.length; i++) {
      String currentUrl = candidateUrls[i];
      try {
        final response = await client.post(
          Uri.parse(currentUrl),
          headers: {
            'Authorization': 'Bearer $cleanKey',
            'Content-Type': 'application/json; charset=utf-8',
          },
          body: jsonEncode(requestBody),
        ).timeout(const Duration(seconds: 15));

        if (response.statusCode == 404 && i < candidateUrls.length - 1) {
          continue; // 自动重试
        }

        if (response.statusCode != 200) {
          throw Exception('HTTP ${response.statusCode}: ${response.body}');
        }

        final decoded = jsonDecode(utf8.decode(response.bodyBytes));
        final choices = decoded['choices'] as List;
        if (choices.isEmpty) {
          throw Exception('API returned empty choices');
        }

        String translated = choices[0]['message']['content'] ?? '';
        translated = translated.trim();
        if (translated.startsWith('```')) {
          int firstNewLine = translated.indexOf('\n');
          if (firstNewLine != -1) {
            translated = translated.substring(firstNewLine + 1);
          }
          if (translated.endsWith('```')) {
            translated = translated.substring(0, translated.length - 3);
          }
          translated = translated.trim();
        }
        client.close();
        return translated;
      } catch (ex) {
        lastErrorMsg = ex.toString();
        if (i < candidateUrls.length - 1) {
          continue;
        }
      }
    }
    client.close();
    throw Exception(lastErrorMsg ?? 'Unknown network error');
  }

  // 自动获取模型列表 (自带 ASCII 安全清理与 404 自适应重试)
  static Future<List<String>> fetchAvailableModels(String apiUrl, String apiKey) async {
    apiUrl = apiUrl.trim();
    final cleanKey = _cleanApiKey(apiKey);
    if (cleanKey.isEmpty) throw Exception('API Key is empty');

    List<String> candidateUrls = [];
    if (apiUrl.endsWith('/models')) {
      candidateUrls.add(apiUrl);
    } else {
      String tempUrl = apiUrl;
      if (tempUrl.endsWith('/')) {
        tempUrl = tempUrl.substring(0, tempUrl.length - 1);
      }
      if (tempUrl.endsWith('/v1') || tempUrl.contains('/v1/') || tempUrl.contains('xiaomimimo.com')) {
        candidateUrls.add('$tempUrl/models');
      } else {
        candidateUrls.add('$tempUrl/v1/models');
        candidateUrls.add('$tempUrl/models');
      }
    }

    String? lastErrorMsg;
    final client = http.Client();
    for (int i = 0; i < candidateUrls.length; i++) {
      String currentUrl = candidateUrls[i];
      try {
        final response = await client.get(
          Uri.parse(currentUrl),
          headers: {
            'Authorization': 'Bearer $cleanKey',
            'Accept': 'application/json',
          },
        ).timeout(const Duration(seconds: 10));

        if (response.statusCode == 404 && i < candidateUrls.length - 1) {
          continue; // 自动重试下一个
        }

        if (response.statusCode != 200) {
          throw Exception('HTTP ${response.statusCode}: ${response.body}');
        }

        final decoded = jsonDecode(utf8.decode(response.bodyBytes));
        final dataList = decoded['data'] as List?;
        if (dataList == null) throw Exception('Models format invalid: missing "data" array');

        List<String> models = [];
        for (var item in dataList) {
          if (item is Map && item.containsKey('id')) {
            models.add(item['id'].toString());
          }
        }
        models = models.toSet().toList();
        models.sort();
        client.close();
        return models;
      } catch (e) {
        lastErrorMsg = e.toString();
        if (i < candidateUrls.length - 1) {
          continue;
        }
      }
    }
    client.close();
    throw Exception(lastErrorMsg ?? 'Unknown network error');
  }

  static String _getTargetLanguagePrompt(String targetLang) {
    switch (targetLang) {
      case 'Auto':
        return 'Automatic (Bilingual translation: translate Chinese to English, and translate non-Chinese languages like English/Japanese/Korean to Chinese).';
      case 'Chinese':
        return 'Chinese (简体中文).';
      case 'English':
        return 'English.';
      case 'Japanese':
        return 'Japanese (日本語).';
      case 'Korean':
        return 'Korean (한국어).';
      case 'French':
        return 'French (Français).';
      case 'German':
        return 'German (Deutsch).';
      case 'Spanish':
        return 'Spanish (Español).';
      default:
        return 'Chinese.';
    }
  }

  static String _getTranslationStylePrompt(String translationStyle) {
    switch (translationStyle) {
      case 'AmericanColloquial':
        return "Translation style: Casual American English. Use natural local slang, typical idioms, and contractions (like 'gonna', 'wanna', 'I\'d', 'you\'re') suitable for informal daily messaging.";
      case 'BritishColloquial':
        return "Translation style: Conversational British English. Use natural British expressions, phrasing, and idioms suitable for daily UK messaging.";
      case 'Business':
        return "Translation style: Professional Business English. Use polite, professional, and formal vocabulary suitable for workplace communications and emails.";
      case 'Academic':
        return "Translation style: Academic English. Use high-level vocabulary, varied sentence structures, and a formal tone suitable for IELTS or writing essays.";
      case 'Concise':
        return "Translation style: Concise and fluent English. Keep it as short and clear as possible. Eliminate redundancy, use direct and natural phrasing.";
      default:
        return "";
    }
  }
}
