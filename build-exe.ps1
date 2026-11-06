#Requires -Version 5.1
<#
.SYNOPSIS
    一键把 STranslate.Lite 构建成可直接运行的 exe，或 Velopack 安装包。

.DESCRIPTION
    双击 build-exe.cmd 会进入菜单；也可以带参数直接跑：

        .\build-exe.cmd 1                     # 单文件 exe（依赖 .NET 10 桌面运行时）
        .\build-exe.cmd 2                     # 自包含单文件 exe（免装运行时）
        .\build-exe.cmd 3                     # Velopack 安装包（Setup.exe + 便携 zip）
        .\build-exe.cmd 4                     # 三种全部生成
        .\build-exe.cmd 1 -Version 2.0.12     # 指定版本号（默认取 csproj 的 <Version>）
        .\build-exe.cmd 3 -Out D:\dist        # 指定输出根目录（默认仓库下的 publish）

.PARAMETER Mode
    1 / 2 / 3 / 4，或别名 exe / standalone / setup / all；留空进入交互菜单。

.PARAMETER Version
    覆盖版本号，可带 v 前缀。默认读取 STranslate.Lite.csproj 里的 <Version>。

.PARAMETER Out
    输出根目录，默认仓库根目录下的 publish。

.PARAMETER Open
    结束后在资源管理器中定位第一个产物。
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Mode = '',

    [string]$Version = '',

    [string]$Out = 'publish',

    [switch]$Open
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 输出辅助
# ---------------------------------------------------------------------------
function Write-Title([string]$Text) {
    Write-Host ''
    Write-Host ('=' * 66) -ForegroundColor DarkCyan
    Write-Host ('  ' + $Text) -ForegroundColor Cyan
    Write-Host ('=' * 66) -ForegroundColor DarkCyan
}

function Write-Step([string]$Text) { Write-Host ('>> ' + $Text) -ForegroundColor Cyan }
function Write-Info([string]$Text) { Write-Host ('   ' + $Text) -ForegroundColor Gray }
function Write-Ok([string]$Text) { Write-Host ('[OK]   ' + $Text) -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host ('[警告] ' + $Text) -ForegroundColor Yellow }
function Write-Fail([string]$Text) { Write-Host ('[失败] ' + $Text) -ForegroundColor Red }

function Format-Size([long]$Bytes) {
    if ($Bytes -ge 1GB) { return ('{0:N2} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0:N0} KB' -f ($Bytes / 1KB)) }
    return ($Bytes.ToString() + ' B')
}

# ---------------------------------------------------------------------------
# 路径与环境
# ---------------------------------------------------------------------------
$script:RepoRoot = Split-Path -Parent $PSCommandPath
Set-Location -LiteralPath $script:RepoRoot

$script:ProjectPath = Join-Path $script:RepoRoot 'src\STranslate.Lite\STranslate.Lite.csproj'
$script:IconPath = Join-Path $script:RepoRoot 'src\STranslate.Lite\Resources\app.ico'
$script:ExeName = 'STranslate.Lite.exe'
$script:PackId = 'STranslate.Lite'
$script:PackInput = Join-Path $script:RepoRoot 'src\.artifacts\publish\pack'
$script:Results = New-Object System.Collections.ArrayList

if ([System.IO.Path]::IsPathRooted($Out)) {
    $script:OutRoot = $Out
}
else {
    $script:OutRoot = Join-Path $script:RepoRoot $Out
}

function Add-Result([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        [void]$script:Results.Add((Get-Item -LiteralPath $Path))
    }
}

# ---------------------------------------------------------------------------
# 执行外部命令（dotnet / vpk），并检查退出码
# ---------------------------------------------------------------------------
function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$Exe,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory = $true)][string]$Step
    )

    Write-Step $Step
    Write-Info ('命令: ' + $Exe + ' ' + ($Arguments -join ' '))

    # 原生命令写 stderr 时，Stop 偏好会把它变成终止错误；这段时间改用 Continue，
    # 真正成败仍然只看退出码。
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Arguments | Out-Host
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($LASTEXITCODE -ne 0) {
        throw ($Step + ' 失败，退出码 ' + $LASTEXITCODE + '。')
    }
}

