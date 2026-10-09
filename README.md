# 小贴事源码

这是小贴事的本地任务备忘录源码与美术资源，不含个人任务数据。

## 目录

- `windows-winforms/`：当前 Windows 版，C# WinForms + WebView2。
- `mac-electron/`：macOS 版，Electron 主进程及共用的 HTML/CSS/JavaScript 界面。
- `壁纸/`：1920 × 1080 暖色书桌壁纸。

任务保存在用户目录下的 `LittleNotes/tasks.json`。Windows 路径是 `%APPDATA%\LittleNotes\tasks.json`；macOS 路径是 `~/Library/Application Support/LittleNotes/tasks.json`。

Windows 版可在装有 .NET Framework 4.x 的 Windows 上运行 `windows-winforms/构建-Windows.ps1`。运行程序还需要 Microsoft Edge WebView2 Runtime。

macOS 版使用 Electron 44.7.0。将 `mac-electron/` 的文件作为 Electron 应用的 `Contents/Resources/app/` 内容打包。公开分发前，应在对应架构的 Mac 上运行验证，并用自己的 Apple Developer 身份进行签名和公证。

字体 `Xiaolai-Regular.ttf` 按 SIL Open Font License 1.1 分发，许可见 `Xiaolai-OFL.txt`。WebView2 DLL 的许可见 Windows 目录内的许可文件。Electron 运行时的许可随 macOS 发布包提供。
