$ErrorActionPreference = 'Stop'
$taskFolder = Join-Path $PSScriptRoot 'distribution-sources'
New-Item -ItemType Directory -Path $taskFolder -Force | Out-Null
$taskSources = @(
    @{ Name='cygwin-3.4.10-1-src.tar.xz'; Url='https://ftp.cvut.cz/mirrors/cygwin.com/x86_64/release/cygwin/cygwin-3.4.10-1-src.tar.xz'; Hash='AC70AF0D4E644732F74946F55F7BAFE010BAB9B9DA39A9327C3F0367F1FA43A2' },
    @{ Name='WinDivert-2.2.2-source.zip'; Url='https://github.com/basil00/WinDivert/archive/refs/tags/v2.2.2.zip'; Hash='65EC79C9E6AFA99F648A3F4D1F6DB794640B40D0B65BD438770EA503EE14ECB7' }
)
foreach ($taskSource in $taskSources) {
    $taskPath = Join-Path $taskFolder $taskSource.Name
    if ((Test-Path -LiteralPath $taskPath) -and (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash -eq $taskSource.Hash) { continue }
    $taskPartial = $taskPath + '.partial'
    try {
        Invoke-WebRequest -Uri $taskSource.Url -OutFile $taskPartial -UseBasicParsing -TimeoutSec 90
        if ((Get-FileHash -LiteralPath $taskPartial -Algorithm SHA256).Hash -ne $taskSource.Hash) { throw ('Source hash mismatch: ' + $taskSource.Name) }
        Move-Item -LiteralPath $taskPartial -Destination $taskPath -Force
    } finally { if (Test-Path -LiteralPath $taskPartial) { Remove-Item -LiteralPath $taskPartial } }
}
Write-Output 'Third-party source archives verified.'
