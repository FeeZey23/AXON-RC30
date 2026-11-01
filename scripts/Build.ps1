[CmdletBinding()]param([switch]$SkipUiSmoke)
. (Join-Path $PSScriptRoot 'Repository-Common.ps1')
if($env:OS -ne 'Windows_NT'){throw 'This WPF validation must run on Windows.'}
$root=Get-ProjectRoot;$checks=Join-Path $root '.artifacts\checks';$runtime=Join-Path $root '.artifacts\runtime-framework-dependent'
New-Item -ItemType Directory -Path $checks,$runtime -Force | Out-Null
& (Join-Path $PSScriptRoot 'Test-Repository.ps1')
& (Join-Path $root 'scripts\Test-Secrets.ps1')
Invoke-LoggedTool node @((Join-Path $root 'scripts\Check-Documentation.mjs')) (Join-Path $checks 'documentation.log')
$before=Get-RepositoryDigest $root
Push-Location $root
try{
 Invoke-LoggedTool dotnet @('restore','ArenaDesktopCompanion.sln') (Join-Path $checks 'restore.log')
 foreach($c in @('Debug','Release')){Invoke-LoggedTool dotnet @('build','ArenaDesktopCompanion.sln','-c',$c,'--no-restore','--no-incremental','-p:TreatWarningsAsErrors=true','--nologo') (Join-Path $checks ('build-'+$c+'.log'))}
 Invoke-LoggedTool dotnet @('test','tests/ArenaDesktopCompanion.Core.Tests','-c','Release','--no-build','--logger','trx;LogFileName=core.trx','--results-directory',$checks) (Join-Path $checks 'core.log')
 [xml]$trx=Get-Content (Join-Path $checks 'core.trx') -Raw;$counts=$trx.TestRun.ResultSummary.Counters
 if([int]$counts.failed -ne 0 -or [int]$counts.passed -lt 1 -or [int]$counts.notExecuted -ne 0){throw 'Core validation incomplete'}
 Invoke-LoggedTool dotnet @('run','--project','tests/AXON.RC30.Integration','-c','Release','--no-build') (Join-Path $checks 'connector.log')
 Invoke-LoggedTool node @('--test','tests/AXON.RC30.Integration/relay-http.test.mjs') (Join-Path $checks 'relay.log')
 Invoke-LoggedTool dotnet @('restore','src/ArenaDesktopCompanion','-r','win-x64') (Join-Path $checks 'restore-framework-dependent.log')
 Invoke-LoggedTool dotnet @('clean','src/ArenaDesktopCompanion','-c','Release','-r','win-x64','--nologo') (Join-Path $checks 'clean-framework-dependent.log')
 Invoke-LoggedTool dotnet @('publish','src/ArenaDesktopCompanion','-c','Release','-r','win-x64','--self-contained','false','-p:TreatWarningsAsErrors=true','-o',$runtime) (Join-Path $checks 'publish-framework-dependent.log')
 & (Join-Path $root 'scripts\Prepare-GitHub.ps1') -Destination (Join-Path $runtime 'Tools\GitHub')
 $uiPassed=$false;$uiCount=0;$skips=0
 if(-not $SkipUiSmoke){
 & (Join-Path $PSScriptRoot 'Wait-InputIdle.ps1')
  $env:AXON_RC30_SMOKE='1';$env:AXON_RC30_SMOKE_OUTPUT=Join-Path $checks 'ui.json'
  Remove-Item -LiteralPath $env:AXON_RC30_SMOKE_OUTPUT -ErrorAction SilentlyContinue
  try{
   $p=Start-Process (Join-Path $runtime 'AXON.RC30.exe') -WorkingDirectory $runtime -PassThru
   if(-not $p.WaitForExit(180000)){$p.Kill();throw 'Dedicated UI smoke process timed out'}
   if($p.ExitCode -ne 0){throw ('UI test process failed: '+$p.ExitCode)}
   $ui=Get-Content $env:AXON_RC30_SMOKE_OUTPUT -Raw -Encoding UTF8 | ConvertFrom-Json
   if(-not $ui.Passed){throw ('UI validation failed: '+$ui.Error)}
   $skips=@($ui.Checks | Where-Object {$_ -like 'SKIP:*'}).Count
   $uiCount=@($ui.Checks | Where-Object {$_ -notlike 'SKIP:*' -and $_ -notlike 'Hardware-*'}).Count;$uiPassed=$true
  }finally{Remove-Item Env:AXON_RC30_SMOKE -ErrorAction SilentlyContinue;Remove-Item Env:AXON_RC30_SMOKE_OUTPUT -ErrorAction SilentlyContinue}
 }
 $after=Get-RepositoryDigest $root;if($before -ne $after){throw 'Source tree changed during validation'}
 $report=@{Passed=$true;SourceDigest=$after;At=(Get-Date).ToString('o');CorePassed=[int]$counts.passed;CoreFailed=[int]$counts.failed;ConnectorPassed=@(Select-String (Join-Path $checks 'connector.log') -Pattern '^PASS ').Count;RelayPassed=$true;UiPassed=$uiPassed;UiCount=$uiCount;NativeGestureSkipped=$skips;ReleaseGate=($uiPassed -and $skips -eq 0);InstallerVerified=$false}
 $report | ConvertTo-Json | Set-Content (Join-Path $checks 'validation.json') -Encoding UTF8
 $report | ConvertTo-Json
}finally{Pop-Location}
