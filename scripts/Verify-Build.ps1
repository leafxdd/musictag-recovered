[CmdletBinding()]
param(
    [string[]] $Configurations = @('Debug', 'Release'),
    [string] $Platform = 'Any CPU',
    [string] $ArtifactsDir = 'artifacts',
    [switch] $RunSmokeTests
)

$ErrorActionPreference = 'Stop'

function Resolve-MSBuild {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $msbuildPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($msbuildPath -and (Test-Path -LiteralPath $msbuildPath)) {
            return $msbuildPath
        }
    }

    $knownPaths = @(
        'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
    )
    foreach ($path in $knownPaths) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    throw 'MSBuild not found. Install Visual Studio Build Tools with the MSBuild component or run from a Developer PowerShell.'
}

function Invoke-CharacterizationTests {
    $exe = Resolve-Path -LiteralPath 'src\MusicTag.Tests\bin\Release\net481\MusicTag.Tests.exe'
    & $exe.Path
    if ($LASTEXITCODE -ne 0) {
        throw 'Characterization tests failed.'
    }
}

function Invoke-FilenameRelatedBatchDialogSmokeTest {
    $command = @'
$ErrorActionPreference = 'Stop'
$exe = Resolve-Path -LiteralPath 'src\MusicTag\bin\Release\net481\MusicTag.exe'
Add-Type -AssemblyName System.Windows.Forms
$assembly = [System.Reflection.Assembly]::LoadFrom($exe.Path)
$type = $assembly.GetType('MusicTag.Schemes.FilenameRelatedBatchDialog', $true)
$form = [Activator]::CreateInstance($type, $true)
try { 'Constructed=' + $form.GetType().FullName } finally { if ($form -is [System.IDisposable]) { $form.Dispose() } }
'@
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command $command
    if ($LASTEXITCODE -ne 0) {
        throw 'FilenameRelatedBatchDialog smoke test failed.'
    }
}

function Invoke-OptionsDialogSmokeTest {
    $command = @'
$ErrorActionPreference = 'Stop'
$exe = Resolve-Path -LiteralPath 'src\MusicTag\bin\Release\net481\MusicTag.exe'
Add-Type -AssemblyName System.Windows.Forms
$assembly = [System.Reflection.Assembly]::LoadFrom($exe.Path)
$type = $assembly.GetType('MusicTag.Importers.OptionsDialog', $true)
$form = [Activator]::CreateInstance($type, $true)
try { 'Constructed=' + $form.GetType().FullName } finally { if ($form -is [System.IDisposable]) { $form.Dispose() } }
'@
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command $command
    if ($LASTEXITCODE -ne 0) {
        throw 'OptionsDialog smoke test failed.'
    }
}

function Invoke-ReleaseStartupSmokeTest {
    $command = @'
$ErrorActionPreference = 'Stop'
$exe = Resolve-Path -LiteralPath 'src\MusicTag\bin\Release\net481\MusicTag.exe'
Get-Process | Where-Object { $_.Path -eq $exe.Path } | Stop-Process -Force -ErrorAction SilentlyContinue
$process = Start-Process -FilePath $exe.Path -WorkingDirectory (Split-Path -LiteralPath $exe.Path) -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 5
$stayedAlive = -not $process.HasExited
if ($stayedAlive) { Stop-Process -Id $process.Id -Force }
'StartedAndStayedAlive=' + $stayedAlive
if (-not $stayedAlive) { exit 1 }
'@
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command $command
    if ($LASTEXITCODE -ne 0) {
        throw 'Release startup smoke test failed.'
    }
}

$msbuild = Resolve-MSBuild
New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

foreach ($configuration in $Configurations) {
    $logPath = Join-Path $ArtifactsDir "msbuild-sln-$($configuration.ToLowerInvariant()).log"
    & $msbuild .\MusicTag.sln /restore "/p:Configuration=$configuration" "/p:Platform=$Platform" /m /v:minimal /fl "/flp:logfile=$logPath;verbosity=normal"
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed for configuration '$configuration'. See '$logPath'."
    }
}

if ($RunSmokeTests) {
    Invoke-CharacterizationTests
    Invoke-FilenameRelatedBatchDialogSmokeTest
    Invoke-OptionsDialogSmokeTest
    Invoke-ReleaseStartupSmokeTest
}
