[CmdletBinding()]param([switch]$IsolatedIdentity)
. (Join-Path $PSScriptRoot 'Repository-Common.ps1')
$root=Get-ProjectRoot;$checks=Join-Path $root '.artifacts\checks';$tools=Join-Path $root '.artifacts\tools\wix-bal-4.0.6';New-Item -ItemType Directory -Path $tools -Force|Out-Null
[xml]$v=Get-Content (Join-Path $root 'Version.props') -Raw;$version=[string]$v.Project.PropertyGroup.ProductVersion
$out=Join-Path $root 'releases';if($IsolatedIdentity){$out=Join-Path $out 'Smoke'}
$manifest=Get-Content (Join-Path $out 'installer-manifest.json') -Raw|ConvertFrom-Json;$msi=Join-Path $out $manifest.File
if($manifest.Version -ne $version){throw 'MSI and bundle versions disagree'}
if((Get-FileHash $msi).Hash.ToLowerInvariant() -ne $manifest.Sha256){throw 'MSI changed'}
$nupkg=Join-Path $tools 'bal.zip'
if(-not(Test-Path $nupkg)){Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/wixtoolset.bal.wixext/4.0.6/wixtoolset.bal.wixext.4.0.6.nupkg' -OutFile $nupkg}
$expected='NRn+DldYU/tXhWq/eCoOcVoOCLME6ZUjC1UnbB3aU7ZqmQr7ReN5hey3Lu0bTyUcjCvcIfQoxjLuUazgzciLhw=='
$sha=[Security.Cryptography.SHA512]::Create();try{$actual=[Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($nupkg)))}finally{$sha.Dispose()}
if($actual -ne $expected){throw 'Official NuGet extension SHA512 mismatch'}
$unpacked=Join-Path $tools 'unpacked';if(-not(Test-Path $unpacked)){Expand-Archive -LiteralPath $nupkg -DestinationPath $unpacked}
$extension=Get-ChildItem $unpacked -Filter WixToolset.Bal.wixext.dll -Recurse|Select-Object -First 1;if(-not $extension){throw 'WiX 4 BAL extension missing'}
$source=Join-Path $root 'installer\Bundle.wxs'
if($IsolatedIdentity){$content=(Get-Content $source -Raw).Replace('0B7B6AA7-D89E-41BA-9691-1B216DD67AB7','9D872712-218A-4F48-A923-CB7F5A62ED19').Replace('Name="AXON RC30"','Name="AXON RC30 Smoke"');$source=Join-Path $checks 'SmokeBundle.wxs';[IO.File]::WriteAllText($source,$content)}
$exe=Join-Path $out 'AXON RC30 Installer.exe';$prior=$env:DOTNET_ROLL_FORWARD;$env:DOTNET_ROLL_FORWARD='Major'
Push-Location $root
try{Invoke-LoggedTool dotnet @('tool','run','wix','build',$source,'-arch','x64','-ext',$extension.FullName,'-d',('ProductVersion='+$version),'-d',('MsiPath='+$msi),'-d',('IconPath='+ (Join-Path $root 'src\ArenaDesktopCompanion\Assets\app.ico')),'-o',$exe) (Join-Path $checks ('bundle-build-'+$IsolatedIdentity+'.log'))}finally{$env:DOTNET_ROLL_FORWARD=$prior;Pop-Location}
$hash=(Get-FileHash $exe).Hash.ToLowerInvariant();[IO.File]::WriteAllText(($exe+'.sha256'),($hash+'  '+[IO.Path]::GetFileName($exe)+"`n"))
@{File=[IO.Path]::GetFileName($exe);Sha256=$hash;MsiSha256=$manifest.Sha256;Version=$version;IsolatedIdentity=[bool]$IsolatedIdentity;AuthenticodeSigned=$false}|ConvertTo-Json|Set-Content (Join-Path $out 'bootstrapper-manifest.json') -Encoding UTF8
