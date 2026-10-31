[CmdletBinding()]param([Parameter(Mandatory=$true)][string]$Msi)
. (Join-Path $PSScriptRoot 'Repository-Common.ps1')
if($env:OS -ne 'Windows_NT'){throw 'Windows only'}
$root=Get-ProjectRoot;$msiPath=[IO.Path]::GetFullPath($Msi);$manifest=Get-Content (Join-Path (Split-Path $msiPath -Parent) 'installer-manifest.json') -Raw|ConvertFrom-Json
if((Get-FileHash $msiPath).Hash.ToLowerInvariant() -ne $manifest.Sha256){throw 'Installer hash mismatch'}
$installed=@(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue|Get-ItemProperty -ErrorAction SilentlyContinue|Where-Object {$_.DisplayName -eq 'ISAS - AXON-RC30'})
if($installed.Count){throw 'Existing ISAS MSI installation found; do not replace it for testing'}
$checks=Join-Path $root '.artifacts\checks';$testRoot=Join-Path $root ('.artifacts\msitest-'+[Guid]::NewGuid().ToString('N'));$app=Join-Path $testRoot 'app';New-Item -ItemType Directory -Path $checks,$testRoot -Force|Out-Null
$processes=@(Get-Process -Name 'AXON.RC30','ArenaDesktopCompanion' -ErrorAction SilentlyContinue|Select-Object Id,Path)
$privateRoot=Join-Path $env:LOCALAPPDATA 'AXON\RC30';$privateSettings=@{};if(Test-Path $privateRoot){Get-ChildItem $privateRoot -Filter 'settings*.json*' -File|ForEach-Object {$privateSettings[$_.FullName]=(Get-FileHash $_.FullName).Hash}}
$settingsUnchanged=$true;$registered=$false;$uninstalled=$false;$passed=$false;$errorText=$null;$uiCount=0;$skips=0
try{
 $log=Join-Path $checks 'msi-test-install.log'
 $p=Start-Process msiexec.exe -ArgumentList ('/i "'+$msiPath+'" /qn /norestart MSIRESTARTMANAGERCONTROL=Disable INSTALLFOLDER="'+$app+'" /L*v "'+$log+'"') -Wait -PassThru
 if($p.ExitCode -ne 0){throw ('Test MSI install failed: '+$p.ExitCode)};$registered=$true
 $exe=Join-Path $app 'AXON.RC30.exe';if(-not(Test-Path $exe)){throw 'Test executable not installed'}
 $env:AXON_RC30_SMOKE='1';$env:AXON_RC30_SMOKE_OUTPUT=Join-Path $checks 'installer-ui.json';Remove-Item $env:AXON_RC30_SMOKE_OUTPUT -ErrorAction SilentlyContinue
 try{
  $uiProcess=Start-Process $exe -WorkingDirectory $app -PassThru
  if(-not $uiProcess.WaitForExit(180000)){$uiProcess.Kill();throw 'Installed UI smoke timed out'}
  if($uiProcess.ExitCode -ne 0){throw ('Installed UI process failed: '+$uiProcess.ExitCode)}
  $ui=Get-Content $env:AXON_RC30_SMOKE_OUTPUT -Raw|ConvertFrom-Json
  $skips=@($ui.Checks|Where-Object {$_ -like 'SKIP:*'}).Count;$uiCount=@($ui.Checks).Count
  if(-not $ui.Passed -or $skips){throw ('Installed UI test failed or native gesture skipped: '+$ui.Error)}
 }finally{Remove-Item Env:AXON_RC30_SMOKE -ErrorAction SilentlyContinue;Remove-Item Env:AXON_RC30_SMOKE_OUTPUT -ErrorAction SilentlyContinue}
 $passed=$true
}catch{$errorText=$_.Exception.ToString()}
finally{
 if($registered){$log=Join-Path $checks 'msi-test-uninstall.log';$p=Start-Process msiexec.exe -ArgumentList ('/x "'+$msiPath+'" /qn /norestart MSIRESTARTMANAGERCONTROL=Disable /L*v "'+$log+'"') -Wait -PassThru;$uninstalled=($p.ExitCode -eq 0);if(-not $uninstalled){$passed=$false;$errorText+='; test MSI uninstall failed: '+$p.ExitCode}}
}
if(Test-Path (Join-Path $app 'AXON.RC30.exe')){$uninstalled=$false;$passed=$false;$errorText+='; executable remains after uninstall'}
foreach($item in $privateSettings.Keys){if(-not(Test-Path $item) -or (Get-FileHash $item).Hash -ne $privateSettings[$item]){$settingsUnchanged=$false;$passed=$false;$errorText+='; productive settings changed'}}
foreach($item in $processes){if(-not(Get-Process -Id $item.Id -ErrorAction SilentlyContinue)){$passed=$false;$errorText+='; protected production process disappeared'}}
$report=@{Passed=$passed;InstallVerified=$registered;UninstallVerified=$uninstalled;InstalledUiChecks=$uiCount;NativeSkips=$skips;TestDataScope='RC30.Smoke';SeparateInstallTarget=$true;OperatingSystemVmIsolation=$false;ProductiveSettingsUnchanged=$settingsUnchanged;ProductiveProcessIds=@($processes|ForEach-Object {$_.Id});Error=$errorText;MsiSha256=$manifest.Sha256;SourceDigest=$manifest.SourceDigest;At=(Get-Date).ToString('o')}
$report|ConvertTo-Json -Depth 5|Set-Content (Join-Path $checks 'installer-test.json') -Encoding UTF8
if(-not $passed){throw $errorText};$report|ConvertTo-Json -Depth 5
