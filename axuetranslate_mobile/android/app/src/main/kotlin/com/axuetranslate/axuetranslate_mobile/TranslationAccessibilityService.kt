package com.axuetranslate.axuetranslate_mobile

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.AccessibilityServiceInfo
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.os.Bundle
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo

class TranslationAccessibilityService : AccessibilityService() {

    companion object {
        @Volatile
        var instance: TranslationAccessibilityService? = null
            private set
    }

    private var activeFocusNode: AccessibilityNodeInfo? = null

    private val wechatInputBounds = android.graphics.Rect() // 打字时截获并锁死的微信输入框绝对物理 bounds 矩形

    @Volatile
    private var lastActiveText: String = "" // 实时拦截并驻留内存的输入文字，用以瓦解微信无障碍抹除对抗

    override fun onServiceConnected() {
        super.onServiceConnected()
        instance = this
        
        // 动态微调部分参数
        val info = AccessibilityServiceInfo().apply {
            eventTypes = AccessibilityEvent.TYPE_VIEW_FOCUSED or 
                         AccessibilityEvent.TYPE_VIEW_TEXT_SELECTION_CHANGED or
                         AccessibilityEvent.TYPE_VIEW_TEXT_CHANGED
            feedbackType = AccessibilityServiceInfo.FEEDBACK_GENERIC
            flags = AccessibilityServiceInfo.FLAG_REPORT_VIEW_IDS or 
                    AccessibilityServiceInfo.FLAG_RETRIEVE_INTERACTIVE_WINDOWS
            notificationTimeout = 100
        }
        this.serviceInfo = info
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent) {
        val pkgName = event.packageName?.toString() ?: ""
        if (pkgName.isEmpty() || pkgName == packageName) {
            return
        }

        // 1. 只要是其他应用的打字、焦点、文本选中等事件，就从事件流中抽取文本同步至内存
        val eventTextList = event.text
        if (eventTextList != null && eventTextList.isNotEmpty()) {
            val t = eventTextList[0]?.toString() ?: ""
            // 不进行 IsNotBlank 校验，即使是空白、空格也强行同步，支持微信清空状态重置 lastActiveText
            lastActiveText = t.trim()
        } else {
            // 如果事件文本内容为空，且属于文本改变事件，强制将 lastActiveText 归零清空
            if (event.eventType == AccessibilityEvent.TYPE_VIEW_TEXT_CHANGED) {
                lastActiveText = ""
            }
        }

        // 2. 只要事件类型属于文本改变、获取焦点、文本选择，其 source (即便不是 EditText) 也绝对是交互焦点，强制缓存 activeFocusNode 并锁死 Bounds
        val source = event.source
        if (source != null) {
            if (event.eventType == AccessibilityEvent.TYPE_VIEW_TEXT_CHANGED || 
                event.eventType == AccessibilityEvent.TYPE_VIEW_FOCUSED ||
                event.eventType == AccessibilityEvent.TYPE_VIEW_TEXT_SELECTION_CHANGED) {
                activeFocusNode = source
                
                // 特别防御：在微信包名下，瞬间提取并记录输入框在打字那一瞬间的绝对 bounds 物理位置！
                if (pkgName == "com.tencent.mm") {
                    try {
                        val tempRect = android.graphics.Rect()
                        source.getBoundsInScreen(tempRect)
                        if (tempRect.width() > 0 && tempRect.height() > 0) {
                            wechatInputBounds.set(tempRect)
                        }
                    } catch (e: Exception) {}
                }
            } else {
                // 作为兜底，如果能通过子孙树扫描出 EditText，则缓存 EditText 节点
                val editText = findEditTextNode(source)
                if (editText != null) {
                    activeFocusNode = editText
                    if (pkgName == "com.tencent.mm") {
                        try {
                            val tempRect = android.graphics.Rect()
                            editText.getBoundsInScreen(tempRect)
                            if (tempRect.width() > 0 && tempRect.height() > 0) {
                                wechatInputBounds.set(tempRect)
                            }
                        } catch (e: Exception) {}
                    }
                }
            }
        }
    }

    private fun findEditTextNode(node: AccessibilityNodeInfo?): AccessibilityNodeInfo? {
        if (node == null) return null
        if (isEditableInputNode(node)) {
            return node
        }
        for (i in 0 until node.childCount) {
            val child = node.getChild(i) ?: continue
            val found = findEditTextNode(child)
            if (found != null) {
                return found
            }
        }
        val parent = node.parent
        if (parent != null && isEditableInputNode(parent)) {
            return parent
        }
        return null
    }

