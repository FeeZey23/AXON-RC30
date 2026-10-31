[CmdletBinding()]param()
. (Join-Path $PSScriptRoot 'Repository-Common.ps1')
$root=Get-ProjectRoot;$tools=Join-Path $root '.artifacts\tools\gitleaks-8.30.1';$checks=Join-Path $root '.artifacts\checks';New-Item -ItemType Directory -Path $tools,$checks -Force|Out-Null
$zip=Join-Path $tools 'gitleaks.zip';if(-not(Test-Path $zip)){Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_windows_x64.zip' -OutFile $zip}
if((Get-FileHash $zip).Hash.ToLowerInvariant() -ne 'd29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e'){throw 'Official Gitleaks checksum mismatch'}
$exe=Join-Path $tools 'gitleaks.exe';Expand-Archive -LiteralPath $zip -DestinationPath $tools -Force
$stage=Join-Path (Join-Path $root '.artifacts') ('secrets-source-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $stage|Out-Null
foreach($f in (Get-RepositoryFiles $root)){$relative=$f.FullName.Substring($root.Length+1);$destination=Join-Path $stage $relative;New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force|Out-Null;Copy-Item -LiteralPath $f.FullName -Destination $destination}
# Scan actual source, not third-party tool samples/generated outputs. History is scanned separately in full.
try{Invoke-LoggedTool $exe @('dir',$stage,'--config',(Join-Path $root '.gitleaks.toml'),'--no-banner','--redact','--report-format','json','--report-path',(Join-Path $checks 'secrets-working-tree.json')) (Join-Path $checks 'secrets-working-tree.log')
}finally{Remove-Item -LiteralPath $stage -Recurse -Force}
if(Test-Path (Join-Path $root '.git')){Invoke-LoggedTool $exe @('git',$root,'--config',(Join-Path $root '.gitleaks.toml'),'--log-opts=--all','--no-banner','--redact','--report-format','json','--report-path',(Join-Path $checks 'secrets-history.json')) (Join-Path $checks 'secrets-history.log')}
Write-Output 'Gitleaks source/history checks passed (redacted reports stay private).'
