# STranslate Lite

一个从零实现的 Windows 极简划词翻译器，放在 `STranslate-lite` 仓库中，与主项目 WPF 版 STranslate 完全独立。

设计目标只有四个：

1. 常驻后台、内存占用尽量低。
2. 鼠标侧键或自定义快捷键触发，划词后立即翻译。
3. 翻译浮窗**绝不抢当前应用焦点**，不打断 IDEA、微信、浏览器里的输入。
4. 取词过程不污染剪贴板，翻译结束后恢复原内容。

## 功能

- 鼠标侧键 `XButton1`（后退键）或 `XButton2`（前进键）触发。
- 自定义全局快捷键，默认 `Ctrl + Alt + T`。
- 触发后模拟 `Ctrl+C`，固定等待 100ms，再读取选中文本。
- 取词前深拷贝剪贴板，取词后恢复文本、图片、HTML、RTF、文件列表等可复制格式。
- 默认使用免 API Key 的 Google 翻译接口；中文自动翻英文，其它语言自动翻简体中文。
- 可选 OpenAI 兼容接口，支持 DeepSeek、OpenAI 等 `/v1/chat/completions` 服务。
- 无边框、半透明、圆角浮窗，跟随鼠标位置显示。
- 浮窗 `Esc` / 鼠标移开 / 前台窗口变化时自动关闭。
- 前台应用全屏时自动禁用翻译，并放行鼠标侧键，不影响游戏和全屏视频。
- 托盘菜单、设置窗口、开机自启（当前用户，无需管理员权限）。
- 单实例运行；再次启动 exe 会唤起已有实例的设置窗口。

## 环境要求

- Windows 10 / Windows 11，x64。
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（构建）。
- 最终用户只需要 **.NET 10 Desktop Runtime**；如果使用自包含发布，则不需要安装运行时。
- 没有 NuGet 第三方依赖。

检查环境：

```powershell
dotnet --info
```

## 构建与运行

在仓库根目录执行：

```powershell
# Debug 构建
dotnet build .\src\STranslate.Lite\STranslate.Lite.csproj -c Debug

# 直接运行（WinExe，不会弹出控制台）
dotnet run --project .\src\STranslate.Lite\STranslate.Lite.csproj -c Debug
```

构建产物因为仓库的 `Directory.Build.props` 统一放在：

```text
src\.artifacts\bin\STranslate.Lite\debug\STranslate.Lite.exe
```

Release：

```powershell
dotnet build .\src\STranslate.Lite.slnx -c Release
```

产物：

```text
src\.artifacts\bin\STranslate.Lite\release\STranslate.Lite.exe
```

## 发布

### 方式一：依赖 .NET 10 Desktop Runtime 的单文件

体积较小，目标机器需要安装 .NET 10 Desktop Runtime：

```powershell
dotnet publish .\src\STranslate.Lite\STranslate.Lite.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true `
  -o .\publish\STranslate.Lite
