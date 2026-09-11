$ErrorActionPreference = 'Stop'
$taskOutput = Join-Path $PSScriptRoot 'out\checks'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
foreach ($taskMode in @('test','ui-smoke')) {
    $taskReport = Join-Path $taskOutput ($taskMode + '.txt')
    $taskProcess = Start-Process -FilePath (Join-Path $PSScriptRoot 'app\BlockMook.exe') -ArgumentList @('--'+$taskMode,('"'+$taskReport+'"')) -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit(30000)) { throw ($taskMode+' timed out') }
    if (-not (Test-Path -LiteralPath $taskReport)) { throw ($taskMode+' did not write a report') }
    $taskResult = Get-Content -LiteralPath $taskReport -Raw
    if ($taskProcess.ExitCode -ne 0 -or $taskResult -match '(?m)^FAIL' -or $taskResult -notmatch 'PASS') { throw $taskResult }
    Get-Content -LiteralPath $taskReport -Tail 1
}
