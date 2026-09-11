$ErrorActionPreference = 'Stop'
$taskEngine = Join-Path $PSScriptRoot 'app\engine'
New-Item -ItemType Directory -Path $taskEngine -Force | Out-Null
foreach ($taskLine in Get-Content -LiteralPath (Join-Path $PSScriptRoot 'engine.lock')) {
    $taskPair = $taskLine.Split('|')
    if ($taskPair.Length -ne 2 -or $taskPair[0] -notmatch '^[A-Za-z0-9_.-]+$' -or $taskPair[1] -notmatch '^[A-F0-9]{64}$') { throw 'Invalid engine.lock entry' }
    $taskPath = Join-Path $taskEngine $taskPair[0]
    if ((Test-Path -LiteralPath $taskPath) -and (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash -eq $taskPair[1]) { continue }
    $taskTemp = $taskPath + '.partial'
    try {
        Invoke-WebRequest -Uri ('https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/1.10.2/bin/' + $taskPair[0]) -OutFile $taskTemp -UseBasicParsing -TimeoutSec 60
        if ((Get-FileHash -LiteralPath $taskTemp -Algorithm SHA256).Hash -ne $taskPair[1]) { throw ('Upstream hash mismatch: ' + $taskPair[0]) }
        Move-Item -LiteralPath $taskTemp -Destination $taskPath -Force
    } finally { if (Test-Path -LiteralPath $taskTemp) { Remove-Item -LiteralPath $taskTemp } }
}
Write-Output 'Engine files match the pinned manifest.'