# ---------------------------------------------------------------------------
# 环境与版本
# ---------------------------------------------------------------------------
function Assert-Environment {
    Write-Step '检查构建环境'

    if (-not (Test-Path -LiteralPath $script:ProjectPath)) {
        throw ('找不到项目文件：' + $script:ProjectPath)
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw '未找到 dotnet 命令，请先安装 .NET 10 SDK：https://dotnet.microsoft.com/download/dotnet/10.0'
    }

    $sdks = @(& dotnet --list-sdks)
    $hasNet10 = $false
    foreach ($line in $sdks) {
        if ($line -match '^10\.') { $hasNet10 = $true }
    }

    if ($hasNet10) {
        Write-Ok '已找到 .NET 10 SDK'
    }
    else {
        Write-Warn ('未检测到 .NET 10 SDK，当前可用：' + ($sdks -join '; '))
        Write-Warn '继续构建很可能失败，请确认 SDK 版本。'
    }
}

function Resolve-Version {
    param([string]$Requested)

    if (-not [string]::IsNullOrWhiteSpace($Requested)) {
        return ($Requested.Trim() -replace '^[vV]', '')
    }

    $text = Get-Content -LiteralPath $script:ProjectPath -Raw
    $match = [regex]::Match($text, '<Version>\s*([^<\s]+)\s*</Version>')
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }
    return '1.0.0'
}

# ---------------------------------------------------------------------------
# 交互菜单
# ---------------------------------------------------------------------------
function Show-Menu {
    Write-Title 'STranslate.Lite 一键生成 exe'
    Write-Host '  1  单文件 exe          依赖 .NET 10 桌面运行时，体积小、构建快'
    Write-Host '  2  自包含单文件 exe    免装运行时，体积大（约 70MB+）'
    Write-Host '  3  Velopack 安装包     生成 win-Setup.exe + 便携 zip'
    Write-Host '  4  全部生成            1 + 2 + 3，耗时最长'
    Write-Host '  Q  退出'
    Write-Host ''
    Write-Info ('版本号  : ' + $script:BuildVersion)
    Write-Info ('输出目录: ' + $script:OutRoot)
}

function Read-Mode {
    while ($true) {
        $choice = Read-Host '请输入序号'
        if ([string]::IsNullOrWhiteSpace($choice)) { continue }

        switch ($choice.Trim().ToLowerInvariant()) {
            '1' { return '1' }
            '2' { return '2' }
            '3' { return '3' }
            '4' { return '4' }
            'q' { return 'q' }
            default { Write-Warn ('无效输入：' + $choice) }
        }
    }
}

function Normalize-Mode([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    switch ($Value.Trim().ToLowerInvariant()) {
        '1' { return '1' }
        'exe' { return '1' }
        'single' { return '1' }
        '2' { return '2' }
        'standalone' { return '2' }
        'selfcontained' { return '2' }
        '3' { return '3' }
        'setup' { return '3' }
        'installer' { return '3' }
        'vpk' { return '3' }
        '4' { return '4' }
        'all' { return '4' }
        'q' { return 'q' }
        '?' { return '?' }
        'help' { return '?' }
        default { throw ('未知模式：' + $Value + '（可用：1 2 3 4 / exe / standalone / setup / all）') }
    }
}

function Show-Usage {
    Write-Title '用法'
    Write-Host '  build-exe.cmd              双击或直接运行，进入菜单'
    Write-Host '  build-exe.cmd 1            单文件 exe（依赖 .NET 10 桌面运行时）'
    Write-Host '  build-exe.cmd 2            自包含单文件 exe（免装运行时）'
    Write-Host '  build-exe.cmd 3            Velopack 安装包（Setup.exe + 便携 zip）'
    Write-Host '  build-exe.cmd 4            全部生成'
    Write-Host ''
    Write-Host '  可选参数：'
    Write-Host '    -Version 2.0.12          指定版本号（默认取 csproj 的 <Version>）'
    Write-Host '    -Out D:\dist             指定输出根目录（默认 publish）'
    Write-Host '    -Open                    结束后打开产物所在目录'
}

# ---------------------------------------------------------------------------
# 模式 1 / 2：单文件 exe
# ---------------------------------------------------------------------------
function Publish-SingleFile {
    param(
        [switch]$SelfContained,
        [Parameter(Mandatory = $true)][string]$TargetDir
    )

    if ($SelfContained) { $label = '自包含单文件 exe' }
    else { $label = '单文件 exe（依赖 .NET 10 桌面运行时）' }

    Write-Title ($label + '，版本 ' + $script:BuildVersion)

    if (Test-Path -LiteralPath $TargetDir) {
        Write-Info ('清理旧产物：' + $TargetDir)
        Remove-Item -LiteralPath $TargetDir -Recurse -Force
    }

    $arguments = @(
        'publish'
        $script:ProjectPath
        '--configuration'; 'Release'
        '--runtime'; 'win-x64'
        '--self-contained'; $(if ($SelfContained) { 'true' } else { 'false' })
        '--property:PublishSingleFile=true'
        ('--property:Version=' + $script:BuildVersion)
        '--output'; $TargetDir
    )

    if ($SelfContained) {
        $arguments += '--property:IncludeNativeLibrariesForSelfExtract=true'
        $arguments += '--property:EnableCompressionInSingleFile=true'
    }

    Invoke-Native -Exe 'dotnet' -Arguments $arguments -Step 'dotnet publish'

    $exePath = Join-Path $TargetDir $script:ExeName
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw ('构建结束，但未找到 ' + $exePath)
    }

    $item = Get-Item -LiteralPath $exePath
    Write-Ok ('已生成：' + $item.FullName + '（' + (Format-Size $item.Length) + '）')
    Add-Result $exePath
}

