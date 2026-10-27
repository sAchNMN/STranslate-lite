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
- 内置 **15 个免密钥翻译引擎**（国内外），另有 4 个本地 / 自建服务和通用大模型接口，共 20 个可选引擎。
- 目标语言支持自动识别与 21 种指定语言。
- 无边框、半透明、圆角浮窗，跟随鼠标位置显示，标题栏显示引擎名与语言方向。
- 浮窗 `Esc` / 鼠标移开 / 前台窗口变化时自动关闭。
- 前台应用全屏时自动禁用翻译，并放行鼠标侧键，不影响游戏和全屏视频。
- 托盘菜单可直接切换翻译引擎，也可切换触发方式、显示配置目录。
- 托盘菜单、设置窗口、开机自启（当前用户，无需管理员权限）。
- 设置窗口内置“测试当前引擎”按钮，随时确认当前网络下引擎是否可用。
- 单实例运行；再次启动 exe 会唤起已有实例的设置窗口。

## 翻译引擎

全部引擎均可在设置窗口或托盘菜单切换，无需修改代码。

### 免密钥直连（15 个）

| 引擎 | 区域 | 协议要点 | 备注 |
| --- | --- | --- | --- |
| Google 翻译 | 国外 | `translate.googleapis.com` 网页接口 | 429 时自动回退 `clients5.google.com` |
| 微软翻译 | 国外 | Edge 内置 `translatetext` 端点 | 支持批量与自动识别 |
| DeepL | 国外 | `www2.deepl.com` JSON-RPC | 网页接口按 IP 限流，429 时换其它引擎 |
| Yandex | 国外 | Android 客户端 `tr.json` 接口 | 不区分繁体中文；部分网络返回 403 |
| Reverso | 国外 | 官方网页 `translation.web` 接口 | 可能被 Cloudflare 访问验证拦截 |
| MyMemory | 国外 | 官方匿名 `get` 接口 | 匿名每天约 5000 字符 |
| Apertium | 国外 | 开源 APY `translate` 接口 | 仅部分欧洲语言，无中日韩 |
| Lara 翻译 | 国外 | `webapi.laratranslate.com` | 官方网页匿名调用 |
| Lingvanex | 国外 | `api-b2b.backenster.com` | 自动获取网页匿名令牌 |
| B站翻译 | 国内 | `index-translate.bilibili.com` | Index-Translate-35B-A3B 大模型，质量较高 |
| 腾讯交互翻译 | 国内 | `transmart.qq.com/api/imt` | 官方证书过期期间不可用（见“已知限制”） |
| 火山翻译 | 国内 | `translate.volcengine.com/crx` | 网页批量接口 |
| 阿里翻译 | 国内 | `translate.alibaba.com` | 临时 CSRF + multipart |
| 有道翻译 | 国内 | `dict.youdao.com/jsonapi_s` | 仅中英互译，500 字分片 |
| 金山词霸 | 国内 | `ifanyi.iciba.com` | 仅中英互译，AES 签名与响应解密 |

### 本地 / 自建（4 个）

| 引擎 | 默认地址 | 说明 |
| --- | --- | --- |
| Ollama | `http://127.0.0.1:11434` | 本地大模型，默认模型 `qwen2.5:7b` |
| LibreTranslate | `http://127.0.0.1:5000` | 自建实例，可填可选 API Key |
| DeepLX | `http://127.0.0.1:1188` | DeepL 兼容协议 |
| MTranServer | `http://127.0.0.1:8989` | 本地离线翻译服务器 |

### 大模型接口（1 个）

| 引擎 | 默认值 | 说明 |
| --- | --- | --- |
| OpenAI 兼容接口 | `https://api.openai.com/v1/chat/completions` / `gpt-4o-mini` | 可接 DeepSeek、Moonshot、通义、LM Studio 等 |

### 引擎行为约定

- 每个引擎独立保存自己的地址 / Key / 模型，切换引擎不会互相覆盖。
- 长文本按引擎限制自动分片（按 Unicode 码点或 UTF-8 字节），在换行与句末标点处优先断开，译文按原顺序拼回。
- 引擎不支持目标语言时会给出明确提示，而不是静默返回原文。
- 有道、词霸、MyMemory、Apertium 不支持自动识别，程序用本地语种识别补齐源语言。
- 所有网络错误统一提示为“超时 / 网络失败 / 证书校验失败 / 限流”等中文原因。

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
- 翻译引擎（20 个引擎快速切换）
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
| `TargetLanguage` | `auto` 或 `zh-CN`、`zh-TW`、`en`、`ja` 等 |
| `Translator` | 引擎名，取值为 `Google`、`Microsoft`、`Bilibili`、`Transmart`、`Volcengine`、`Alibaba`、`Youdao`、`Iciba`、`Lara`、`Lingvanex`、`MyMemory`、`Apertium`、`DeepL`、`Yandex`、`Reverso`、`Ollama`、`LibreTranslate`、`DeepLx`、`MTranServer`、`OpenAiCompatible` |
| `Services` | 按引擎名保存的 `Endpoint` / `ApiKey` / `Model`，只保留填写过的引擎 |
| `AutoStart` | 是否写入当前用户 Run 注册表项 |

