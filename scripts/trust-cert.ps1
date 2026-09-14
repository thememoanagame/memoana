param([Parameter(Mandatory=$true)]
[string]$P)

$repositoryRoot = Resolve-Path "${PSScriptRoot}\.."
$path = Join-Path $repositoryRoot ".certs"

New-Item -path $path -ItemType Directory -Force | Out-Null
Push-Location $path
dotnet dev-certs https -ep ./aspnetapp.pfx -p $P
dotnet dev-certs https --trust
Pop-Location