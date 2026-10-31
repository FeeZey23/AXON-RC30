[CmdletBinding()]param()
. (Join-Path $PSScriptRoot 'Repository-Common.ps1')
$root=Get-ProjectRoot;$checks=Join-Path $root '.artifacts\checks';$out=Join-Path $root 'releases\Smoke';New-Item -ItemType Directory -Path $out -Force|Out-Null
$prod=Get-Content (Join-Path $root 'releases\installer-manifest.json') -Raw|ConvertFrom-Json
$main=(Get-Content (Join-Path $root 'installer\Product.wxs') -Raw).Replace('8B849114-5D61-4D47-8133-C1400D6D413A','F5289A8F-D4D9-45DA-8063-78CF208A30E0').Replace('ISAS - AXON-RC30','ISAS - AXON-RC30 Smoke').Replace('Software\ISAS\AXON-RC30','Software\ISAS\AXON-RC30.Smoke').Replace('Name="AXON-RC30"','Name="AXON-RC30 Smoke"').Replace('D9F31660-87F6-4C58-A2AA-E44550B5C697','BF047537-CB14-45BA-9311-64C087B0FA06')
$mainPath=Join-Path $checks 'SmokeProduct.wxs';[IO.File]::WriteAllText($mainPath,$main)
$runtime=Get-Content (Join-Path $checks 'RuntimeFiles.wxs') -Raw
$runtime=$runtime.Replace('Software\ISAS\AXON-RC30','Software\ISAS\AXON-RC30.Smoke')
$runtime=[regex]::Replace($runtime,'Guid="[^"]+"',{param($m)$sha=[Security.Cryptography.SHA256]::Create();try{$hash=-join($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes('smoke:'+ $m.Value))|ForEach-Object {$_.ToString('x2')})}finally{$sha.Dispose()};'Guid="{'+$hash.Substring(0,8)+'-'+$hash.Substring(8,4)+'-'+$hash.Substring(12,4)+'-'+$hash.Substring(16,4)+'-'+$hash.Substring(20,12)+'}"'})
$runtimePath=Join-Path $checks 'SmokeRuntimeFiles.wxs';[IO.File]::WriteAllText($runtimePath,$runtime)
$msi=Join-Path $out ('ISAS-AXON-RC30-'+$prod.Version+'-win-x64.msi');$prior=$env:DOTNET_ROLL_FORWARD;$env:DOTNET_ROLL_FORWARD='Major'
Push-Location $root
try{Invoke-LoggedTool dotnet @('tool','run','wix','build',$mainPath,$runtimePath,'-arch','x64','-d',('ProductVersion='+$prod.Version),'-d',('IconPath='+ (Join-Path $root 'src\ArenaDesktopCompanion\Assets\app.ico')),'-o',$msi) (Join-Path $checks 'smoke-msi-build.log')}finally{$env:DOTNET_ROLL_FORWARD=$prior;Pop-Location}
$hash=(Get-FileHash $msi).Hash.ToLowerInvariant();@{File=[IO.Path]::GetFileName($msi);Sha256=$hash;Version=$prod.Version;SourceDigest=$prod.SourceDigest;IsolatedIdentity=$true;ProductionMsiSha256=$prod.Sha256}|ConvertTo-Json|Set-Content (Join-Path $out 'installer-manifest.json') -Encoding UTF8
