# AxueTranslate (译)

[![License](https://img.shields.io/badge/License-Apache--2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078d7.svg)](#)
[![Version](https://img.shields.io/badge/Version-v1.0.0-purple.svg)](https://github.com/cwr-1219/AxueTranslate/releases)

**AxueTranslate** 是一款专为 Windows 用户打造的、基于 WPF 架构的高质感大语言模型（LLM）驱动划词与全选翻译替换助手。

只需一次配置，即可在任意编辑软件中通过快捷键一键将选中（或未选中但处于输入框内）的文本翻译并直接原位替换。支持 DeepSeek、小米大模型、自定义大模型等主流 API，为您的工作流带来丝滑的语言转换体验。

---

## 🌟 核心功能特性

* **⚡ 划词翻译替换**：鼠标选中任意编辑框中的文本，按下快捷键（默认 `Ctrl + Alt + T`），即可自动拉取大模型进行精准翻译并原位粘贴替换。
* **🧠 智能全选翻译**：在无选中文本的输入框中，程序将智能全选输入框内所有文本进行翻译并自动替换，且 100% 保持原窗口焦点不丢失。
* **🎨 英文语气与风格润色**：专为英语学习和办公场景设计。当翻译至英语时，可自由切换语气风格：
  * 🇺🇸 日常美式口语
  * 🇬🇧 地道英式口语
  * 💼 职场商务英语
  * 📝 雅思学术英语
  * ⚡ 极简流利表达
  * 📄 默认标准翻译
* **💎 精致 Fluent 暗色圆角 UI**：深邃唯美的深色调设计，带有渐变呼吸色标题栏、精美圆角、柔和阴影，配合流畅、100% 居中无抖动的 Fluent Loading 加载动画轨道，带来极佳的视觉享受。
* **🛡️ 多进程高稳定性设计**：针对微信等第三方工具可能抢占、锁死或擦除剪贴板的痛点，内置多进程竞争保护机制。包含 5 次延迟写入重试与独创的 Guid 临时空标记检测，彻底解决崩溃与失效问题。

---

## 🚀 如何安装与使用

### 1. 下载与安装
我们在 GitHub 的 [Releases 页面](https://github.com/cwr-1219/AxueTranslate/releases/tag/v1.0.0) 提供了两种分发格式：
* **标准安装版 (`AxueTranslate_v1.0.0_windows_x64-setup.exe`)**：一键安装，自动在桌面及开始菜单创建快捷方式。
* **免安装绿色版 (`AxueTranslate_v1.0.0_windows_x64.zip`)**：解压即可直接运行主程序 `AxueTranslate.exe`。

### 2. 快捷配置
1. 启动软件，软件默认会最小化到系统右下角托盘。**双击托盘图标**即可唤出配置界面。
2. 配置您的大模型 API 信息（以 DeepSeek 为例）：
   * **API 接口地址**：输入服务商地址（如 `https://api.deepseek.com/v1`）
   * **API Key**：输入您的 API 密钥。
   * **模型名称**：如 `deepseek-chat`。也可点击右侧的 **“切换模型”** 按钮拉取您的可用模型列表进行点选。
3. 选择您需要的目标翻译语种（如“自动双向翻译”或特定语种）。
4. 在“全局快捷键”输入框中点击并按下您想要的快捷组合键（默认 `Ctrl + Alt + T`）进行录入。
5. 点击 **“保存配置并最小化到后台”**。

### 3. 日常使用
* **划词翻译**：鼠标选中一段文字 $\rightarrow$ 按下快捷键 $\rightarrow$ 等待 1-2 秒，选中的文本将被替换为翻译好的文本。
* **全选翻译**：在任意输入框中（不选中任何文字） $\rightarrow$ 按下快捷键 $\rightarrow$ 该输入框的所有文字将被自动全选、翻译并直接替换。

---

## 🛠️ 开发者指南 (如何编译)

如果您希望基于源码进行二次开发或自行打包，请参考以下指南：

### 环境要求
* Windows 操作系统
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Inno Setup 6](https://jrsoftware.org/isinstall.php) (可选，仅用于打包 Windows 安装包)

### 编译发布步骤
1. **克隆仓库**：
   ```bash
   git clone https://github.com/cwr-1219/AxueTranslate.git
   cd AxueTranslate/SpeedTranslate
   ```
2. **发布二进制产物**：
   在 `SpeedTranslate` 项目目录下运行以下 dotnet 发布命令，生成 win-x64 的独立自包含发布件：
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained true
   ```
   发布成功后，编译好的程序及依赖会生成在：
   `SpeedTranslate\bin\Release\net8.0-windows\win-x64\publish\` 目录中。

3. **打包安装包**：
   打开 Inno Setup 编译器，加载项目根目录下的 `setup.iss` 配置文件，点击 **Compile**。编译完成后，将在 `SpeedTranslate\bin\Release\net8.0-windows\win-x64\Setup\` 目录下生成标准 Setup 安装包。

---

## 🗺️ 产品路线图 (Roadmap)

我们致力于打造全平台、一体化的无缝极速翻译体验。以下平台版本正在开发规划中：

* [x] **Windows 桌面端** (已正式发布 v1.0.0)
* [ ] **macOS 桌面端** (研发中，敬请期待)
* [ ] **iOS 移动端** (规划中，敬请期待)
* [ ] **Android 移动端 (APK)** (规划中，敬请期待)

如果您对本软件有任何意见或建议，欢迎提交 [Issues](https://github.com/cwr-1219/AxueTranslate/issues)！

---

## ⚖️ 开源协议与商用限制声明

本项目的源码采用 **[Apache License 2.0](LICENSE)** 协议开源。

> **⚠️ 特别非商用限制声明**
>
> 尽管本项目采用 Apache 2.0 协议开源，但为了保护原创开源成果：
> **未经作者明确的书面授权，任何人严禁将本软件的全部或部分源码、编译后的可执行文件或打包安装程序用于任何商业目的。**
> 这包括但不限于：将本软件上架至应用商店兜售、打包为付费产品转售、添加内置付费广告或商业增值服务等牟利行为。商业合作请联系作者获取正式授权。
