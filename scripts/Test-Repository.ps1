[CmdletBinding()]param()
. (Join-Path $PSScriptRoot 'Repository-Common.ps1')
$root=Get-ProjectRoot;$violations=New-Object 'System.Collections.Generic.List[string]'
foreach($file in (Get-RepositoryFiles $root)){
 $relative=$file.FullName.Substring($root.Length+1).Replace('\','/')
 if($relative -match '(^|/)(\.axon|\.axon-rc30|Profiles|Sessions|StateBackups|uploads|_private[^/]*)(/|$)' -or $file.Name -match '^(settings\..*|session\.json|token\.txt|verbindung\.txt|peer-binding\.json)$' -or $file.Extension -in @('.pfx','.p12','.pem','.key','.msi','.zip','.dmp')){$violations.Add($relative);continue}
 if($file.Extension -in @('.cs','.xaml','.csproj','.props','.md','.json','.ps1','.mjs','.js','.yml','.wxs')){
  $text=[IO.File]::ReadAllText($file.FullName)
  $expanded=$text.Replace('\n',"`n").Replace('\r',"`r")
  if($expanded -match '(?s)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----\s*[A-Za-z0-9+/=\s]{48,}-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----' -or $text -match '(?im)^Kennwort:\s*[a-z0-9]{4}(?:-[a-z0-9]{4}){3}\s*$'){$violations.Add($relative+': possible credential')}
 }
}
if($violations.Count){throw ('Repository privacy check failed: '+($violations -join ', '))}
Write-Output 'Repository: no excluded runtime data or obvious credential files found. Manual review of images and changes remains required.'
