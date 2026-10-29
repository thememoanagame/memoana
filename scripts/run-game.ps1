[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, HelpMessage="Target Framework")]
    [ValidateSet("w", "windows", "a", "android")]
    [Alias("f")][string]$TFM,
    [Parameter(Mandatory=$false, HelpMessage="Optional Build")]
    [switch][Alias("b")]$Build,
    [Parameter(Mandatory=$false, HelpMessage="Verbose")]
    [switch][Alias("v")]$VBS

)
$ROOT = Resolve-Path $(Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($TFM)) {
    Write-Error "The target framework is required."
    exit 1
}

$TARGET_FRAMEWORK = switch ($TFM.ToLower()) {
    "a" { "net10.0-android" }
    "android" { "net10.0-android" }
    "w" { "net10.0-windows10.0.19041.0" }
    "windows" { "net10.0-windows10.0.19041.0" }
}

$PROJECT = Join-Path $ROOT "src/MemoAna/MemoAna.csproj"

if ($Build) {
    Write-Host "Building project '$PROJECT' for target framework '$TARGET_FRAMEWORK' to run the application" -ForegroundColor Green
    dotnet run --project $PROJECT --framework $TARGET_FRAMEWORK --no-restore --verbosity normal
}
else
{
    Write-Host "Running project '$PROJECT' for target framework '$TARGET_FRAMEWORK' without building" -ForegroundColor Green
    dotnet run --project $PROJECT --framework $TARGET_FRAMEWORK --no-build --no-restore --verbosity normal
}
