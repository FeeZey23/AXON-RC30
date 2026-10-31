[CmdletBinding()]param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
$tools=Join-Path (Get-ProjectRoot) '.artifacts\tools\gitleaks-8.30.1';New-Item -ItemType Directory -Path $tools,$Destination -Force|Out-Null
$zip=Join-Path $tools 'gitleaks.zip';if(-not(Test-Path $zip)){Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_windows_x64.zip' -OutFile $zip}
if((Get-FileHash $zip).Hash.ToLowerInvariant() -ne 'd29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e'){throw 'Official Gitleaks checksum mismatch'}
$exe=Join-Path $tools 'gitleaks.exe';Expand-Archive -LiteralPath $zip -DestinationPath $tools -Force
Copy-Item $exe (Join-Path $Destination 'gitleaks.exe') -Force
Copy-Item (Join-Path $tools 'LICENSE') (Join-Path $Destination 'LICENSE.txt') -Force