    override fun onInterrupt() {
        // 服务中断
    }

    override fun onUnbind(intent: android.content.Intent?): Boolean {
        instance = null
        return super.onUnbind(intent)
    }

    override fun onDestroy() {
        instance = null
        super.onDestroy()
    }

    /**
      * 获取当前有焦点的输入框节点
      */
    fun getFocusedNode(): AccessibilityNodeInfo? {
        val currentPkg = rootInActiveWindow?.packageName?.toString() ?: ""

        // 特别攻防防线：如果是微信，绝不盲信任何已失效的死节点引用！
        // 实时获取屏幕活体窗口节点树 rootInActiveWindow，使用“几何位置雷达”进行毫秒级抓取！
        if (currentPkg == "com.tencent.mm") {
            val root = rootInActiveWindow
            if (root != null) {
                // 1. 优先使用绝对 Bounds 空间几何雷达，完美捕捉当前显示在此物理坐标处的最新微信输入控件！
                val boundsNode = findWeChatNodeByBounds(root, wechatInputBounds)
                if (boundsNode != null) {
                    activeFocusNode = boundsNode
                    return boundsNode
                }

                // 2. 备用策略：使用类名与文字内容特征匹配
                val wechatNode = findWeChatActiveInputNode(root, lastActiveText)
                if (wechatNode != null) {
                    activeFocusNode = wechatNode
                    return wechatNode
                }
            }
        }

        // 1. 如果有缓存节点，且当前处于相同的包名窗口 (或悬浮球/系统界面)，强行免校验返回它！
        val cachedNode = activeFocusNode
        if (cachedNode != null) {
            val cachedPkg = cachedNode.packageName?.toString() ?: ""
            
            if (cachedPkg.isNotEmpty() && (currentPkg == cachedPkg || currentPkg == packageName || currentPkg.contains("systemui", ignoreCase = true))) {
                val isWeChat = cachedPkg == "com.tencent.mm"
                if (isWeChat) {
                    return cachedNode
                } else {
                    // 如果不是微信，必须在 refresh() 成功且其确实处于 has-focus 状态时才重用，防止移开焦点后仍去翻译它！
                    if (cachedNode.refresh() && cachedNode.isFocused) {
                        return cachedNode
                    }
                }
            }
        }

        // 2. 兜底策略：如果上面的高灵敏度复用由于切换了别的应用跳过了，则刷新并校验后返回。
        // 特别防线：若是微信，免去 satisfies-editable 和 isFocused 限制。若非微信，必须强制核实节点当前依旧拥有 isFocused 焦点状态，防止用户移开焦点后仍盲目翻译！
        if (cachedNode != null) {
            val isWeChat = cachedNode.packageName?.toString() == "com.tencent.mm"
            if (cachedNode.refresh()) {
                if (isWeChat) {
                    return cachedNode
                } else if (cachedNode.isFocused && isEditableInputNode(cachedNode)) {
                    return cachedNode
                }
            }
        }

        // 2. 尝试从当前活动窗口中检索，如果包名是我们自己，说明活动窗口变成了悬浮球，跳过直接通过 windows 遍历
        val root = rootInActiveWindow
        if (root != null && root.packageName != packageName) {
            // 2.1 优先寻找有焦点的可编辑节点
            var found = findFocusedEditableNode(root)
            if (found != null) {
                activeFocusNode = found
                return found
            }
            // 2.2 其次寻找含有文字的可编辑节点 (优先击中用户有输入的框，避免空的系统搜索框等干扰)
            found = findAnyEditableNodeWithText(root)
            if (found != null) {
                activeFocusNode = found
                return found
            }
            // 2.3 兜底方案：寻找窗口中的任意一个可编辑节点 (例如微信底部输入框即使在点击一瞬间失去了焦点，也能被扫描到)
            found = findAnyEditableNode(root)
            if (found != null) {
                activeFocusNode = found
                return found
            }
        }

        // 3. 遍历屏幕上所有交互式窗口，寻找其他应用的可编辑框 (避免悬浮球窗口抢焦导致 rootInActiveWindow 变成悬浮球自身)
        try {
            val activeWindows = windows
            if (activeWindows != null) {
                // 3.1 优先寻找其他应用中被聚焦的可编辑节点
                for (window in activeWindows) {
                    val windowRoot = window.root ?: continue
                    if (windowRoot.packageName == packageName) {
                        continue
                    }
                    val found = findFocusedEditableNode(windowRoot)
                    if (found != null) {
                        activeFocusNode = found
                        return found
                    }
                }
                // 3.2 其次寻找其他应用中含有文字内容的编辑节点 (排除空的系统搜索栏，完美命中含有文本的聊天输入框)
                for (window in activeWindows) {
                    val windowRoot = window.root ?: continue
                    if (windowRoot.packageName == packageName) {
                        continue
                    }
                    val found = findAnyEditableNodeWithText(windowRoot)
                    if (found != null) {
                        activeFocusNode = found
                        return found
                    }
                }
                // 3.3 兜底寻找其他应用中的任意可编辑节点
                for (window in activeWindows) {
                    val windowRoot = window.root ?: continue
                    if (windowRoot.packageName == packageName) {
                        continue
                    }
                    val found = findAnyEditableNode(windowRoot)
                    if (found != null) {
                        activeFocusNode = found
                        return found
                    }
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }

        return null
    }

    private fun isEditableInputNode(node: AccessibilityNodeInfo): Boolean {
        if (node.isEditable) {
            return true
        }
        val className = node.className
        if (className != null) {
            val nameStr = className.toString()
            if (nameStr.endsWith("EditText") || nameStr.contains("edittext", ignoreCase = true)) {
                return true
            }
        }
        return false
    }

    private fun findFocusedEditableNode(node: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        if (node.isFocused && isEditableInputNode(node)) {
            return node
        }
        for (i in 0 until node.childCount) {
            val child = node.getChild(i) ?: continue
            val found = findFocusedEditableNode(child)
            if (found != null) {
                return found
            }
        }
        return null
    }

    private fun findAnyEditableNodeWithText(node: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        val text = node.text
        if (isEditableInputNode(node) && text != null && text.toString().isNotBlank()) {
            return node
        }
        for (i in 0 until node.childCount) {
            val child = node.getChild(i) ?: continue
            val found = findAnyEditableNodeWithText(child)
            if (found != null) {
                return found
            }
        }
        return null
    }

    private fun findAnyEditableNode(node: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        if (isEditableInputNode(node)) {
            return node
        }
        for (i in 0 until node.childCount) {
            val child = node.getChild(i) ?: continue
            val found = findAnyEditableNode(child)
            if (found != null) {
                return found
            }
        }
        return null
    }

    /**
     * 获取当前焦点输入框中的文本 (或者选中的文本)
     * 返回 Pair(待翻译文本, 是否是选中文本)
     */
    fun getTargetText(): Pair<String, Boolean> {
        val node = getFocusedNode() ?: return Pair("", false)
        val pkgName = node.packageName?.toString() ?: ""
        val isWeChat = pkgName == "com.tencent.mm"
        
        // 1. 优先尝试获取选中的文本
        val nodeText = node.text
        var textVal = nodeText?.toString() ?: ""
        
        val selectionStart = node.textSelectionStart
        val selectionEnd = node.textSelectionEnd
        
        if (selectionStart >= 0 && selectionEnd > selectionStart && selectionEnd <= textVal.length) {
            val selectedText = textVal.substring(selectionStart, selectionEnd)
            if (selectedText.isNotBlank()) {
                return Pair(selectedText, true)
            }
        }

        // 2. 如果不是选中文字模式，以 lastActiveText（真实打字记录）为绝对基因防线判定！
        if (isWeChat) {
            // 微信环境下，直接用 wechat 窃听到的 lastActiveText 屏蔽掉任何 Hint
            textVal = lastActiveText
        } else {
            // 普通应用环境下，如果 lastActiveText 为空，且 node.text 不为空，那读出来的文字必然只是 Hint 占位符！
            // 另外，我们尝试读取 node 的 hintText 属性，如果它俩相同，直接判为空
            val hint = if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
                node.hintText?.toString() ?: ""
            } else ""

            if (lastActiveText.isEmpty() || textVal.trim() == hint.trim() || 
                textVal.trim() == "Send a message" || textVal.trim() == "Type a message" ||
                textVal.trim() == "发送消息" || textVal.trim() == "Message") {
                textVal = ""
            }
        }
        
        return Pair(textVal, false)
    }

    /**
     * 替换输入框中的文本
     */
    fun replaceText(newText: String): Boolean {
        val node = getFocusedNode() ?: return false
        return performReplaceTextOnNodeAndChildren(node, newText)
    }

    fun performReplaceTextOnNodeAndChildren(node: AccessibilityNodeInfo?, newText: String): Boolean {
        if (node == null) return false

        // 为了强行让输入框进入物理就绪状态，先尝试在其上申请焦点和模拟微点击
        try {
            node.performAction(AccessibilityNodeInfo.ACTION_FOCUS)
            node.performAction(AccessibilityNodeInfo.ACTION_CLICK)
        } catch (e: Exception) {}

        // 1. 尝试直接在当前节点上执行 ACTION_SET_TEXT
        try {
            val arguments = Bundle().apply {
                putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, newText)
            }
            if (node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, arguments)) {
                return true
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }

        // 2. 如果直接 SET_TEXT 失败，尝试模拟剪贴板粘贴
        if (performPasteText(node, newText)) {
            return true
        }

        // 3. 递归遍历其所有子节点进行相同操作，轰炸嵌套在底层的真实输入组件
        try {
            val childCount = node.childCount
            for (i in 0 until childCount) {
                val child = node.getChild(i) ?: continue
                if (performReplaceTextOnNodeAndChildren(child, newText)) {
                    return true
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }

        return false
    }

    private fun performPasteText(node: AccessibilityNodeInfo, newText: String): Boolean {
        try {
            val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            val clip = ClipData.newPlainText("AxueTranslate", newText)
            clipboard.setPrimaryClip(clip)
            
            node.performAction(AccessibilityNodeInfo.ACTION_FOCUS)
            node.performAction(AccessibilityNodeInfo.ACTION_CLICK)
            val selArgs = Bundle().apply {
                putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_START_INT, 0)
                putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_END_INT, node.text?.length ?: 0)
            }
            node.performAction(AccessibilityNodeInfo.ACTION_SET_SELECTION, selArgs)
            
            if (node.performAction(AccessibilityNodeInfo.ACTION_PASTE)) {
                return true
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
        return false
    }

    /**
     * 微信活体输入节点精准捕获仪
     */
    private fun findWeChatActiveInputNode(node: AccessibilityNodeInfo?, targetText: String): AccessibilityNodeInfo? {
        if (node == null) return null
        
        // 1. 优先校验类名是否为 EditText / MMEditText 或包含 Edit 的控件
        val className = node.className?.toString() ?: ""
        if (className.contains("EditText", ignoreCase = true) || className.contains("Edit", ignoreCase = true)) {
            return node
        }
        
        // 2. 其次校验其当前显示的 text 是不是正好等于我们实时缓存的打字 text，这提供了强力铁证！
        val nodeText = node.text?.toString() ?: ""
        if (targetText.isNotEmpty() && nodeText == targetText) {
            return node
        }
        
        // 3. 深度递归向下搜索子孙节点树
        try {
            val childCount = node.childCount
            for (i in 0 until childCount) {
                val child = node.getChild(i) ?: continue
                val found = findWeChatActiveInputNode(child, targetText)
                if (found != null) {
                    return found
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
        
        return null
    }

    /**
     * 微信输入框绝对 Bounds 空间几何坐标雷达扫描器
     */
    private fun findWeChatNodeByBounds(node: AccessibilityNodeInfo?, targetBounds: android.graphics.Rect): AccessibilityNodeInfo? {
        if (node == null || targetBounds.isEmpty) return null
        
        // 1. 获取并比对当前活体节点的屏幕绝对 Bounds。允许 8 像素以内微小位移误差以实现最高容错
        val currentBounds = android.graphics.Rect()
        try {
            node.getBoundsInScreen(currentBounds)
            if (Math.abs(currentBounds.left - targetBounds.left) <= 8 &&
                Math.abs(currentBounds.top - targetBounds.top) <= 8 &&
                Math.abs(currentBounds.right - targetBounds.right) <= 8 &&
                Math.abs(currentBounds.bottom - targetBounds.bottom) <= 8) {
                return node
            }
        } catch (e: Exception) {}
        
        // 2. 深度递归向下遍历子孙节点树
        try {
            val childCount = node.childCount
            for (i in 0 until childCount) {
                val child = node.getChild(i) ?: continue
                val found = findWeChatNodeByBounds(child, targetBounds)
                if (found != null) {
                    return found
                }
            }
        } catch (e: Exception) {}
        
        return null
    }
}