# ---------------------------------------------------------------------------
# 模式 3：Velopack 安装包
# ---------------------------------------------------------------------------
function Get-VpkPath {
    $command = Get-Command vpk -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $toolDir = Join-Path $env:USERPROFILE '.dotnet\tools'
    $toolExe = Join-Path $toolDir 'vpk.exe'
    if (Test-Path -LiteralPath $toolExe) { return $toolExe }

    Write-Warn '未检测到 Velopack CLI（vpk），正在自动安装...'
    Invoke-Native -Exe 'dotnet' -Arguments @('tool', 'install', '--global', 'vpk') -Step '安装 vpk'

    if (Test-Path -LiteralPath $toolExe) { return $toolExe }

    $command = Get-Command vpk -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    throw 'vpk 安装后仍找不到，请手动执行 dotnet tool install -g vpk 后重试。'
}

function New-VelopackPackage {
    Write-Title ('Velopack 安装包，版本 ' + $script:BuildVersion)

    if ($script:BuildVersion -notmatch '^\d+\.\d+\.\d+') {
        throw ('版本号 "' + $script:BuildVersion + '" 不是 主.次.修订 形式，vpk 拒绝打包。')
    }

    $vpkPath = Get-VpkPath
    Write-Ok ('vpk: ' + $vpkPath)

    Write-Step '发布自包含多文件到打包输入目录'
    Write-Info ('输入目录：' + $script:PackInput)
    if (Test-Path -LiteralPath $script:PackInput) {
        Remove-Item -LiteralPath $script:PackInput -Recurse -Force
    }

    $publishArguments = @(
        'publish'
        $script:ProjectPath
        '--configuration'; 'Release'
        '--runtime'; 'win-x64'
        '--self-contained'; 'true'
        '--property:PublishSingleFile=false'
        ('--property:Version=' + $script:BuildVersion)
        '--output'; $script:PackInput
    )
    Invoke-Native -Exe 'dotnet' -Arguments $publishArguments -Step 'dotnet publish（Velopack 打包输入）'

    if (-not (Test-Path -LiteralPath (Join-Path $script:PackInput $script:ExeName))) {
        throw ('打包输入目录里缺少 ' + $script:ExeName)
    }

    if (-not (Test-Path -LiteralPath $script:OutRoot)) {
        New-Item -ItemType Directory -Path $script:OutRoot -Force | Out-Null
    }

    # 同版本的旧产物会让 vpk 直接报错，先清掉再打。
    $oldPackages = Join-Path $script:OutRoot ($script:PackId + '-' + $script:BuildVersion + '-*.nupkg')
    Get-ChildItem -Path $oldPackages -File -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Info ('删除同版本旧包：' + $_.Name)
        Remove-Item -LiteralPath $_.FullName -Force
    }
    foreach ($name in @(($script:PackId + '-win-Setup.exe'), ($script:PackId + '-win-Portable.zip'))) {
        $path = Join-Path $script:OutRoot $name
        if (Test-Path -LiteralPath $path) {
            Write-Info ('删除旧产物：' + $name)
            Remove-Item -LiteralPath $path -Force
        }
    }

    $vpkArguments = @(
        'pack'
        '-u'; $script:PackId
        '-v'; $script:BuildVersion
        '-p'; $script:PackInput
        '-o'; $script:OutRoot
        # 不显式指定时 vpk 按 x86 处理，安装目录会落到 Program Files (x86)，与 x64 主程序不符。
        '-r'; 'win-x64'
        '-e'; $script:ExeName
        '-i'; $script:IconPath
        '--packTitle'; 'STranslate.Lite'
    )
    Invoke-Native -Exe $vpkPath -Arguments $vpkArguments -Step 'vpk pack'

    $expected = @(
        ($script:PackId + '-win-Setup.exe')
        ($script:PackId + '-win-Portable.zip')
        ($script:PackId + '-' + $script:BuildVersion + '-full.nupkg')
    )
    foreach ($name in $expected) {
        $path = Join-Path $script:OutRoot $name
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path
            Write-Ok ('已生成：' + $item.Name + '（' + (Format-Size $item.Length) + '）')
            Add-Result $path
        }
    }
}

