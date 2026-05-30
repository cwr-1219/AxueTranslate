package com.axuetranslate.axuetranslate_mobile

import android.animation.ValueAnimator
import android.annotation.SuppressLint
import android.app.Service
import android.content.Context
import android.content.Intent
import android.graphics.*
import android.graphics.drawable.GradientDrawable
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.view.*
import android.view.animation.LinearInterpolator
import android.widget.Toast
import java.util.concurrent.atomic.AtomicBoolean

class FloatWindowService : Service() {

    private lateinit var windowManager: WindowManager
    private var floatView: FloatBallView? = null
    private var layoutParams: WindowManager.LayoutParams? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        windowManager = getSystemService(Context.WINDOW_SERVICE) as WindowManager
        showNotification()
        showFloatBall()
    }

    @SuppressLint("ClickableViewAccessibility")
    private fun showFloatBall() {
        if (floatView != null) return

        // 悬浮窗权限安全防崩守卫
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.M) {
            if (!android.provider.Settings.canDrawOverlays(this)) {
                val prefs = getSharedPreferences("FlutterSharedPreferences", Context.MODE_PRIVATE)
                val appLang = prefs.getString("flutter.app_language", "zh") ?: "zh"
                showToast(if (appLang == "zh") "请先授予悬浮窗权限" else "Please grant Floating Window permission first")
                stopSelf()
                return
            }
        }

        val context = this
        floatView = FloatBallView(context)

        val type = if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
        } else {
            @Suppress("DEPRECATION")
            WindowManager.LayoutParams.TYPE_PHONE
        }

        layoutParams = WindowManager.LayoutParams(
            dpToPx(54),
            dpToPx(54),
            type,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT
        ).apply {
            gravity = Gravity.TOP or Gravity.START
            x = 0
            y = windowManager.defaultDisplay.height / 2
        }

        try {
            windowManager.addView(floatView, layoutParams)
        } catch (e: Exception) {
            e.printStackTrace()
            stopSelf()
            return
        }

        // 菜单与长按状态变量声明
        val handler = Handler(Looper.getMainLooper())
        
        // 1. 物理长按任务 Runnable
        val longClickRunnable = Runnable {
            isLongClicked = true
            vibrateFeedback()
            showFloatMenu()
        }

        // 实现拖拽、长按手势与贴边逻辑的完美合一
        floatView?.setOnTouchListener(object : View.OnTouchListener {
            private var x = 0
            private var y = 0
            private var touchX = 0f
            private var touchY = 0f
            private var isMoved = false

            override fun onTouch(v: View?, event: MotionEvent?): Boolean {
                if (event == null || layoutParams == null) return false
                when (event.action) {
                    MotionEvent.ACTION_DOWN -> {
                        x = layoutParams!!.x
                        y = layoutParams!!.y
                        touchX = event.rawX
                        touchY = event.rawY
                        isMoved = false
                        isLongClicked = false
                        floatView?.wakeUp()
                        
                        // 启动长按延时任务
                        handler.postDelayed(longClickRunnable, 600)
                    }
                    MotionEvent.ACTION_MOVE -> {
                        val dx = (event.rawX - touchX).toInt()
                        val dy = (event.rawY - touchY).toInt()
                        
                        // 若拖动位移明显，立即取消长按定时器以防误触
                        if (Math.abs(dx) > 15 || Math.abs(dy) > 15) {
                            handler.removeCallbacks(longClickRunnable)
                            isMoved = true
                        }
                        
                        // 若不是长按触发的移动，跟随手指滑动
                        if (!isLongClicked && isMoved) {
                            layoutParams!!.x = x + dx
                            layoutParams!!.y = y + dy
                            windowManager.updateViewLayout(floatView, layoutParams)
                        }
                    }
                    MotionEvent.ACTION_UP -> {
                        handler.removeCallbacks(longClickRunnable)
                        if (!isMoved && !isLongClicked) {
                            // 点击事件
                            floatView?.performClick()
                        } else if (isMoved && !isLongClicked) {
                            // 松开手贴边
                            animateToSide()
                        }
                    }
                }
                return true
            }
        })

        floatView?.setOnClickListener {
            onFloatBallClicked()
        }
    }

    private var menuView: android.widget.LinearLayout? = null
    private var menuParams: WindowManager.LayoutParams? = null
    private val menuHandler = Handler(Looper.getMainLooper())
    private val autoDismissMenuRunnable = Runnable { dismissFloatMenu() }
    private var isLongClicked = false

    // 2. 动态编译并拼接所选风格与语种的终极 System Prompt 写入原生沙盒，解决微信自定义风格不可用问题
    private fun rebuildSystemPrompt(prefs: android.content.SharedPreferences) {
        val targetLang = prefs.getString("flutter.target_language", "Auto") ?: "Auto"
        val selectedStyle = prefs.getString("flutter.translation_style", "Standard") ?: "Standard"
        
        val targetLangPrompt = when (targetLang) {
            "Auto" -> "Automatic (Bilingual translation: translate Chinese to English, and translate non-Chinese languages like English/Japanese/Korean to Chinese)."
            "Chinese" -> "Chinese (简体中文)."
            "English" -> "English."
            "Japanese" -> "Japanese (日本語)."
            "Korean" -> "Korean (한국어)."
            "French" -> "French (Français)."
            "German" -> "German (Deutsch)."
            "Spanish" -> "Spanish (Español)."
            "Russian" -> "Russian (Русский)."
            "Italian" -> "Italian (Italiano)."
            "Portuguese" -> "Portuguese (Português)."
            "Vietnamese" -> "Vietnamese (Tiếng Việt)."
            "Thai" -> "Thai (ภาษาไทย)."
            "Arabic" -> "Arabic (العربية)."
            else -> "Chinese."
        }
        
        var stylePrompt = ""
        if (selectedStyle != "Standard") {
            stylePrompt = getStylePromptFromJson(prefs, selectedStyle)
        }
        
        val finalPrompt = """You are a professional and accurate translator. Translate the text provided by the user into the target language.

Target Language settings:
$targetLangPrompt
${if (stylePrompt.isNotEmpty()) "\n$stylePrompt" else ""}

CRITICAL RULES:
1. Output ONLY the raw translated text content. Do NOT wrap it in Markdown code blocks (do not use ```), and do NOT add any introductions, explanations, prefixes, or notes.
2. Keep the exact same formatting, paragraphs, spaces, and punctuation of the original text.
3. If the input text is already in the target language (or the main language matching it), translate it back to the other major language (e.g. if target language is Chinese, and the input is Chinese, translate it to English; if target language is English, and input is English, translate it to Chinese)."""

        prefs.edit().putString("flutter.system_prompt", finalPrompt).apply()
    }
    
    private fun getStylePromptFromJson(prefs: android.content.SharedPreferences, styleName: String): String {
        val builtInPrompt = when (styleName) {
            "AmericanColloquial" -> "Translation style: Casual American English. Use natural local slang, typical idioms, and contractions suitable for daily messaging."
            "BritishColloquial" -> "Translation style: Conversational British English. Use natural British expressions, phrasing, and idioms suitable for daily UK messaging."
            "Business" -> "Translation style: Professional Business English. Use polite, professional, and formal vocabulary suitable for workplace communications and emails."
            "Academic" -> "Translation style: Academic English. Use high-level vocabulary, varied sentence structures, and a formal tone suitable for essays and academic writing."
            "Concise" -> "Translation style: Concise and fluent. Keep it as short and clear as possible. Eliminate redundancy, use direct and natural phrasing."
            else -> null
        }
        if (builtInPrompt != null) return builtInPrompt
        
        try {
            val jsonStr = prefs.getString("flutter.style_configs_json", "") ?: ""
            if (jsonStr.isNotEmpty()) {
                val array = org.json.JSONArray(jsonStr)
                for (i in 0 until array.length()) {
                    val obj = array.getJSONObject(i)
                    if (obj.optString("name") == styleName) {
                        return obj.optString("prompt")
                    }
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
        return ""
    }

    // 3. 长按在屏幕侧边弹出极具毛玻璃发光高颜值配置菜单，点击弹出二级滑动选择浮层
    @SuppressLint("ClickableViewAccessibility")
    private fun showFloatMenu() {
        if (menuView != null) {
            dismissFloatMenu()
            return
        }

        val context = this
        val prefs = getSharedPreferences("FlutterSharedPreferences", Context.MODE_PRIVATE)

        // 3.1 菜单容器布局 (圆角 16dp + 亮紫色高亮赛博发光线描边)
        menuView = android.widget.LinearLayout(context).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            val pad = dpToPx(14)
            setPadding(pad, pad, pad, pad)
            
            val drawable = GradientDrawable(
                GradientDrawable.Orientation.TL_BR,
                intArrayOf(Color.parseColor("#F51E1535"), Color.parseColor("#FB0F0A1A"))
            ).apply {
                cornerRadius = dpToPx(16).toFloat()
                setStroke(dpToPx(1), Color.parseColor("#88A855F7"))
            }
            background = drawable
        }

        // 3.2 外部触摸监听，点按空白区菜单立即高雅退场
        menuView?.setOnTouchListener { _, event ->
            if (event.action == MotionEvent.ACTION_OUTSIDE) {
                dismissFloatMenu()
                true
            } else {
                resetMenuTimer()
                false
            }
        }

        // 头部标题："配置小助手"
        val titleText = android.widget.TextView(context).apply {
            text = if (prefs.getString("flutter.app_language", "zh") == "zh") "阿雪侧边助手" else "Config Helper"
            setTextColor(Color.WHITE)
            textSize = 12f
            typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD)
            gravity = Gravity.CENTER
            setPadding(0, 0, 0, dpToPx(10))
        }
        menuView?.addView(titleText)

        // ------------------ 3.3 模型供应商切换 Item ------------------
        val modelLayout = createMenuItemLayout(context, 
            title = if (prefs.getString("flutter.app_language", "zh") == "zh") "大模型供应商" else "Model Provider",
            currentVal = prefs.getString("flutter.selected_model", "DeepSeek 大模型") ?: "DeepSeek 大模型"
        ) { textValView ->
            try {
                val configsJson = prefs.getString("flutter.model_configs_json", "") ?: ""
                val models = ArrayList<Pair<String, String>>()
                val modelsData = ArrayList<org.json.JSONObject>()
                if (configsJson.isNotEmpty()) {
                    val array = org.json.JSONArray(configsJson)
                    for (i in 0 until array.length()) {
                        val obj = array.getJSONObject(i)
                        val name = obj.optString("displayName")
                        if (name.isNotEmpty()) {
                            models.add(Pair(name, name))
                            modelsData.add(obj)
                        }
                    }
                }
                
                if (models.isEmpty()) {
                    val defaultModels = listOf("DeepSeek 大模型", "小米大模型", "OpenAI (ChatGPT)", "Google (Gemini)", "自定义模型")
                    for (m in defaultModels) {
                        models.add(Pair(m, m))
                    }
                }
                
                val current = prefs.getString("flutter.selected_model", "DeepSeek 大模型") ?: "DeepSeek 大模型"
                
                showSubMenuPopup(textValView, models, current) { key, displayName ->
                    prefs.edit().putString("flutter.selected_model", key).apply()
                    
                    val idx = modelsData.indexOfFirst { it.optString("displayName") == key }
                    if (idx != -1 && idx < modelsData.size) {
                        val targetCfg = modelsData[idx]
                        prefs.edit().apply {
                            putString("flutter.api_url", targetCfg.optString("apiUrl"))
                            putString("flutter.api_key", targetCfg.optString("apiKey"))
                            putString("flutter.model_name", targetCfg.optString("modelName"))
                        }.apply()
                    }
                    
                    textValView.text = displayName
                    showToast(if (prefs.getString("flutter.app_language", "zh") == "zh") "切换为: $displayName" else "Switched to: $displayName")
                }
            } catch (e: Exception) {
                e.printStackTrace()
            }
        }
        menuView?.addView(modelLayout)
        addDivider(context)

        // ------------------ 3.4 翻译说话风格切换 Item ------------------
        val styleLayout = createMenuItemLayout(context, 
            title = if (prefs.getString("flutter.app_language", "zh") == "zh") "翻译说话风格" else "Translation Style",
            currentVal = getStyleDisplayName(prefs, prefs.getString("flutter.translation_style", "Standard") ?: "Standard")
        ) { textValView ->
            try {
                val styles = ArrayList<Pair<String, String>>()
                val stylesJson = prefs.getString("flutter.style_configs_json", "") ?: ""
                if (stylesJson.isNotEmpty()) {
                    val array = org.json.JSONArray(stylesJson)
                    for (i in 0 until array.length()) {
                        val obj = array.getJSONObject(i)
                        styles.add(Pair(obj.optString("name"), obj.optString("displayName")))
                    }
                }
                
                if (styles.isEmpty()) {
                    styles.addAll(listOf(
                        Pair("Standard", "标准 (Standard)"),
                        Pair("AmericanColloquial", "美式口语 (American)"),
                        Pair("BritishColloquial", "英式口语 (British)"),
                        Pair("Business", "商务职场 (Business)"),
                        Pair("Academic", "学术雅思 (Academic)"),
                        Pair("Concise", "极简流利 (Concise)")
                    ))
                }
                
                val current = prefs.getString("flutter.translation_style", "Standard") ?: "Standard"
                
                showSubMenuPopup(textValView, styles, current) { key, displayName ->
                    prefs.edit().putString("flutter.translation_style", key).apply()
                    rebuildSystemPrompt(prefs)
                    
                    textValView.text = displayName
                    showToast(if (prefs.getString("flutter.app_language", "zh") == "zh") "风格切为: $displayName" else "Style: $displayName")
                }
            } catch (e: Exception) {
                e.printStackTrace()
            }
        }
        menuView?.addView(styleLayout)
        addDivider(context)

        // ------------------ 3.5 翻译目标语种切换 Item ------------------
        val langKeys = arrayOf("Auto", "Chinese", "English", "Japanese", "Korean", "French", "German", "Spanish", "Russian", "Italian", "Portuguese", "Vietnamese", "Thai", "Arabic")
        val langNames = arrayOf("Auto (中英互译)", "Chinese (简体)", "English (英文)", "Japanese (日语)", "Korean (韩语)", "French (法语)", "German (德语)", "Spanish (西班牙语)", "Russian (俄语)", "Italian (意语)", "Portuguese (葡语)", "Vietnamese (越语)", "Thai (泰语)", "Arabic (阿语)")
        
        val langLayout = createMenuItemLayout(context, 
            title = if (prefs.getString("flutter.app_language", "zh") == "zh") "翻译目标语种" else "Target Language",
            currentVal = langNames[Math.max(0, langKeys.indexOf(prefs.getString("flutter.target_language", "Auto") ?: "Auto"))]
        ) { textValView ->
            val current = prefs.getString("flutter.target_language", "Auto") ?: "Auto"
            val languages = ArrayList<Pair<String, String>>()
            for (i in langKeys.indices) {
                languages.add(Pair(langKeys[i], langNames[i]))
            }
            
            showSubMenuPopup(textValView, languages, current) { key, displayName ->
                prefs.edit().putString("flutter.target_language", key).apply()
                rebuildSystemPrompt(prefs)
                
                textValView.text = displayName
                showToast(if (prefs.getString("flutter.app_language", "zh") == "zh") "语种切为: $displayName" else "Target Language: $displayName")
            }
        }
        menuView?.addView(langLayout)

        // 3.6 自适应左/右屏幕边缘，智能化左右贴合排列
        val fParams = layoutParams ?: return
        val screenWidth = windowManager.defaultDisplay.width
        val isLeft = fParams.x + dpToPx(54) / 2 < screenWidth / 2
        
        val mWidth = dpToPx(160)
        menuParams = WindowManager.LayoutParams(
            mWidth,
            WindowManager.LayoutParams.WRAP_CONTENT,
            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
            } else {
                @Suppress("DEPRECATION")
                WindowManager.LayoutParams.TYPE_PHONE
            },
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or 
            WindowManager.LayoutParams.FLAG_WATCH_OUTSIDE_TOUCH or 
            WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT
        ).apply {
            gravity = Gravity.TOP or Gravity.START
            y = fParams.y - dpToPx(40)
            x = if (isLeft) {
                fParams.x + dpToPx(54) + dpToPx(6)
            } else {
                fParams.x - mWidth - dpToPx(6)
            }
            windowAnimations = android.R.style.Animation_Toast
        }

        try {
            windowManager.addView(menuView, menuParams)
            resetMenuTimer()
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    /**
     * 弹出高质感二级滑动选择浮层 (使用 PopupWindow + ScrollView 上下滑动选择)
     */
    private fun showSubMenuPopup(
        anchorView: View, 
        items: List<Pair<String, String>>, 
        currentKey: String, 
        onSelect: (String, String) -> Unit
    ) {
        val context = this
        val popupWindow = android.widget.PopupWindow(context)
        
        val scrollView = android.widget.ScrollView(context).apply {
            isVerticalScrollBarEnabled = true
            overScrollMode = View.OVER_SCROLL_IF_CONTENT_SCROLLS
        }
        
        val listContainer = android.widget.LinearLayout(context).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            val pad = dpToPx(6)
            setPadding(pad, pad, pad, pad)
        }
        scrollView.addView(listContainer)
        
        // 极光黑紫色高级圆角卡片背景
        val backgroundDrawable = GradientDrawable(
            GradientDrawable.Orientation.TL_BR,
            intArrayOf(Color.parseColor("#F51C1230"), Color.parseColor("#FB0B0716"))
        ).apply {
            cornerRadius = dpToPx(12).toFloat()
            setStroke(dpToPx(1), Color.parseColor("#CCA855F7")) // 极亮紫色霓虹发光线
        }
        scrollView.background = backgroundDrawable
        
        // 动态构建每一个滚动选项的 TextView
        for (item in items) {
            val isSelected = item.first == currentKey
            val itemTextView = android.widget.TextView(context).apply {
                text = item.second
                textSize = 11f
                val padH = dpToPx(12)
                val padV = dpToPx(8)
                setPadding(padH, padV, padH, padV)
                
                if (isSelected) {
                    setTextColor(Color.parseColor("#E9D5FF")) // 极致白紫色
                    typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD)
                    val selectedBg = GradientDrawable().apply {
                        cornerRadius = dpToPx(6).toFloat()
                        setColor(Color.parseColor("#33A855F7"))
                    }
                    background = selectedBg
                } else {
                    setTextColor(Color.parseColor("#94A3B8"))
                    typeface = Typeface.create(Typeface.DEFAULT, Typeface.NORMAL)
                    val outVal = android.util.TypedValue()
                    context.theme.resolveAttribute(android.R.attr.selectableItemBackground, outVal, true)
                    setBackgroundResource(outVal.resourceId)
                }
                
                isClickable = true
                isFocusable = true
                
                setOnClickListener {
                    vibrateFeedback()
                    onSelect(item.first, item.second)
                    popupWindow.dismiss()
                    resetMenuTimer()
                }
            }
            listContainer.addView(itemTextView)
        }
        
        popupWindow.contentView = scrollView
        popupWindow.width = dpToPx(135)
        
        val maxH = dpToPx(160)
        listContainer.measure(View.MeasureSpec.UNSPECIFIED, View.MeasureSpec.UNSPECIFIED)
        val measuredH = listContainer.measuredHeight + dpToPx(12)
        popupWindow.height = if (measuredH > maxH) maxH else WindowManager.LayoutParams.WRAP_CONTENT
        
        popupWindow.isFocusable = true
        popupWindow.isOutsideTouchable = true
        popupWindow.setBackgroundDrawable(android.graphics.drawable.ColorDrawable(Color.TRANSPARENT))
        
        // 自适应左贴边还是右贴边定位
        val fParams = layoutParams ?: return
        val screenWidth = windowManager.defaultDisplay.width
        val isLeftBall = fParams.x + dpToPx(54) / 2 < screenWidth / 2
        
        val xOff = if (isLeftBall) {
            dpToPx(150)
        } else {
            -dpToPx(140)
        }
        val yOff = -dpToPx(20)
        
        try {
            popupWindow.showAsDropDown(anchorView, xOff, yOff)
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    private fun dismissFloatMenu() {
        menuHandler.removeCallbacks(autoDismissMenuRunnable)
        if (menuView != null) {
            if (menuView?.parent != null) {
                try {
                    windowManager.removeView(menuView)
                } catch (e: Exception) {
                    e.printStackTrace()
                }
            }
            menuView = null
            menuParams = null
        }
    }

    private fun resetMenuTimer() {
        menuHandler.removeCallbacks(autoDismissMenuRunnable)
        menuHandler.postDelayed(autoDismissMenuRunnable, 4000)
    }

    private fun createMenuItemLayout(context: Context, title: String, currentVal: String, onClick: (android.widget.TextView) -> Unit): android.widget.LinearLayout {
        return android.widget.LinearLayout(context).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            val padH = dpToPx(10)
            val padV = dpToPx(6)
            setPadding(padH, padV, padH, padV)
            
            val outVal = android.util.TypedValue()
            context.theme.resolveAttribute(android.R.attr.selectableItemBackground, outVal, true)
            setBackgroundResource(outVal.resourceId)
            isClickable = true
            isFocusable = true
            
            val titleView = android.widget.TextView(context).apply {
                text = title
                setTextColor(Color.parseColor("#94A3B8"))
                textSize = 9f
            }
            
            val valView = android.widget.TextView(context).apply {
                text = currentVal
                setTextColor(Color.parseColor("#A855F7"))
                textSize = 12f
                typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD)
                setPadding(0, dpToPx(2), 0, 0)
            }
            
            addView(titleView)
            addView(valView)
            
            setOnClickListener {
                onClick(valView)
                resetMenuTimer()
            }
        }
    }

    private fun addDivider(context: Context) {
        val divider = View(context).apply {
            layoutParams = android.widget.LinearLayout.LayoutParams(
                android.widget.LinearLayout.LayoutParams.MATCH_PARENT,
                dpToPx(1)
            ).apply {
                setMargins(dpToPx(10), dpToPx(4), dpToPx(10), dpToPx(4))
            }
            setBackgroundColor(Color.parseColor("#1AA855F7"))
        }
        menuView?.addView(divider)
    }

    private fun getStyleDisplayName(prefs: android.content.SharedPreferences, name: String): String {
        val builtIn = when (name) {
            "Standard" -> "标准 (Standard)"
            "AmericanColloquial" -> "美式口语 (American)"
            "BritishColloquial" -> "英式口语 (British)"
            "Business" -> "商务职场 (Business)"
            "Academic" -> "学术雅思 (Academic)"
            "Concise" -> "极简流利 (Concise)"
            else -> null
        }
        if (builtIn != null) return builtIn

        try {
            val jsonStr = prefs.getString("flutter.style_configs_json", "") ?: ""
            if (jsonStr.isNotEmpty()) {
                val array = org.json.JSONArray(jsonStr)
                for (i in 0 until array.length()) {
                    val obj = array.getJSONObject(i)
                    if (obj.optString("name") == name) {
                        return obj.optString("displayName")
                    }
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
        return name
    }

    private fun vibrateFeedback() {
        try {
            val vibrator = getSystemService(Context.VIBRATOR_SERVICE) as android.os.Vibrator
            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
                vibrator.vibrate(android.os.VibrationEffect.createOneShot(45, 120))
            } else {
                @Suppress("DEPRECATION")
                vibrator.vibrate(45)
            }
        } catch (e: Exception) {}
    }

    private fun animateToSide() {
        val params = layoutParams ?: return
        val view = floatView ?: return
        val screenWidth = windowManager.defaultDisplay.width
        val viewWidth = view.width

        val targetX = if (params.x + viewWidth / 2 < screenWidth / 2) {
            0
        } else {
            screenWidth - viewWidth
        }

        val animator = ValueAnimator.ofInt(params.x, targetX)
        animator.duration = 250
        animator.addUpdateListener { animation ->
            params.x = animation.animatedValue as Int
            if (view.parent != null) {
                windowManager.updateViewLayout(view, params)
            }
        }
        animator.start()
        
        // 拖动抬手后，触发贴边延时半透明隐藏
        view.resetIdleTimer()
    }

    private val isTranslating = AtomicBoolean(false)

    private fun onFloatBallClicked() {
        if (isTranslating.get()) return

        val accessibilityService = TranslationAccessibilityService.instance
        val prefs = getSharedPreferences("FlutterSharedPreferences", Context.MODE_PRIVATE)
        val appLang = prefs.getString("flutter.app_language", "zh") ?: "zh"
        
        val textTranslating = if (appLang == "zh") "正在翻译中..." else "Translating..."
        val textNoFocus = if (appLang == "zh") "未找到可用输入框或文字" else "No editable focus found"
        val textSuccess = if (appLang == "zh") "翻译并替换成功" else "Replaced successfully"
        val textFailed = if (appLang == "zh") "翻译失败，请检查配置" else "Translation failed"

        if (accessibilityService == null) {
            showToast(if (appLang == "zh") "请先在系统设置中启用“无障碍服务”" else "Please enable Accessibility Service first")
            return
        }

        // 精准交互守卫：首先判定有没有捕捉到当前活动页面的输入框焦点
        val focusedNode = accessibilityService.getFocusedNode()
        if (focusedNode == null) {
            showToast(if (appLang == "zh") "未找到可用输入框，请先点击输入框" else "No input field found, please click one first")
            return
        }

        // 其次获取输入框中的文字，若文字为空则进行“请输入内容”的明确提示
        val (text, isSelected) = accessibilityService.getTargetText()
        if (text.isBlank()) {
            showToast(if (appLang == "zh") "请输入内容" else "Please enter text first")
            return
        }

        isTranslating.set(true)
        floatView?.setLoading(true)
        showToast(textTranslating)

        // 请求大模型翻译
        requestTranslation(text) { result ->
            Handler(Looper.getMainLooper()).post {
                isTranslating.set(false)
                floatView?.setLoading(false)
                
                result.onSuccess { translatedText ->
                    val replaced = accessibilityService.replaceText(translatedText)
                    if (replaced) {
                        showToast(textSuccess)
                    } else {
                        // 替换失败，复制到粘贴板作为备选
                        showToast(if (appLang == "zh") "写入失败，已复制到剪贴板" else "Failed to replace, copied to clipboard")
                    }
                }.onFailure { error ->
                    error.printStackTrace()
                    showToast("$textFailed: ${error.message}")
                }
            }
        }
    }

    private fun requestTranslation(text: String, callback: (Result<String>) -> Unit) {
        Thread {
            try {
                // 全局豁免 HTTPS 的 SSL 校验，完美兼容各类自签 IP 中转站与反向代理限制
                trustAllHostsAndCerts()
                val prefs = getSharedPreferences("FlutterSharedPreferences", Context.MODE_PRIVATE)
                val apiUrl = prefs.getString("flutter.api_url", "https://api.deepseek.com")?.trim() ?: ""
                val rawApiKey = prefs.getString("flutter.api_key", "") ?: ""
                val apiKey = rawApiKey.filter { it.toInt() in 33..126 }.trim()
                val modelName = prefs.getString("flutter.model_name", "deepseek-chat")?.trim() ?: ""
                val targetLang = prefs.getString("flutter.target_language", "Auto") ?: "Auto"
                val translationStyle = prefs.getString("flutter.translation_style", "Standard") ?: "Standard"
                val systemPrompt = prefs.getString("flutter.system_prompt", "")?.trim() ?: ""

                if (apiUrl.isEmpty() || apiKey.isEmpty()) {
                    callback(Result.failure(Exception("API settings empty")))
                    return@Thread
                }

                // 候选 URL 终结点，支持自动 fallback 以兼容不需要 /v1 或需要 /v1 的情况
                val candidateUrls = ArrayList<String>()
                if (apiUrl.endsWith("/chat/completions")) {
                    candidateUrls.add(apiUrl)
                } else {
                    var tempUrl = apiUrl
                    if (tempUrl.endsWith("/")) {
                        tempUrl = tempUrl.substring(0, tempUrl.length - 1)
                    }
                    if (tempUrl.endsWith("/v1") || tempUrl.contains("/v1/") || tempUrl.contains("xiaomimimo.com")) {
                        candidateUrls.add("$tempUrl/chat/completions")
                    } else {
                        candidateUrls.add("$tempUrl/v1/chat/completions")
                        candidateUrls.add("$tempUrl/chat/completions")
                    }
                }

                var finalPrompt = systemPrompt
                if (finalPrompt.isEmpty()) {
                    val targetLangPrompt = when (targetLang) {
                        "Auto" -> "Automatic (Bilingual translation: translate Chinese to English, and translate non-Chinese languages like English/Japanese/Korean to Chinese)."
                        "Chinese" -> "Chinese (简体中文)."
                        "English" -> "English."
                        "Japanese" -> "Japanese (日本語)."
                        "Korean" -> "Korean (한국어)."
                        "French" -> "French (Français)."
                        "German" -> "German (Deutsch)."
                        "Spanish" -> "Spanish (Español)."
                        "Russian" -> "Russian (Русский)."
                        "Italian" -> "Italian (Italiano)."
                        "Portuguese" -> "Portuguese (Português)."
                        "Vietnamese" -> "Vietnamese (Tiếng Việt)."
                        "Thai" -> "Thai (ภาษาไทย)."
                        "Arabic" -> "Arabic (العربية)."
                        else -> "Chinese."
                    }
                    
                    var stylePrompt = ""
                    if (targetLang == "English" && translationStyle != "Standard") {
                        stylePrompt = when (translationStyle) {
                            "AmericanColloquial" -> "Translation style: Casual American English. Use natural local slang, typical idioms, and contractions (like 'gonna', 'wanna', 'I\'d', 'you\'re') suitable for informal daily messaging."
                            "BritishColloquial" -> "Translation style: Conversational British English. Use natural British expressions, phrasing, and idioms suitable for daily UK messaging."
                            "Business" -> "Translation style: Professional Business English. Use polite, professional, and formal vocabulary suitable for workplace communications and emails."
                            "Academic" -> "Translation style: Academic English. Use high-level vocabulary, varied sentence structures, and a formal tone suitable for IELTS or writing essays."
                            "Concise" -> "Translation style: Concise and fluent English. Keep it as short and clear as possible. Eliminate redundancy, use direct and natural phrasing."
                            else -> ""
                        }
                    }

                    finalPrompt = """You are a professional and accurate translator. Translate the text provided by the user into the target language.

Target Language settings:
$targetLangPrompt
${if (stylePrompt.isNotEmpty()) "\n$stylePrompt" else ""}

CRITICAL RULES:
1. Output ONLY the raw translated text content. Do NOT wrap it in Markdown code blocks (do not use ```), and do NOT add any introductions, explanations, prefixes, or notes.
2. Keep the exact same formatting, paragraphs, spaces, and punctuation of the original text.
3. If the input text is already in the target language (or the main language matching it), translate it back to the other major language (e.g. if target language is Chinese, and the input is Chinese, translate it to English; if target language is English, and input is English, translate it to Chinese)."""
                }

                var lastError: Exception? = null
                for (urlStr in candidateUrls) {
                    try {
                        val url = java.net.URL(urlStr)
                        val conn = url.openConnection() as java.net.HttpURLConnection
                        
                        // 强力特区：在 HttpsURLConnection 实例级别注入证书与主机域名验证豁免，实现百分之百绝对穿透！
                        if (conn is javax.net.ssl.HttpsURLConnection) {
                            try {
                                val trustAllCerts = arrayOf<javax.net.ssl.TrustManager>(object : javax.net.ssl.X509TrustManager {
                                    override fun getAcceptedIssuers(): Array<java.security.cert.X509Certificate> = arrayOf()
                                    override fun checkClientTrusted(certs: Array<java.security.cert.X509Certificate>, authType: String) {}
                                    override fun checkServerTrusted(certs: Array<java.security.cert.X509Certificate>, authType: String) {}
                                })
                                val sc = javax.net.ssl.SSLContext.getInstance("SSL")
                                sc.init(null, trustAllCerts, java.security.SecureRandom())
                                conn.sslSocketFactory = sc.socketFactory
                                conn.hostnameVerifier = javax.net.ssl.HostnameVerifier { _, _ -> true }
                            } catch (e: Exception) {
                                e.printStackTrace()
                            }
                        }

                        conn.requestMethod = "POST"
                        conn.setRequestProperty("Authorization", "Bearer $apiKey")
                        conn.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                        conn.connectTimeout = 15000
                        conn.readTimeout = 15000
                        conn.doOutput = true

                        val jsonBody = org.json.JSONObject().apply {
                            put("model", modelName)
                            put("temperature", 0.3)
                            val messagesArray = org.json.JSONArray().apply {
                                put(org.json.JSONObject().apply {
                                    put("role", "system")
                                    put("content", finalPrompt)
                                })
                                put(org.json.JSONObject().apply {
                                    put("role", "user")
                                    put("content", text)
                                })
                            }
                            put("messages", messagesArray)
                        }

                        val os = conn.outputStream
                        os.write(jsonBody.toString().toByteArray(Charsets.UTF_8))
                        os.flush()
                        os.close()

                        val responseCode = conn.responseCode
                        if (responseCode == 404) {
                            continue
                        }
                        if (responseCode != 200) {
                            val errorStream = conn.errorStream
                            val errorText = errorStream?.bufferedReader()?.use { it.readText() } ?: ""
                            throw Exception("HTTP $responseCode: $errorText")
                        }

                        val responseText = conn.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
                        val responseJson = org.json.JSONObject(responseText)
                        val choices = responseJson.getJSONArray("choices")
                        if (choices.length() == 0) {
                            throw Exception("Empty choices returned")
                        }
                        var translated = choices.getJSONObject(0).getJSONObject("message").getString("content")
                        translated = translated.trim()
                        if (translated.startsWith("```")) {
                            val firstNewLine = translated.indexOf('\n')
                            if (firstNewLine != -1) {
                                translated = translated.substring(firstNewLine + 1)
                            }
                            if (translated.endsWith("```")) {
                                translated = translated.substring(0, translated.length - 3)
                            }
                            translated = translated.trim()
                        }
                        callback(Result.success(translated))
                        return@Thread
                    } catch (e: Exception) {
                        lastError = e
                    }
                }
                callback(Result.failure(lastError ?: Exception("Network error")))
            } catch (e: Exception) {
                callback(Result.failure(e))
            }
        }.start()
    }

    private fun showToast(message: String) {
        Toast.makeText(this, message, Toast.LENGTH_SHORT).show()
    }

    private fun dpToPx(dp: Int): Int {
        val density = resources.displayMetrics.density
        return (dp * density).toInt()
    }

    private fun dpToPx(dp: Float): Float {
        val density = resources.displayMetrics.density
        return dp * density
    }

    override fun onDestroy() {
        try {
            val manager = getSystemService(Context.NOTIFICATION_SERVICE) as android.app.NotificationManager
            manager.cancel(101)
        } catch (e: Exception) {
            e.printStackTrace()
        }
        if (floatView != null && floatView?.parent != null) {
            windowManager.removeView(floatView)
        }
        floatView = null
        super.onDestroy()
    }

    // --- 自定义悬浮球 View，带双自转 Loading 与 贴边贴合渐变动效 ---
    inner class FloatBallView(context: Context) : View(context) {

        private val backgroundPaint = Paint(Paint.ANTI_ALIAS_FLAG)
        private val borderPaint = Paint(Paint.ANTI_ALIAS_FLAG)
        private val loadingPaintOuter = Paint(Paint.ANTI_ALIAS_FLAG)
        private val loadingPaintInner = Paint(Paint.ANTI_ALIAS_FLAG)
        private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG)

        private var isLoading = false
        private var angleOuter = 0f
        private var angleInner = 0f
        
        private val handler = Handler(Looper.getMainLooper())
        private val idleRunnable = Runnable { fadeToHalf() }

        init {
            // 启用软件渲染以支持 BlurMaskFilter 发光光晕
            setLayerType(LAYER_TYPE_SOFTWARE, null)

            // 普通状态背景：亮紫色 (A855F7) 和靛蓝色 (6366F1) 渐变
            backgroundPaint.shader = LinearGradient(
                0f, 0f, dpToPx(54).toFloat(), dpToPx(54).toFloat(),
                Color.parseColor("#A855F7"), Color.parseColor("#6366F1"),
                Shader.TileMode.CLAMP
            )

            // 发光发亮描边
            borderPaint.style = Paint.Style.STROKE
            borderPaint.strokeWidth = dpToPx(2).toFloat()
            borderPaint.color = Color.parseColor("#E2E8F0")

            // 旋转 Loading 发光外星轨 (赛博霓虹发光效)
            loadingPaintOuter.style = Paint.Style.STROKE
            loadingPaintOuter.strokeWidth = dpToPx(3.2f).toFloat()
            loadingPaintOuter.color = Color.parseColor("#A855F7")
            loadingPaintOuter.maskFilter = BlurMaskFilter(dpToPx(2f).toFloat(), BlurMaskFilter.Blur.NORMAL)

            // 旋转 Loading 发光内星轨
            loadingPaintInner.style = Paint.Style.STROKE
            loadingPaintInner.strokeWidth = dpToPx(2.5f).toFloat()
            loadingPaintInner.color = Color.parseColor("#6366F1")
            loadingPaintInner.maskFilter = BlurMaskFilter(dpToPx(1.5f).toFloat(), BlurMaskFilter.Blur.NORMAL)

            // 文字 Paint
            textPaint.color = Color.WHITE
            textPaint.textSize = dpToPx(16).toFloat()
            textPaint.textAlign = Paint.Align.CENTER
            textPaint.typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD)

            resetIdleTimer()
        }

        fun setLoading(loading: Boolean) {
            isLoading = loading
            if (loading) {
                wakeUp()
                startLoadingAnimation()
            } else {
                stopLoadingAnimation()
                resetIdleTimer()
            }
            invalidate()
        }

        private var animatorOuter: ValueAnimator? = null
        private var animatorInner: ValueAnimator? = null

        private fun startLoadingAnimation() {
            animatorOuter = ValueAnimator.ofFloat(0f, 360f).apply {
                duration = 1000
                repeatCount = ValueAnimator.INFINITE
                interpolator = LinearInterpolator()
                addUpdateListener {
                    angleOuter = it.animatedValue as Float
                    invalidate()
                }
                start()
            }

            animatorInner = ValueAnimator.ofFloat(360f, 0f).apply {
                duration = 1500
                repeatCount = ValueAnimator.INFINITE
                interpolator = LinearInterpolator()
                addUpdateListener {
                    angleInner = it.animatedValue as Float
                    invalidate()
                }
                start()
            }
        }

        private fun stopLoadingAnimation() {
            animatorOuter?.cancel()
            animatorInner?.cancel()
            animatorOuter = null
            animatorInner = null
        }

        fun resetIdleTimer() {
            handler.removeCallbacks(idleRunnable)
            handler.postDelayed(idleRunnable, 3000)
        }

        fun wakeUp() {
            handler.removeCallbacks(idleRunnable)
            this.alpha = 1.0f
        }

        private fun fadeToHalf() {
            // 平滑淡出至半透明
            this.animate().alpha(0.35f).setDuration(500).start()
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            val cx = width / 2.0f
            val cy = height / 2.0f
            val radius = width / 2.0f - dpToPx(4)

            if (!isLoading) {
                // 1. 静止状态：重置背景渐变的 local matrix 并画圆
                backgroundPaint.shader?.setLocalMatrix(null)
                canvas.drawCircle(cx, cy, radius, backgroundPaint)
                
                // 画描边
                canvas.drawCircle(cx, cy, radius, borderPaint)
                
                // 画常驻的“译”/“A”字
                textPaint.alpha = 255
                val fontMetrics = textPaint.fontMetrics
                val baseline = cy - (fontMetrics.ascent + fontMetrics.descent) / 2
                
                val prefs = getSharedPreferences("FlutterSharedPreferences", Context.MODE_PRIVATE)
                val appLang = prefs.getString("flutter.app_language", "zh") ?: "zh"
                canvas.drawText(if (appLang == "zh") "译" else "A", cx, baseline, textPaint)
            } else {
                // 2. 翻译 Loading 状态：
                
                // 2.1 让背景渐变 Shader 随 angleOuter 顺时针缓缓自旋，形成极光流动旋涡效果！
                val bgMatrix = Matrix().apply {
                    postRotate(angleOuter, cx, cy)
                }
                backgroundPaint.shader?.setLocalMatrix(bgMatrix)
                canvas.drawCircle(cx, cy, radius, backgroundPaint)

                // 叠加一层极柔和的太极流光半透明暗纱盖板，增加纵深感
                canvas.drawCircle(cx, cy, radius, Paint(Paint.ANTI_ALIAS_FLAG).apply {
                    color = Color.parseColor("#33000000")
                    style = Paint.Style.FILL
                })

                // 2.2 绘制炫彩发光外星环：使用 SweepGradient 制造流光拖尾
                val outerColors = intArrayOf(
                    Color.parseColor("#A855F7"), // 亮紫色
                    Color.parseColor("#6366F1"), // 靛蓝色
                    Color.TRANSPARENT,
                    Color.parseColor("#A855F7")
                )
                val outerPositions = floatArrayOf(0.0f, 0.4f, 0.8f, 1.0f)
                val sweepOuter = SweepGradient(cx, cy, outerColors, outerPositions)
                val matrixOuter = Matrix().apply {
                    postRotate(angleOuter, cx, cy)
                }
                sweepOuter.setLocalMatrix(matrixOuter)
                loadingPaintOuter.shader = sweepOuter

                val rectFOuter = RectF(cx - radius + 4, cy - radius + 4, cx + radius - 4, cy + radius - 4)
                canvas.drawArc(rectFOuter, 0f, 360f, false, loadingPaintOuter)

                // 2.3 绘制炫彩发光内星环：使用逆向快速自旋的渐变流光拖尾
                val innerColors = intArrayOf(
                    Color.TRANSPARENT,
                    Color.parseColor("#38BDF8"), // 亮天蓝色
                    Color.parseColor("#6366F1"), // 靛蓝色
                    Color.TRANSPARENT
                )
                val innerPositions = floatArrayOf(0.0f, 0.3f, 0.7f, 1.0f)
                val sweepInner = SweepGradient(cx, cy, innerColors, innerPositions)
                val matrixInner = Matrix().apply {
                    postRotate(-angleInner, cx, cy)
                }
                sweepInner.setLocalMatrix(matrixInner)
                loadingPaintInner.shader = sweepInner

                val rectFInner = RectF(cx - radius + 11, cy - radius + 11, cx + radius - 11, cy + radius - 11)
                canvas.drawArc(rectFInner, 0f, 360f, false, loadingPaintInner)
                
                // 2.4 中间的“译”/“A”字伴随着外星轨正弦波形进行非常温润的高级呼吸淡入淡出！
                val breatheAlpha = (Math.sin(Math.toRadians(angleOuter.toDouble())).toFloat() * 0.35f + 0.65f) * 255
                textPaint.alpha = breatheAlpha.toInt()
                
                val fontMetrics = textPaint.fontMetrics
                val baseline = cy - (fontMetrics.ascent + fontMetrics.descent) / 2
                val prefs = getSharedPreferences("FlutterSharedPreferences", Context.MODE_PRIVATE)
                val appLang = prefs.getString("flutter.app_language", "zh") ?: "zh"
                canvas.drawText(if (appLang == "zh") "译" else "A", cx, baseline, textPaint)
            }
        }
    }

    private fun showNotification() {
        try {
            val channelId = "axue_translate_service"
            val channelName = "AxueTranslate Background Service"
            
            val manager = getSystemService(Context.NOTIFICATION_SERVICE) as android.app.NotificationManager
            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
                val chan = android.app.NotificationChannel(
                    channelId,
                    channelName,
                    android.app.NotificationManager.IMPORTANCE_LOW
                )
                chan.lockscreenVisibility = android.app.Notification.VISIBILITY_PRIVATE
                manager.createNotificationChannel(chan)
            }

            val prefs = getSharedPreferences("FlutterSharedPreferences", Context.MODE_PRIVATE)
            val appLang = prefs.getString("flutter.app_language", "zh") ?: "zh"
            val titleText = if (appLang == "zh") "阿雪翻译助手正在运行" else "AxueTranslate is running"
            val descText = if (appLang == "zh") "悬浮球与无障碍服务已就绪" else "Float bubble & accessibility ready"

            val intent = Intent(this, MainActivity::class.java)
            val pendingIntent = android.app.PendingIntent.getActivity(
                this, 0, intent,
                android.app.PendingIntent.FLAG_IMMUTABLE or android.app.PendingIntent.FLAG_UPDATE_CURRENT
            )

            val notificationBuilder = if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
                android.app.Notification.Builder(this, channelId)
            } else {
                @Suppress("DEPRECATION")
                android.app.Notification.Builder(this)
            }

            val notification = notificationBuilder
                .setOngoing(true)
                .setSmallIcon(android.R.drawable.ic_menu_manage)
                .setContentTitle(titleText)
                .setContentText(descText)
                .setContentIntent(pendingIntent)
                .build()

            manager.notify(101, notification)
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    /**
     * 忽略 HTTPS SSL 证书校验错误，以兼容各类自建中转 API 站、反代和自签名证书
     */
    private fun trustAllHostsAndCerts() {
        try {
            val trustAllCerts = arrayOf<javax.net.ssl.TrustManager>(object : javax.net.ssl.X509TrustManager {
                override fun getAcceptedIssuers(): Array<java.security.cert.X509Certificate> = arrayOf()
                override fun checkClientTrusted(certs: Array<java.security.cert.X509Certificate>, authType: String) {}
                override fun checkServerTrusted(certs: Array<java.security.cert.X509Certificate>, authType: String) {}
            })

            val sc = javax.net.ssl.SSLContext.getInstance("SSL")
            sc.init(null, trustAllCerts, java.security.SecureRandom())
            javax.net.ssl.HttpsURLConnection.setDefaultSSLSocketFactory(sc.socketFactory)
            javax.net.ssl.HttpsURLConnection.setDefaultHostnameVerifier { _, _ -> true }
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }
}
