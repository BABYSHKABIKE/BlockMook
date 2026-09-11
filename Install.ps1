param([switch]$NoLaunch)
$ErrorActionPreference='Stop'
$taskSource=[IO.Path]::GetFullPath($PSScriptRoot)
$taskManifest=Join-Path $taskSource 'SHA256SUMS.txt'
if(-not (Test-Path -LiteralPath $taskManifest)){throw 'Run Install.cmd from the extracted release ZIP, including SHA256SUMS.txt.'}
$taskVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskSource 'app\BlockMook.exe')).FileVersion
if($taskVersion -notmatch '^([0-9]+\.[0-9]+\.[0-9]+)\.0$'){throw 'Invalid application version'}
$taskVersion=$Matches[1]
$taskBuildHash=(Get-FileHash -LiteralPath (Join-Path $taskSource 'app\BlockMook.exe') -Algorithm SHA256).Hash.Substring(0,12).ToLowerInvariant()
$taskBase=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\BlockMook\versions'))
$taskTarget=[IO.Path]::GetFullPath((Join-Path $taskBase ($taskVersion+'-'+$taskBuildHash)))
if(-not $taskTarget.StartsWith($taskBase+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Install target outside BlockMook versions'}
$taskEntries=@()
foreach($taskLine in Get-Content -LiteralPath $taskManifest){
 if($taskLine -notmatch '^([A-Fa-f0-9]{64})  (.+)$'){throw 'Invalid checksum manifest'}
 $taskHash=$Matches[1];$taskRelative=$Matches[2]
 if($taskRelative -match '(^[/\\]|:|(^|[/\\])\.\.([/\\]|$))'){throw 'Unsafe manifest path'}
 $taskPath=[IO.Path]::GetFullPath((Join-Path $taskSource $taskRelative))
 if(-not $taskPath.StartsWith($taskSource+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Source path outside release'}
 if((Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash -ne $taskHash){throw ('Release checksum mismatch: '+$taskRelative)}
 $taskEntries+=@{Relative=$taskRelative;Hash=$taskHash;Source=$taskPath}
}
if(-not ($taskEntries.Relative -contains 'app/BlockMook.exe')){throw 'Executable missing from checksum manifest'}
# Reuse an identical version; never overwrite a different build or remove old versions.
foreach($taskEntry in $taskEntries){
 $taskDestination=Join-Path $taskTarget $taskEntry.Relative
 if(Test-Path -LiteralPath $taskDestination){if((Get-FileHash -LiteralPath $taskDestination -Algorithm SHA256).Hash -ne $taskEntry.Hash){throw ('Different files already exist in '+$taskTarget)}}
}
foreach($taskEntry in $taskEntries){
 $taskDestination=Join-Path $taskTarget $taskEntry.Relative
 if(-not (Test-Path -LiteralPath $taskDestination)){New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($taskDestination)) -Force | Out-Null;Copy-Item -LiteralPath $taskEntry.Source -Destination $taskDestination}
 if((Get-FileHash -LiteralPath $taskDestination -Algorithm SHA256).Hash -ne $taskEntry.Hash){throw 'Installed file verification failed'}
}
Copy-Item -LiteralPath $taskManifest -Destination (Join-Path $taskTarget 'SHA256SUMS.txt')
$taskExecutable=Join-Path $taskTarget 'app\BlockMook.exe'
$taskShell=New-Object -ComObject WScript.Shell
try{
 $taskLinkPath=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'BlockMook.lnk'
 $taskLink=$taskShell.CreateShortcut($taskLinkPath)
 try{
  if((Test-Path -LiteralPath $taskLinkPath) -and -not ([string]$taskLink.TargetPath).StartsWith($taskBase+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'A different BlockMook shortcut exists. Rename it before installing.'}
  $taskLink.TargetPath=$taskExecutable;$taskLink.WorkingDirectory=[IO.Path]::GetDirectoryName($taskExecutable);$taskLink.IconLocation=$taskExecutable+',0';$taskLink.Description='BlockMook';$taskLink.Save()
 }finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskLink)}
}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskShell)}
# Preserve an existing opt-in startup entry only for a prior installed BlockMook version.
$taskRunKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true)
if($taskRunKey){try{
 $taskOld=$taskRunKey.GetValue('BlockMook')
 if($taskOld -is [string] -and $taskOld.StartsWith('"'+$taskBase+'\',[StringComparison]::OrdinalIgnoreCase)){
  $taskRunKey.SetValue('BlockMook',('"'+$taskExecutable+'" --startup'),[Microsoft.Win32.RegistryValueKind]::String)
 }
}finally{$taskRunKey.Dispose()}}
Write-Output ('Installed: '+$taskTarget)
Write-Output ('Desktop shortcut: '+$taskLinkPath)
if(-not $NoLaunch){Start-Process -FilePath $taskExecutable -WindowStyle Normal}