# ---------------------------------------------------------------------------
# 收尾
# ---------------------------------------------------------------------------
function Write-Summary {
    if ($script:Results.Count -eq 0) { return }

    Write-Title '生成完成'
    foreach ($item in $script:Results) {
        Write-Host ('  * ' + $item.FullName) -ForegroundColor White
        Write-Host ('    ' + (Format-Size $item.Length)) -ForegroundColor DarkGray
    }
    Write-Host ''
    Write-Host ('  输出目录：' + $script:OutRoot) -ForegroundColor Cyan
}

function Open-Output([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        Start-Process -FilePath 'explorer.exe' -ArgumentList ('/select,"' + $Path + '"') | Out-Null
    }
    catch {
        Write-Warn ('无法打开资源管理器：' + $_.Exception.Message)
    }
}

# ---------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------
$interactive = [string]::IsNullOrWhiteSpace($Mode)

try {
    $chosen = Normalize-Mode $Mode
    if ($chosen -eq '?') {
        Show-Usage
        exit 0
    }

    $script:BuildVersion = Resolve-Version -Requested $Version
    if ([string]::IsNullOrWhiteSpace($script:BuildVersion)) {
        throw '版本号为空，无法构建。'
    }

    Assert-Environment

    if ($interactive) {
        Show-Menu
        $chosen = Read-Mode
    }

    if ($chosen -eq 'q') {
        Write-Host '已取消。' -ForegroundColor Yellow
        exit 0
    }

    Write-Host ''
    Write-Info ('仓库根目录：' + $script:RepoRoot)
    Write-Info ('版本号    ：' + $script:BuildVersion)
    Write-Info ('输出目录  ：' + $script:OutRoot)

    switch ($chosen) {
        '1' {
            Publish-SingleFile -SelfContained:$false -TargetDir (Join-Path $script:OutRoot 'STranslate.Lite')
        }
        '2' {
            Publish-SingleFile -SelfContained -TargetDir (Join-Path $script:OutRoot 'STranslate.Lite-standalone')
        }
        '3' {
            New-VelopackPackage
        }
        '4' {
            Publish-SingleFile -SelfContained:$false -TargetDir (Join-Path $script:OutRoot 'STranslate.Lite')
            Publish-SingleFile -SelfContained -TargetDir (Join-Path $script:OutRoot 'STranslate.Lite-standalone')
            New-VelopackPackage
        }
    }

    Write-Summary

    if (($Open -or $interactive) -and $script:Results.Count -gt 0) {
        Open-Output $script:Results[0].FullName
    }

    exit 0
}
catch {
    Write-Host ''
    Write-Fail $_.Exception.Message
    # 真正的解析/运行时错误才附上位置，自己 throw 的业务提示不重复打印。
    if ($_.Exception -isnot [System.Management.Automation.RuntimeException] -and $_.InvocationInfo.PositionMessage) {
        Write-Host ('        ' + $_.InvocationInfo.PositionMessage.Trim()) -ForegroundColor DarkGray
    }
    exit 1
}