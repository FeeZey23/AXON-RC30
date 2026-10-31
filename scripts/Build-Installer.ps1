[CmdletBinding()]param([string]$ProductVersion='0.2.304')
. (Join-Path $PSScriptRoot 'Repository-Common.ps1')
if($env:OS -ne 'Windows_NT'){throw 'MSI build requires Windows.'}
$root=Get-ProjectRoot;$checks=Join-Path $root '.artifacts\checks';$validationPath=Join-Path $checks 'validation.json'
if(-not(Test-Path $validationPath)){throw 'Run scripts/Build.ps1 including UI validation first.'}
$v=Get-Content $validationPath -Raw | ConvertFrom-Json
if(-not $v.ReleaseGate -or $v.SourceDigest -ne (Get-RepositoryDigest $root)){throw 'Full matching Windows validation is missing or source changed.'}
$publish=Join-Path $root '.artifacts\runtime-win-x64';$out=Join-Path $root 'releases';New-Item -ItemType Directory -Path $publish,$out -Force | Out-Null
Push-Location $root
try{
 Invoke-LoggedTool dotnet @('restore','src/ArenaDesktopCompanion','-r','win-x64') (Join-Path $checks 'restore-self-contained.log')
 Invoke-LoggedTool dotnet @('clean','src/ArenaDesktopCompanion','-c','Release','-r','win-x64','--nologo') (Join-Path $checks 'clean-self-contained.log')
 Invoke-LoggedTool dotnet @('publish','src/ArenaDesktopCompanion','-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=false','-p:TreatWarningsAsErrors=true','-o',$publish) (Join-Path $checks 'publish-self-contained.log')
 $priorRoll=$env:DOTNET_ROLL_FORWARD;$env:DOTNET_ROLL_FORWARD='Major'
 try{Invoke-LoggedTool dotnet @('tool','restore') (Join-Path $checks 'wix-restore.log')}finally{$env:DOTNET_ROLL_FORWARD=$priorRoll}
 $ns='http://wixtoolset.org/schemas/v4/wxs';$xml=New-Object Xml.XmlDocument;$wix=$xml.CreateElement('Wix',$ns);[void]$xml.AppendChild($wix)
 function Element($name,$parent,$attributes){$e=$xml.CreateElement($name,$ns);foreach($key in $attributes.Keys){$e.SetAttribute($key,[string]$attributes[$key])};[void]$parent.AppendChild($e);return $e}
 $dirsFragment=Element 'Fragment' $wix @{};$dirRef=Element 'DirectoryRef' $dirsFragment @{Id='INSTALLFOLDER'};$dirNodes=@{''=$dirRef};$dirIds=@{''='INSTALLFOLDER'}
 $fragment=Element 'Fragment' $wix @{};$group=Element 'ComponentGroup' $fragment @{Id='RuntimeFiles'}
 foreach($f in (Get-ChildItem $publish -File -Recurse | Where-Object {$_.Extension -ne '.pdb'} | Sort-Object FullName)){
  $rel=$f.FullName.Substring($publish.Length+1);$parent=[IO.Path]::GetDirectoryName($rel);if($null -eq $parent){$parent=''}
  $walk='';foreach($part in ($parent -split '\\' | Where-Object {$_})){
   $next=if($walk){$walk+'\'+$part}else{$part}
   if(-not $dirIds.ContainsKey($next)){$did='D'+($dirIds.Count);$dirIds[$next]=$did;$dirNodes[$next]=Element 'Directory' $dirNodes[$walk] @{Id=$did;Name=$part}}
   $walk=$next
  }
  $hashProvider=[Security.Cryptography.SHA256]::Create();try{$fileKey=-join ($hashProvider.ComputeHash([Text.Encoding]::UTF8.GetBytes($rel.ToLowerInvariant())) | ForEach-Object {$_.ToString('x2')})}finally{$hashProvider.Dispose()};$fid='F'+$fileKey.Substring(0,32);$componentGuid=('{'+$fileKey.Substring(0,8)+'-'+$fileKey.Substring(8,4)+'-5'+$fileKey.Substring(13,3)+'-a'+$fileKey.Substring(17,3)+'-'+$fileKey.Substring(20,12)+'}').ToUpperInvariant();$component=Element 'Component' $group @{Id=('C'+$fid);Directory=$dirIds[$parent];Guid=$componentGuid}
  [void](Element 'File' $component @{Id=$fid;Source=$f.FullName})
  [void](Element 'RegistryValue' $component @{Root='HKCU';Key='Software\ISAS\AXON-RC30\Files';Name=$fid;Type='integer';Value='1';KeyPath='yes'})
  if($parent){[void](Element 'RemoveFolder' $component @{Id=('R'+$fid);On='uninstall'})}
 }
 $generated=Join-Path $checks 'RuntimeFiles.wxs';$xml.Save($generated)
 $msi=Join-Path $out ('ISAS-AXON-RC30-'+$ProductVersion+'-win-x64.msi')
 $priorRoll=$env:DOTNET_ROLL_FORWARD;$env:DOTNET_ROLL_FORWARD='Major'
 try{Invoke-LoggedTool dotnet @('tool','run','wix','build','installer/Product.wxs',$generated,'-arch','x64','-d',('ProductVersion='+$ProductVersion),'-d',('IconPath='+ (Join-Path $root 'src\ArenaDesktopCompanion\Assets\app.ico')),'-o',$msi) (Join-Path $checks 'msi-build.log')}finally{$env:DOTNET_ROLL_FORWARD=$priorRoll}
 $hash=(Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant();[IO.File]::WriteAllText(($msi+'.sha256'),($hash+'  '+[IO.Path]::GetFileName($msi)+"`n"))
 @{File=[IO.Path]::GetFileName($msi);Sha256=$hash;SourceDigest=$v.SourceDigest;InstallerBuilt=$true;WindowsInstallVerified=$false;AuthenticodeSigned=$false;Version=$ProductVersion} | ConvertTo-Json | Set-Content (Join-Path $out 'installer-manifest.json') -Encoding UTF8
 Write-Output ('MSI built, not yet installation-verified: '+$msi)
}finally{Pop-Location}