`Translator` 使用字符串枚举（不区分大小写）。v1 版本的 `OpenAi` 配置在加载时会自动迁移为
`Services.OpenAiCompatible`，不会丢失。

### 配置示例

```json
{
  "Version": 2,
  "Translator": "Bilibili",
  "TargetLanguage": "auto",
  "Services": {
    "OpenAiCompatible": {
      "Endpoint": "https://api.deepseek.com/v1",
      "ApiKey": "sk-...",
      "Model": "deepseek-chat"
    },
    "Ollama": {
      "Endpoint": "http://127.0.0.1:11434",
      "Model": "qwen2.5:7b"
    },
    "LibreTranslate": {
      "Endpoint": "http://127.0.0.1:5000",
      "ApiKey": ""
    }
  }
}
```

也可以直接在设置窗口的“引擎配置”区域填写，改动只作用于当前选中的引擎。
API Key 会以明文保存在本机配置文件中，请注意本机安全。

自建服务只需要填基地址，程序会自动补全路径：

| 引擎 | 基地址示例 | 实际请求 |
| --- | --- | --- |
| Ollama | `http://127.0.0.1:11434` | `POST /api/chat` |
| LibreTranslate | `http://127.0.0.1:5000` | `POST /translate` |
| DeepLX | `http://127.0.0.1:1188` | `POST /translate` |
| MTranServer | `http://127.0.0.1:8989` | `POST /translate` |
| OpenAI 兼容 | `https://api.deepseek.com/v1` | `POST /chat/completions` |

## 引擎诊断

程序内置两个命令行诊断入口，发布后的单文件 exe 也能直接使用。

### 逐引擎连通性检查

```powershell
# 检查全部引擎，报告写到指定文件
.\STranslate.Lite.exe --probe --out .\probe.txt

# 只检查指定引擎，限制每次 10 秒
.\STranslate.Lite.exe --probe --engine Google,DeepL,Youdao --timeout 10

# 自定义测试文本与目标语言，输出完整译文
.\STranslate.Lite.exe --probe --engine Bilibili --text "Hello world" --target ja --full
```

报告逐行给出 `引擎 / 成功或失败 / 耗时 / 译文或错误原因`，失败时附带底层错误信息。
本地 / 自建引擎未运行时会显示“由于目标计算机积极拒绝，无法连接”，属于预期结果。

### 设置窗口与配置自检

```powershell
.\STranslate.Lite.exe --uicheck
```

在临时目录里依次跑：分片与语言码断言、v1 → v2 配置迁移与读写往返、
设置窗口构造与全部引擎切换，最后把窗口渲染成图片，方便在没有交互桌面的机器上检查布局。结果写入：

```text
%TEMP%\stranslate-lite-uicheck.txt
%TEMP%\stranslate-lite-settings.png          （免配置引擎，例如 Google）
%TEMP%\stranslate-lite-settings-config.png   （需配置引擎，例如 OpenAI 兼容）
```

设置窗口里的“测试当前引擎”按钮使用与 `--probe` 相同的逻辑，可在修改配置后立即验证。

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

### 引擎抽象

`Engines/` 下每个引擎只实现“文本 → 供应商协议 → 译文”，公共部分由基类统一处理：

- `TranslateEngine` 声明引擎名、分组、是否需要地址 / Key / 模型、默认值。
- `EngineRegistry` 维护固定顺序的引擎表，设置窗口、托盘菜单、诊断命令共用同一份注册表。
- `LanguageCodes` 负责内部语言码（`zh-CN` / `zh-TW` / `en`…）与各家协议码（`zh-Hans`、`eng`、`zh-CHS`、`en_GB`…）互转。
- `LanguageDetector` 为不支持自动识别的引擎提供保守的本地语种识别。
- `TextChunker` 按码点或 UTF-8 字节分片，避免切断代理对、优先在换行和句末标点处断开。
- `EngineHttp` 统一 JSON 请求、状态码错误文案与证书错误识别。
- `TranslationService` 只做调度：解析目标语言、调用引擎、归一化超时 / 网络 / JSON 错误。

新增引擎只需继承 `TranslateEngine` 并在 `EngineRegistry` 里注册一行，设置界面和托盘菜单会自动出现。

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

