[CmdletBinding()]
param(
    [Alias('c')]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [Alias('t')]
    [ValidateSet('Windows', 'Android', 'Both')]
    [string]$Target = 'Both',

    [switch]$ServerOnly,

    [Alias('d')]
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

if ($Target -in @('Windows', 'Both') -and -not $IsWindows) {
    Write-ErrorMessage 'The Windows MAUI target can only be started on Windows.'
    exit 1
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

$processes = @()

Write-Info "Starting MemoAna.Server in $Configuration configuration."
$processes += Start-Process -FilePath 'dotnet' -ArgumentList @(
    'run', '--project', $serverProject, '--configuration', $Configuration
) -PassThru

if ($Target -in @('Windows', 'Both')) {
    Write-Info "Starting MemoAna Windows target in $Configuration configuration."
    $processes += Start-Process -FilePath 'dotnet' -ArgumentList @(
        'run', '--project', $mauiProject,
        '--framework', 'net10.0-windows10.0.19041.0',
        '--configuration', $Configuration
    ) -PassThru
}

if ($Target -in @('Android', 'Both')) {
    Write-Info "Starting MemoAna Android target in $Configuration configuration."

    $arguments = @(
        'run', '--project', $mauiProject,
        '--framework', 'net10.0-android',
        '--configuration', $Configuration
    )

    if ($AndroidDevice) {
        $arguments += @('--device', $AndroidDevice)
    }

    $processes += Start-Process -FilePath 'dotnet' -ArgumentList $arguments -PassThru
}

Write-Info "Started $($processes.Count) process(es)."
Write-Info 'The launched processes remain independent of this PowerShell process.'
Write-Info 'Use Visual Studio F5 with MemoAna.slnLaunch for the shared Windows + API debug profile.'
