$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'prepare-sources.ps1')
$taskVersion = '1.2.0'
$taskOutput = Join-Path $PSScriptRoot ('out\packages\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$taskStage = Join-Path $taskOutput 'BlockMook'
$taskZip = Join-Path $taskOutput 'BlockMook-Windows-x64.zip'
New-Item -ItemType Directory -Path (Join-Path $taskStage 'app\engine'),(Join-Path $taskStage 'third-party-source') -Force | Out-Null
# Share only these known product inputs. Never enumerate reports or user settings.
foreach ($taskName in @('Start.cmd','Install.cmd','Install.ps1','build-brand.ps1','README.md','CHANGELOG.md','THIRD-PARTY.md','LICENSE','engine.lock','build.ps1','test.ps1','prepare-engine.ps1','prepare-sources.ps1','package.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Destination $taskStage
}
foreach ($taskName in @('src','assets','docs','distribution-licenses')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Destination $taskStage -Recurse
}
foreach ($taskName in @('BlockMook.exe','BlockMook.exe.config','engine.sha256')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('app\'+$taskName)) -Destination (Join-Path $taskStage 'app')
}
foreach ($taskLine in Get-Content -LiteralPath (Join-Path $PSScriptRoot 'engine.lock')) {
    $taskPair = $taskLine.Split('|')
    if ($taskPair[0] -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Invalid engine filename' }
    $taskPath = Join-Path $PSScriptRoot ('app\engine\'+$taskPair[0])
    if ((Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash -ne $taskPair[1]) { throw 'Engine changed after verification' }
    Copy-Item -LiteralPath $taskPath -Destination (Join-Path $taskStage 'app\engine')
}
foreach ($taskName in @('cygwin-3.4.10-1-src.tar.xz','WinDivert-2.2.2-source.zip')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('distribution-sources\'+$taskName)) -Destination (Join-Path $taskStage 'third-party-source')
}
$taskHashes = Get-ChildItem -LiteralPath $taskStage -File -Recurse | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $_.FullName.Substring($taskStage.Length+1).Replace('\','/')
}
[IO.File]::WriteAllLines((Join-Path $taskStage 'SHA256SUMS.txt'),[string[]]$taskHashes)
Compress-Archive -LiteralPath $taskStage -DestinationPath $taskZip -CompressionLevel Optimal
$taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $taskOutput 'SHA256SUMS.txt'),$taskHash+'  BlockMook-Windows-x64.zip'+[Environment]::NewLine)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'out\latest-package.txt'),$taskZip)
Write-Output $taskZip
