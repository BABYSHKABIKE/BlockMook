$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskRefs = @('System.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Core.dll','System.Net.Http.dll','System.Xml.dll','System.Xaml.dll','System.Web.Extensions.dll') | ForEach-Object { '/r:' + (Join-Path $taskFramework $_) }
$taskRefs += @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','UIAutomationProvider.dll','UIAutomationTypes.dll') | ForEach-Object { '/r:' + (Join-Path $taskFramework ('WPF\' + $_)) }
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'app') -Force | Out-Null
$taskSources = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$taskEngineFiles = @('winws.exe','WinDivert.dll','WinDivert64.sys','cygwin1.dll','quic_initial_www_google_com.bin','tls_clienthello_www_google_com.bin','ACTIVE_DISCORD_UDP.bin')
$taskPinned = Get-Content -LiteralPath (Join-Path $taskRoot 'engine.lock')
$taskHashes = foreach($taskName in $taskEngineFiles) {
    $taskHash = Get-FileHash -LiteralPath (Join-Path (Join-Path $taskRoot 'app\engine') $taskName) -Algorithm SHA256
    $taskEntry = $taskName + '|' + $taskHash.Hash
    if ($taskEntry -notin $taskPinned) { throw ('Engine hash mismatch: ' + $taskName + '. Run prepare-engine.ps1 or review engine.lock before building.') }
    $taskEntry
}
[IO.File]::WriteAllLines((Join-Path $taskRoot 'app\engine.sha256'),[string[]]$taskHashes)
& (Join-Path $taskFramework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/win32icon:' + (Join-Path $taskRoot 'assets\BlockMook.ico')) ('/out:' + (Join-Path $taskRoot 'app\BlockMook.exe')) ('/resource:' + (Join-Path $taskRoot 'src\Main.xaml') + ',Main.xaml') ('/resource:' + (Join-Path $taskRoot 'assets\BlockMook.ico') + ',BlockMook.ico') ('/resource:' + (Join-Path $taskRoot 'app\engine.sha256') + ',engine.sha256') ('/resource:' + (Join-Path $taskRoot 'assets\signal-root.cer') + ',signal-root.cer') @taskRefs @taskSources
if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'src\app.config') -Destination (Join-Path $taskRoot 'app\BlockMook.exe.config')
Write-Output 'Build OK: app\BlockMook.exe'
