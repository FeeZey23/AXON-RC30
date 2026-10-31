[CmdletBinding()]param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
$sha='98d0cfa794721665d7973dd728c78aff7bd836d58f10f6d7a03568eccb0a22e0'
$tools=Join-Path (Get-ProjectRoot) '.artifacts\tools';New-Item -ItemType Directory -Path $tools,$Destination -Force|Out-Null
$zip=Join-Path $tools 'gh_2.103.3_windows_amd64.zip'
if(-not(Test-Path $zip)){Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/cli/cli/releases/download/v2.103.3/gh_2.103.3_windows_amd64.zip' -OutFile $zip}
if((Get-FileHash $zip).Hash.ToLowerInvariant() -ne $sha){throw 'Official GitHub CLI archive checksum mismatch'}
$stage=Join-Path $tools 'gh-2.103.3';Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force
$exe=Get-ChildItem $stage -Filter gh.exe -Recurse|Select-Object -First 1;if(-not $exe){throw 'CLI missing'}
Copy-Item $exe.FullName (Join-Path $Destination 'gh.exe') -Force
$license=Get-ChildItem $stage -Filter LICENSE* -Recurse|Select-Object -First 1;if($license){Copy-Item $license.FullName (Join-Path $Destination 'LICENSE.txt') -Force}
