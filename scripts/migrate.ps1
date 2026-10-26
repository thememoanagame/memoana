[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, HelpMessage="Migration name")]
    [Alias("n")][string]$Name,
    [Parameter(Mandatory=$false, HelpMessage="Verbose")]
    [switch][Alias("v")]$VBS
)

$src = (Get-Item "$PSScriptRoot\..\src").FullName
$RepoRoot = (Get-Item "$PSScriptRoot\..").FullName
$AppStartupProject = "src/MemoAna"
$AppProject = "src/MemoAna.Game"
$AppMigrationsFolder = "Common/Persistence/Migrations"

$ProjectRoot = (Get-Item "$RepoRoot\$StartupProject").FullName

Push-Location $ProjectRoot
try {
    Write-Host "Adding migration '$Name'" -ForegroundColor Cyan
    
    if ($VBS) {
        Write-Host "Verbose mode enabled" -ForegroundColor Yellow
        dotnet ef migrations add $Name --startup-project $AppStartupProject --project $AppProject --framework net10.0-windows10.0.19041.0 --output-dir $AppMigrationsFolder --verbose
    }
    else {
        dotnet ef migrations add $Name --startup-project $AppStartupProject --project $AppProject --framework net10.0-windows10.0.19041.0 --output-dir $AppMigrationsFolder 
    }    

}
catch {
    Write-Error $_
}
finally {
    Pop-Location
}
