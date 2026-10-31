[CmdletBinding()]param([Parameter(Mandatory=$true)][string]$Msi,[switch]$Install,[switch]$CloseExistingRc30)
. (Join-Path $PSScriptRoot 'Repository-Common.ps1')
if($env:OS -ne 'Windows_NT'){throw 'Windows only.'}
$msiPath=[IO.Path]::GetFullPath($Msi);$manifest=Get-Content (Join-Path (Split-Path $msiPath -Parent) 'installer-manifest.json') -Raw | ConvertFrom-Json
if($manifest.File -ne [IO.Path]::GetFileName($msiPath) -or (Get-FileHash $msiPath).Hash.ToLowerInvariant() -ne $manifest.Sha256){throw 'MSI hash/manifest mismatch'}
if(-not $Install){Write-Output 'Dry run only. MSI hash is valid. Use -Install for an explicit Windows installation. Existing app state is outside install directory.';return}
$testFile=Join-Path (Get-ProjectRoot) '.artifacts\checks\installer-test.json'
if(-not(Test-Path $testFile)){throw 'A matching installer install/UI/uninstall verification is required before production installation.'}
$test=Get-Content $testFile -Raw|ConvertFrom-Json
if(-not $test.Passed -or $test.MsiSha256 -ne $manifest.Sha256 -or $test.SourceDigest -ne $manifest.SourceDigest){throw 'Installer test does not match this MSI'}
$running=@(Get-Process -Name 'AXON.RC30' -ErrorAction SilentlyContinue)
if($running.Count -and -not $CloseExistingRc30){throw 'Close RC30 first, or explicitly pass -CloseExistingRc30. rc26/rc27 are not stopped.'}
foreach($p in $running){[void]$p.CloseMainWindow();if(-not $p.WaitForExit(15000)){throw 'RC30 did not close gracefully. No process was killed.'}}
$others=@(Get-Process -Name 'ArenaDesktopCompanion' -ErrorAction SilentlyContinue | ForEach-Object {$_.Id})
$data=Join-Path $env:LOCALAPPDATA 'AXON\RC30';$config=Join-Path $data 'settings.json'
if(Test-Path $config){$backups=Join-Path $data 'StateBackups';New-Item -ItemType Directory -Path $backups -Force | Out-Null;Copy-Item -LiteralPath $config -Destination (Join-Path $backups ('before-msi-'+[Guid]::NewGuid().ToString('N')+'.json'))}
$root=Get-ProjectRoot;$checks=Join-Path $root '.artifacts\checks';New-Item -ItemType Directory -Path $checks -Force | Out-Null
$log=Join-Path $checks 'msi-install.log';$p=Start-Process msiexec.exe -ArgumentList ('/i "'+$msiPath+'" /passive /norestart /L*v "'+$log+'"') -Wait -PassThru
if($p.ExitCode -notin @(0,3010)){throw ('Windows Installer failed: '+$p.ExitCode+'; '+$log)}
$exe=Join-Path $env:LOCALAPPDATA 'Programs\ISAS\AXON-RC30\AXON.RC30.exe'
if(-not(Test-Path $exe)){throw 'Expected installed executable missing'}
$signature=Get-AuthenticodeSignature $msiPath
$runtimeManifest=@(Get-ChildItem (Split-Path $exe -Parent) -Recurse -File | ForEach-Object {@{Path=$_.FullName.Substring((Split-Path $exe -Parent).Length+1);Sha256=(Get-FileHash $_.FullName).Hash.ToLowerInvariant()}})
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent)
$app=$null;for($i=0;$i -lt 60;$i++){$app=Get-Process -Name 'AXON.RC30' -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe -and $_.MainWindowHandle -ne [IntPtr]::Zero} | Select-Object -First 1;if($app){break};Start-Sleep -Milliseconds 250}
if(-not $app){throw 'MSI installed, but application window was not confirmed. Check WebView2 runtime and crash log.'}
foreach($id in $others){if(-not(Get-Process -Id $id -ErrorAction SilentlyContinue)){throw 'An older Companion process disappeared'}}
@{Installed=$true;WindowConfirmed=$true;ProcessId=$app.Id;MsiExitCode=$p.ExitCode;AuthenticodeStatus=[string]$signature.Status;OlderCompanionPidsPreserved=$others;UserDataNotDeleted=$true;At=(Get-Date).ToString('o');RuntimeFiles=$runtimeManifest.Count} | ConvertTo-Json | Set-Content (Join-Path $checks 'installation-verification.json') -Encoding UTF8
$runtimeManifest | ConvertTo-Json | Set-Content (Join-Path $checks 'installed-runtime-manifest.json') -Encoding UTF8
Write-Output 'Windows installation and application window confirmed. Functional live-relay acceptance remains separate.'
