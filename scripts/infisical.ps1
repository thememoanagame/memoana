# ./scripts/infisical.ps1
$ErrorActionPreference = "Stop"

# Check if Infisical is installed
if (-not (Get-Command infisical -ErrorAction SilentlyContinue)) {
    Write-Host "[Infisical] CLI not found. Installing..." -ForegroundColor Yellow
    winget install --id Infisical.Infisical --silent --accept-source-agreements --accept-package-agreements
    
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
}

if (-not (Get-Command infisical -ErrorAction SilentlyContinue)) {
    Write-Error "[Infisical] Error installing infiscal. Do it manually."
    exit 1
}

Write-Host "[Infisical] CLI ready." -ForegroundColor Green

if (-not (Test-Path ".infisical.json")) {
    Write-Host "[Infisical] Project uninitialized. Initalizing'..." -ForegroundColor Yellow
    infisical init
}