package com.axuetranslate.axuetranslate_mobile

import android.app.Activity
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.text.TextUtils
import androidx.annotation.NonNull
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {

    private val CHANNEL = "com.axue.translate/commands"

    override fun configureFlutterEngine(@NonNull flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, CHANNEL).setMethodCallHandler { call, result ->
            when (call.method) {
                "checkPermissions" -> {
                    val floatGranted = Settings.canDrawOverlays(this)
                    val accessibilityActive = isAccessibilityServiceEnabled(this, TranslationAccessibilityService::class.java)
                    result.success(mapOf(
                        "floatGranted" to floatGranted,
                        "accessibilityActive" to accessibilityActive
                    ))
                }
                "checkBatteryOptimization" -> {
                    val pm = getSystemService(Context.POWER_SERVICE) as android.os.PowerManager
                    val isIgnoring = pm.isIgnoringBatteryOptimizations(packageName)
                    result.success(isIgnoring)
                }
                "requestIgnoreBatteryOptimization" -> {
                    try {
                        val intent = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS).apply {
                            data = Uri.parse("package:$packageName")
                        }
                        startActivity(intent)
                        result.success(true)
                    } catch (e: Exception) {
                        try {
                            val intent = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
                            startActivity(intent)
                            result.success(true)
                        } catch (ex: Exception) {
                            result.error("ERROR", ex.message, null)
                        }
                    }
                }
                "openAutoStartSettings" -> {
                    var success = false
                    val intent = Intent()
                    val componentNames = arrayOf(
                        ComponentName("com.miui.securitycenter", "com.miui.permcenter.autostart.AutoStartManagementActivity"),
                        ComponentName("com.huawei.systemmanager", "com.huawei.systemmanager.optimize.process.ProtectActivity"),
                        ComponentName("com.huawei.systemmanager", "com.huawei.systemmanager.startupmgr.ui.StartupNormalAppListActivity"),
                        ComponentName("com.coloros.safecenter", "com.coloros.safecenter.startupapp.StartupAppListActivity"),
                        ComponentName("com.coloros.safecenter", "com.coloros.safecenter.permission.startup.StartupAppListActivity"),
                        ComponentName("com.oppo.safe", "com.oppo.safe.permission.startup.StartupAppListActivity"),
                        ComponentName("com.iqoo.secure", "com.iqoo.secure.ui.phoneoptimize.AddWhiteListActivity"),
                        ComponentName("com.iqoo.secure", "com.iqoo.secure.ui.phoneoptimize.BgStartUpManager"),
                        ComponentName("com.vivo.permissionmanager", "com.vivo.permissionmanager.activity.BgStartUpManagerActivity"),
                        ComponentName("com.samsung.android.sm_cn", "com.samsung.android.sm.ui.ram.AutoRunActivity"),
                        ComponentName("com.samsung.android.sm", "com.samsung.android.sm.ui.activeapplication.ActiveApplicationActivity")
                    )
                    for (componentName in componentNames) {
                        try {
                            intent.component = componentName
                            startActivity(intent)
                            success = true
                            break
                        } catch (e: Exception) {
                            // Continue
                        }
                    }
                    if (!success) {
                        try {
                            val detailsIntent = Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
                                data = Uri.parse("package:$packageName")
                            }
                            startActivity(detailsIntent)
                            success = true
                        } catch (e: Exception) {
                            result.error("ERROR", e.message, null)
                            return@setMethodCallHandler
                        }
                    }
                    result.success(success)
                }
                "requestPermission" -> {
                    val type = call.argument<String>("type")
                    if (type == "float") {
                        val intent = Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName"))
                        startActivity(intent)
                        result.success(true)
                    } else if (type == "accessibility") {
                        val intent = Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)
                        startActivity(intent)
                        result.success(true)
                    } else {
                        result.error("INVALID_TYPE", "Permission type must be 'float' or 'accessibility'", null)
                    }
                }
                "toggleFloatBall" -> {
                    val enable = call.argument<Boolean>("enable") ?: false
                    val intent = Intent(this, FloatWindowService::class.java)
                    if (enable) {
                        startService(intent)
                    } else {
                        stopService(intent)
                    }
                    result.success(true)
                }
                "updateSettings" -> {
                    // 双重保险：直接通过 MethodChannel 在原生侧写入一份 SharedPreferences
                    val prefs = getSharedPreferences("FlutterSharedPreferences", Context.MODE_PRIVATE)
                    val editor = prefs.edit()
                    
                    call.argument<String>("apiUrl")?.let { editor.putString("flutter.api_url", it) }
                    call.argument<String>("apiKey")?.let { editor.putString("flutter.api_key", it) }
                    call.argument<String>("modelName")?.let { editor.putString("flutter.model_name", it) }
                    call.argument<String>("systemPrompt")?.let { editor.putString("flutter.system_prompt", it) }
                    call.argument<String>("targetLanguage")?.let { editor.putString("flutter.target_language", it) }
                    call.argument<String>("translationStyle")?.let { editor.putString("flutter.translation_style", it) }
                    call.argument<String>("appLanguage")?.let { editor.putString("flutter.app_language", it) }
                    call.argument<Boolean>("enableFloat")?.let { editor.putBoolean("flutter.enable_float", it) }
                    
                    editor.apply()
                    result.success(true)
                }
                else -> {
                    result.notImplemented()
                }
            }
        }
    }

    private fun isAccessibilityServiceEnabled(context: Context, serviceClass: Class<*>): Boolean {
        val expectedComponentName = ComponentName(context, serviceClass)
        val enabledServicesSetting = Settings.Secure.getString(
            context.contentResolver,
            Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES
        ) ?: return false
        
        val colonSplitter = TextUtils.SimpleStringSplitter(':')
        colonSplitter.setString(enabledServicesSetting)
        while (colonSplitter.hasNext()) {
            val componentNameString = colonSplitter.next()
            val enabledService = ComponentName.unflattenFromString(componentNameString)
            if (enabledService != null && enabledService == expectedComponentName) {
                return true
            }
        }
        return false
    }
}
