[CmdletBinding()]param([Parameter(Mandatory=$true)][string]$Package,[Parameter(Mandatory=$true)][string]$Sha256,[Parameter(Mandatory=$true)][int]$ParentId,[Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$Result)
$ErrorActionPreference='Stop'
try{
 if((Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Sha256.ToLowerInvariant()){throw 'Update hash mismatch'}
 $parent=Get-Process -Id $ParentId -ErrorAction SilentlyContinue
 if($parent){if($parent.Path -ne $Executable){throw 'Unexpected parent executable'};if(-not $parent.WaitForExit(30000)){throw 'RC30 did not close; no forced kill'}}
 New-Item -ItemType Directory -Path (Split-Path $Result -Parent) -Force|Out-Null
 if((Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Sha256.ToLowerInvariant()){throw 'Update changed while waiting'}
 if([IO.Path]::GetFileName($Package) -ne 'AXON RC30 Installer.exe'){throw 'Unexpected update package'}
 $log=$Result+'.bundle.log' 
 $p=Start-Process -FilePath $Package -ArgumentList ('/passive /norestart /log "'+$log+'"') -Wait -PassThru
 if($p.ExitCode -notin @(0,3010)){throw ('Windows Installer failed: '+$p.ExitCode)}
 @{Succeeded=$true;ExitCode=$p.ExitCode;At=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content $Result -Encoding UTF8
 Start-Process -FilePath $Executable -WorkingDirectory (Split-Path $Executable -Parent)
}catch{New-Item -ItemType Directory -Path (Split-Path $Result -Parent) -Force|Out-Null;@{Succeeded=$false;Error=$_.Exception.Message;At=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content $Result -Encoding UTF8;exit 1}