```

运行：

```powershell
.\publish\STranslate.Lite\STranslate.Lite.exe
```

### 方式二：自包含单文件

目标机器不需要预装 .NET，体积更大：

```powershell
dotnet publish .\src\STranslate.Lite\STranslate.Lite.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o .\publish\STranslate.Lite-standalone
```

> 本程序没有使用 NativeAOT。WinForms + NativeAOT 在 .NET 10 上仍不适合作为稳定发布路径；当前框架依赖发布已经能把私有工作集控制在约 13MB。

## 使用

1. 启动 `STranslate.Lite.exe`，程序只显示托盘图标，不显示主窗口。
2. 在任意程序里选中文字。
3. 按鼠标侧键，或按 `Ctrl + Alt + T`。
4. 浮窗会在鼠标附近显示翻译结果。
5. 按 `Esc`、把鼠标移开，或切换到其它前台窗口，浮窗关闭。

托盘菜单：

- 立即翻译当前选中文本
- 触发方式（XButton1 / XButton2 / 快捷键 / 禁用）
- 快捷键与翻译设置…
- 全屏时禁用翻译
- 开机自动启动
- 打开配置目录
- 退出

再次双击 `STranslate.Lite.exe`（程序已在运行时）会唤起设置窗口，不会启动第二个进程。

## 配置

配置文件：

```text
%APPDATA%\STranslate.Lite\settings.json
```

日志：

```text
%APPDATA%\STranslate.Lite\app.log
```

日志只记录启动、钩子、翻译状态和异常，不记录用户选中的文本内容。

测试或便携运行时，可以用环境变量隔离数据目录：

```powershell
$env:STRANSLATE_LITE_HOME = "D:\temp\STranslate.Lite"
.\src\.artifacts\bin\STranslate.Lite\debug\STranslate.Lite.exe
```

配置项说明：

| 字段 | 说明 |
| --- | --- |
| `TriggerMode` | `XButton1` / `XButton2` / `Hotkey` / `Disabled` |
| `Hotkey` | `Ctrl` / `Alt` / `Shift` / `Win` + `VirtualKey` |
| `IgnoreFullscreen` | 前台全屏时是否禁用翻译 |
| `TargetLanguage` | `auto` 或 `zh-CN`、`en`、`ja` 等 |
| `Translator` | `Google` / `OpenAiCompatible` |
| `OpenAi` | OpenAI 兼容接口地址、Key、模型 |
| `AutoStart` | 是否写入当前用户 Run 注册表项 |

### OpenAI 兼容接口示例

```json
{
  "Translator": "OpenAiCompatible",
  "OpenAi": {
    "Endpoint": "https://api.deepseek.com/v1/chat/completions",
    "ApiKey": "sk-...",
    "Model": "deepseek-chat"
  }
}
```

也可以直接在设置窗口的“OpenAI 兼容接口（可选）”区域填写。API Key 会以明文保存在本机配置文件中，请注意本机安全。

## 关键实现

### 不抢焦点

浮窗同时使用：

- `WS_EX_NOACTIVATE`
- `WS_EX_TOOLWINDOW`
- `WS_EX_TOPMOST`
- `ShowWithoutActivation => true`
- `WM_MOUSEACTIVATE => MA_NOACTIVATE`
- `SetWindowPos(..., SWP_NOACTIVATE | SWP_SHOWWINDOW)`

实测在普通文本框、IDE 编辑器中触发翻译时，前台窗口句柄和键盘焦点均保持不变，可以继续输入。

### 剪贴板恢复

`ClipboardSnapshot` 在发送 `Ctrl+C` 之前抓取剪贴板：

- 先读取并深拷贝所有可克隆格式（文本、图片、HTML、RTF、文件列表、字节流等）。
- 对只暴露 `System.Drawing.Bitmap` 自定义格式的程序，额外补一份标准 `Bitmap` 格式，保证恢复后 `Clipboard.GetImage()` 仍可读取。
- 无论取词成功、失败还是被取消，都在 `finally` 中恢复快照。
- 快照中的 `Bitmap` / `Stream` 在恢复完成后释放。

### 侧键拦截

使用 `WH_MOUSE_LL` 低级鼠标钩子：

- 只在匹配当前触发方式时返回 `1`，吃掉 `WM_XBUTTONDOWN`。
- 对已经拦截的按键，同时吃掉对应的 `WM_XBUTTONUP` / `WM_XBUTTONDBLCLK`，避免下游收到“半次点击”。
- 全屏时完全放行，不拦截、不发送 `Ctrl+C`。
- 钩子回调只做判断和异步派发，不在钩子里做网络或耗时操作。

### Esc 关闭

浮窗显示期间临时安装 `WH_KEYBOARD_LL` 键盘钩子，只观察 `Esc`：

- 从不返回 `1`，不拦截按键，`Esc` 仍会传递给当前应用。
- 浮窗关闭后立即卸载键盘钩子。
- 键盘钩子安装失败时，退回 `GetAsyncKeyState` 轮询。

### 全屏判断

`FullscreenDetector` 使用 `GetForegroundWindow`、`IsZoomed`、`GetWindowLongPtr`、`MonitorFromWindow`、`GetMonitorInfo`：

- 排除带标题栏的最大化窗口，避免任务栏自动隐藏时把普通最大化窗口误判为全屏。
- 无边框最大化的游戏/播放器仍按全屏处理。
- 排除桌面、任务栏、Alt+Tab 等系统窗口。
- 只有窗口矩形覆盖所在显示器完整边界时才视为全屏。

## 内存

在 Windows 10/11 x64、.NET 10 上实测（空闲、托盘常驻）：

- 私有工作集（Task Manager 的“内存”列）：约 **13MB**。
- 总工作集：约 **50–75MB**，其中包含与所有 .NET 进程共享的运行时页面。
- 无第三方依赖，关闭了 Server GC 和并发 GC，减少空闲线程与后台内存。

如果你看到的是“总工作集”而不是“私有工作集”，数值会高于 50MB；这是框架依赖 .NET 桌面程序的正常现象。需要进一步压低总工作集，可以使用自包含裁剪发布或改写为 Win32 原生程序，但会显著增加构建复杂度。

## 已验证项目

在当前开发机上使用真实前台窗口和注入输入做过以下验证：

- XButton1 / XButton2 触发翻译。
- 触发后前台窗口句柄不变，文本框键盘焦点不变。
- 侧键默认前进/后退行为被拦截。
- 全屏窗口中侧键放行，不弹浮窗。
- 取词后纯文本剪贴板恢复。
- 取词后 4×4 位图剪贴板恢复，且恢复后仍可 `GetImage()`。
- `Esc` 关闭浮窗。
- 鼠标移开关闭浮窗。
- 自定义快捷键 `Ctrl + Alt + T` 触发。
- 再次启动 exe 唤起设置窗口。
- 设置窗口布局、保存路径显示正常。

## 已知限制

- 对以管理员权限运行的目标程序，普通权限的本程序可能无法注入 `Ctrl+C` 或收到其前台窗口信息；需要以管理员身份运行本程序。
- 终端、远程桌面、部分 IDE 的“复制”快捷键不是 `Ctrl+C`，或复制动作被终端占用，这类程序可能无法取词。
- 某些游戏、反作弊程序、UWP/安全桌面会阻止低级钩子或模拟输入。
- Google 免费接口是非官方公开端点，可能限流、需要代理或随时变化；长期使用建议配置 OpenAI 兼容接口。
- 富文本/图片格式的剪贴板恢复是“尽力而为”，个别私有格式无法深拷贝时会放弃该格式，但至少有纯文本回退。
- 浮窗显示期间按 `Esc` 会关闭浮窗，同时 `Esc` 仍会传递给当前应用；这是有意的非拦截设计。

## 目录结构

```text
src/STranslate.Lite/
├─ Program.cs                 # 入口、单实例、异常处理
├─ AppPaths.cs                # 配置/日志路径
├─ AppSettings.cs             # 配置模型
├─ SettingsStore.cs           # JSON 持久化
├─ GlobalInputHooks.cs        # 鼠标侧键钩子
├─ HotkeyWindow.cs            # RegisterHotKey 消息窗口
├─ FullscreenDetector.cs      # 全屏判断
├─ ClipboardSnapshot.cs       # 剪贴板深拷贝/恢复
├─ SelectedTextReader.cs      # Ctrl+C + 100ms + 序列号轮询
├─ TranslationService.cs      # Google / OpenAI 兼容翻译
├─ PopupForm.cs               # 无焦点半透明浮窗
├─ TriggerCoordinator.cs      # 一次取词流程协调
├─ SettingsForm.cs            # 设置窗口
├─ TrayApplicationContext.cs  # 托盘、生命周期、菜单
├─ AutoStartService.cs        # HKCU Run 开机自启
├─ NativeMethods.cs           # Win32 互操作
├─ app.manifest
└─ Resources/app.ico
```

## 卸载

1. 托盘菜单选择“退出”。
2. 删除 `%APPDATA%\STranslate.Lite`。
3. 如果开启过开机自启，删除注册表值：

```powershell
Remove-ItemProperty `
  -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
  -Name 'STranslate.Lite' `
  -ErrorAction SilentlyContinue
```

## License

本仓库沿用上游 STranslate 的 MIT License。`Resources/app.ico` 来自上游项目。