引擎实现只增加启动时的少量对象（每个引擎一个单例实例），不持有连接池之外的资源，
HttpClient 全局共享，因此引擎数量增加对常驻内存几乎没有影响。

如果你看到的是“总工作集”而不是“私有工作集”，数值会高于 50MB；这是框架依赖 .NET 桌面程序的正常现象。需要进一步压低总工作集，可以使用自包含裁剪发布或改写为 Win32 原生程序，但会显著增加构建复杂度。

## 已验证项目

在当前开发机上使用真实前台窗口、注入输入和真实网络请求做过以下验证：

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
- 20 个引擎逐一发起真实请求，确认可用的引擎返回正确中文译文：
  Google、微软、B站、火山、阿里、有道、金山词霸、Lara、Lingvanex、MyMemory、Apertium。
- 长文本分片：英文 660 字、中文 270 字分别测试 6 / 3 个引擎，结尾标记完整保留。
- 中文 → 英文方向：微软、有道、词霸均返回完整英文译文。
- 设置窗口构造与全部引擎切换的冒烟检查（`--uicheck`）通过，含分片不丢字 / 不切代理对、v1 配置迁移断言。

## 已知限制

- 对以管理员权限运行的目标程序，普通权限的本程序可能无法注入 `Ctrl+C` 或收到其前台窗口信息；需要以管理员身份运行本程序。
- 终端、远程桌面、部分 IDE 的“复制”快捷键不是 `Ctrl+C`，或复制动作被终端占用，这类程序可能无法取词。
- 某些游戏、反作弊程序、UWP/安全桌面会阻止低级钩子或模拟输入。
- 免密钥接口都是各家的非公开网页协议：可能限流、变更、要求访问验证，或按 IP / 地区限制。实测中：
  - 腾讯交互翻译的服务端证书已于 2026-10-12 过期，在腾讯续期前会一直提示“证书校验失败”。
  - DeepL 网页接口对数据中心 / 共享出口 IP 返回 429，家庭网络通常正常。
  - Yandex 与 Reverso 在部分网络返回 403，可作为备用引擎。
  - Google 主接口限流时会自动回退到 `clients5.google.com`，两者都失败才报错。
- MyMemory 匿名额度约每天 5000 字符；有道、词霸只支持中英互译；Apertium 不含中日韩。
- 本地 / 自建引擎需要先自行安装对应服务，未启动时提示“目标计算机积极拒绝”。
- 富文本/图片格式的剪贴板恢复是“尽力而为”，个别私有格式无法深拷贝时会放弃该格式，但至少有纯文本回退。
- 浮窗显示期间按 `Esc` 会关闭浮窗，同时 `Esc` 仍会传递给当前应用；这是有意的非拦截设计。

## 目录结构

```text
src/STranslate.Lite/
├─ Program.cs                 # 入口、命令行诊断分发、单实例、异常处理
├─ AppPaths.cs                # 配置/日志路径
├─ AppSettings.cs             # 配置模型、引擎枚举、按引擎保存的服务参数
├─ SettingsStore.cs           # JSON 持久化与 v1 配置迁移
├─ GlobalInputHooks.cs        # 鼠标侧键钩子
├─ HotkeyWindow.cs            # RegisterHotKey 消息窗口
├─ FullscreenDetector.cs      # 全屏判断
├─ ClipboardSnapshot.cs       # 剪贴板深拷贝/恢复
├─ SelectedTextReader.cs      # Ctrl+C + 100ms + 序列号轮询
├─ TranslationService.cs      # 调度引擎、语言解析、错误归一
├─ EngineProbe.cs             # --probe 命令行诊断
├─ UiSmokeTest.cs             # --uicheck 自检（分片 / 配置迁移 / 设置窗口）
├─ Engines/
│  ├─ TranslateEngine.cs      # 引擎基类、语言码、语种识别、分片、HTTP 工具
│  ├─ EngineRegistry.cs       # 引擎注册表（顺序即界面顺序）
│  ├─ FreeEngines.cs          # Google、微软、B站
│  ├─ ChineseEngines.cs       # 腾讯、火山、阿里、有道、词霸
│  ├─ InternationalEngines.cs # Lara、Lingvanex、MyMemory、Apertium、DeepL、Yandex、Reverso
│  └─ SelfHostedEngines.cs    # Ollama、LibreTranslate、DeepLX、MTranServer、OpenAI 兼容
├─ PopupForm.cs               # 无焦点半透明浮窗
├─ TriggerCoordinator.cs      # 一次取词流程协调
├─ SettingsForm.cs            # 设置窗口（引擎选择、引擎配置、测试按钮）
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