[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('Windows', 'Android', 'Both')]
    [string]$Target = 'Both',

    [switch]$ServerOnly,

    [string]$AndroidDevice
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$solution = Join-Path $repoRoot 'MemoAna.slnx'
$serverProject = Join-Path $repoRoot 'src/MemoAna.Server/MemoAna.Server.csproj'
$mauiProject = Join-Path $repoRoot 'src/MemoAna/MemoAna.csproj'

function Write-Info([string]$Message) {
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-ErrorMessage([string]$Message) {
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

if (-not (Test-Path $solution)) {
    throw "Solution not found: $solution"
}

if ($ServerOnly) {
    Write-Info "Starting MemoAna.Server in $Configuration configuration."
    dotnet run --project $serverProject --configuration $Configuration
    exit $LASTEXITCODE
}

if ($Target -in @('Windows', 'Both')) {
    if (-not $IsWindows) {
        Write-ErrorMessage 'The Windows MAUI target can only be started on Windows.'
        exit 1
    }
}

if ($Target -in @('Android', 'Both')) {
    $adb = Get-Command adb -ErrorAction SilentlyContinue
    if (-not $adb) {
        Write-ErrorMessage 'ADB was not found in PATH. Connect an Android device and make adb available.'
        exit 1
    }

    $devices = @(adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S+\s+device\s*$' })
    if ($devices.Count -eq 0) {
        Write-ErrorMessage 'No Android device in the ready state was found.'
        exit 1
    }

    if ($AndroidDevice) {
        $matching = $devices | Where-Object { $_ -match "^$([regex]::Escape($AndroidDevice))\s+device\s*$" }
        if (-not $matching) {
            Write-ErrorMessage "Android device '$AndroidDevice' is not connected and ready."
            exit 1
        }
    }
}

$jobs = @()

Write-Info "Starting MemoAna.Server in $Configuration configuration."
$jobs += Start-Job -ScriptBlock {
    param($Project, $Config)
    Set-Location (Split-Path -Parent (Split-Path -Parent $Project))
    dotnet run --project $Project --configuration $Config
} -ArgumentList $serverProject, $Configuration

if ($Target -in @('Windows', 'Both')) {
    Write-Info "Starting MemoAna Windows target in $Configuration configuration."
    $jobs += Start-Job -ScriptBlock {
        param($Project, $Config)
        Set-Location (Split-Path -Parent (Split-Path -Parent $Project))
        dotnet build $Project --configuration $Config -f net10.0-windows10.0.19041.0 -t:Run
    } -ArgumentList $mauiProject, $Configuration
}

if ($Target -in @('Android', 'Both')) {
    Write-Info "Starting MemoAna Android target in $Configuration configuration."
    $jobs += Start-Job -ScriptBlock {
        param($Project, $Config, $Device)
        Set-Location (Split-Path -Parent (Split-Path -Parent $Project))
        if ($Device) {
            $env:ANDROID_DEVICE_ID = $Device
        }
        dotnet build $Project --configuration $Config -f net10.0-android -t:Run
    } -ArgumentList $mauiProject, $Configuration, $AndroidDevice
}

try {
    while ($jobs | Where-Object State -in @('Running', 'NotStarted')) {
        foreach ($job in $jobs) {
            Receive-Job $job -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 500
    }

    foreach ($job in $jobs) {
        Receive-Job $job -ErrorAction SilentlyContinue
        if ($job.State -eq 'Failed') {
            Write-ErrorMessage "A launch job failed: $($job.ChildJobs[0].JobStateInfo.Reason.Message)"
        }
    }
}
finally {
    $jobs | Stop-Job -ErrorAction SilentlyContinue
    $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
}
