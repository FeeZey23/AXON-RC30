$ErrorActionPreference='Stop'
function Get-ProjectRoot { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')) }
function Get-RepositoryFiles {
 param([string]$Root)
 Get-ChildItem -LiteralPath $Root -Recurse -File -Force | Where-Object {
  $relative=$_.FullName.Substring($Root.Length+1).Replace('\','/')
  $relative -notmatch '(^|/)(\.git|\.vs|\.idea|\.artifacts|\.tools|releases|bin|obj|TestResults|node_modules|__pycache__)(/|$)'
 }
}
function Get-RepositoryDigest {
 param([string]$Root)
 $rows=@(Get-RepositoryFiles $Root | Sort-Object FullName | ForEach-Object {$_.FullName.Substring($Root.Length+1).Replace('\','/')+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()})
 $sha=[Security.Cryptography.SHA256]::Create()
 try { -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows -join "`n"))) | ForEach-Object {$_.ToString('x2')}) } finally {$sha.Dispose()}
}
function Invoke-LoggedTool {
 param([string]$Program,[string[]]$Arguments,[string]$Log)
 $old=$ErrorActionPreference;$ErrorActionPreference='Continue'
 try{& $Program @Arguments *> $Log;$exit=$LASTEXITCODE}finally{$ErrorActionPreference=$old}
 if($exit -ne 0){Get-Content -LiteralPath $Log -Tail 45;throw ($Program+' failed with exit '+$exit+'; see '+$Log)}
}
