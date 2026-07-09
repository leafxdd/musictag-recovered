[CmdletBinding()]
param(
    [string[]] $Configurations = @('Debug', 'Release'),
    [string] $ArtifactsDir = 'artifacts',
    [switch] $RunSmokeTests
)

$ErrorActionPreference = 'Stop'

# net8 迁移(experiment/per-monitor-dpi-v2):
#   - msbuild.exe -> dotnet build(net8.0-windows SDK 项目;dotnet SDK 8+)。
#   - 输出路径 net481 -> net8.0-windows。
#   - 原两个"外部 PowerShell 反射构造对话框"冒烟并入 characterization 套件
#     (Tests/DialogConstructionSmoke.cs)——外部 netfx/x64 宿主无法加载 x86 net8 程序集。
#   - 启动冒烟新增异常日志扫描:net8 首启曾出现"进程活着但挂在未处理异常弹窗"的假阳性
#     (FontAwesome 缺 System.Runtime.Caching),仅凭 StayedAlive 判定不可靠。

function Invoke-CharacterizationTests {
    $exe = Resolve-Path -LiteralPath 'src\MusicTag.Tests\bin\Release\net8.0-windows\MusicTag.Tests.exe'
    & $exe.Path
    if ($LASTEXITCODE -ne 0) {
        throw 'Characterization tests failed.'
    }
}

function Invoke-ReleaseStartupSmokeTest {
    $command = @'
$ErrorActionPreference = 'Stop'
$exe = Resolve-Path -LiteralPath 'src\MusicTag\bin\Release\net8.0-windows\MusicTag.exe'
Get-Process | Where-Object { $_.Path -eq $exe.Path } | Stop-Process -Force -ErrorAction SilentlyContinue
$binDir = Split-Path -LiteralPath $exe.Path
Remove-Item (Join-Path $binDir 'temp\Log') -Recurse -Force -ErrorAction SilentlyContinue
$process = Start-Process -FilePath $exe.Path -WorkingDirectory $binDir -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 5
$stayedAlive = -not $process.HasExited
if ($stayedAlive) { Stop-Process -Id $process.Id -Force }
'StartedAndStayedAlive=' + $stayedAlive
# 未处理异常(UnhandledException / ThreadException)落盘即失败;LoadAppSettingData 的
# JsonReaderException 是已知自愈路径(仓库自带旧版二进制 MusicTag.dat,读失败删除重建),放行。
$fatalLogs = Get-ChildItem (Join-Path $binDir 'temp\Log') -Filter '*.log' -Recurse -ErrorAction SilentlyContinue |
    Where-Object { (Get-Content $_.FullName -Raw) -match 'UnhandledException|ThreadException' }
if ($fatalLogs) {
    'FatalExceptionLogs:'
    $fatalLogs | ForEach-Object { $_.FullName; Get-Content $_.FullName -Raw }
    exit 1
}
'NoFatalExceptionLogs=True'
if (-not $stayedAlive) { exit 1 }
'@
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command $command
    if ($LASTEXITCODE -ne 0) {
        throw 'Release startup smoke test failed.'
    }
}

New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

foreach ($configuration in $Configurations) {
    $logPath = Join-Path $ArtifactsDir "build-sln-$($configuration.ToLowerInvariant()).log"
    dotnet build .\MusicTag.sln -c $configuration -v minimal "/flp:logfile=$logPath;verbosity=normal"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for configuration '$configuration'. See '$logPath'."
    }
}

if ($RunSmokeTests) {
    Invoke-CharacterizationTests
    Invoke-ReleaseStartupSmokeTest
}
