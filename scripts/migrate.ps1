[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, HelpMessage="Migration name")]
    [Alias("n")][string]$Name,
    [Parameter(Mandatory=$false, HelpMessage="Is this an app migration?")]
    [switch][Alias("a")]$App,
    [Parameter(Mandatory=$false, HelpMessage="Verbose")]
    [switch][Alias("v")]$VBS
)
 
$RepoRoot = (Get-Item "$PSScriptRoot\..").FullName
$AppStartupProject = (Get-Item (Join-Path $RepoRoot "src/MemoAna")).FullName
$ServerStartupProject = (Get-Item (Join-Path $RepoRoot "src/MemoAna.Server")).FullName
$AppProject = (Get-Item (Join-Path $RepoRoot "src/MemoAna.Game")).FullName
$AppMigrationsFolder = "Common/Persistence/Migrations"
$ServerMigrationsFolder = "Infrastructure/Persistence/Migrations"
if ($App) { $StartupProject = $AppStartupProject } else { $StartupProject = $ServerStartupProject }

$ProjectRoot = (Get-Item "$RepoRoot\$StartupProject").FullName

Push-Location $ProjectRoot
try {
    Write-Host "Adding migration '$Name'" -ForegroundColor Cyan
    if($App) {  
        if ($VBS) {
            Write-Host "Verbose mode enabled" -ForegroundColor Yellow
            dotnet ef migrations add $Name --startup-project $AppStartupProject --project $AppProject --framework net10.0-windows10.0.19041.0 --output-dir $AppMigrationsFolder --verbose
        }
        else {
            dotnet ef migrations add $Name --startup-project $AppStartupProject --project $AppProject --framework net10.0-windows10.0.19041.0 --output-dir $AppMigrationsFolder 
        }    
    } else {
        if ($VBS) {
            Write-Host "Verbose mode enabled" -ForegroundColor Yellow
            dotnet ef migrations add $Name --startup-project $ServerStartupProject --output-dir $ServerMigrationsFolder --verbose
        }
        else {
            dotnet ef migrations add $Name --startup-project $ServerStartupProject --output-dir $ServerMigrationsFolder 
        }    
    }
}
catch {
    Write-Error $_
}
finally {
    Pop-Location
}
