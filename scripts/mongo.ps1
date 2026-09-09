<#
.SYNOPSIS
    Script for instantiate an MongoDB container through Docker.
.DESCRIPTION
    Creates an local volume for data persistency and runs the container with
    settings loaded through script parameters or defaults if not supplied.
#>

param(
    [string]$CN = "mongodb-local",
    [string]$DN = "mongo-db",
    [string]$U = "mongodb",
    [string]$P = "m0n60_DB",
    [int]$HostPort = 27017,
    [string]$VolumeName = "mongo-db-data"
)

$volumeExists = docker volume ls --q -f "name=^${VolumeName}$"
if (-not $volumeExists) {
    Write-Host "Creating data volume: $VolumeName..." -ForegroundColor Cyan
    docker volume create $VolumeName | Out-Null
}

$ExistingContainer = docker ps -a -q -f "name=^/${CN}$"
if ($ExistingContainer) {
    Write-Host "Container '$CN' already exists. Stoping and removing..." -ForegroundColor Yellow
    docker stop $CN | Out-Null
    docker rm $CN | Out-Null
}

Write-Host "Starting MongoDB container '$CN'..." -ForegroundColor Green

docker run -d `
    --name $CN `
    -p "${HostPort}:27017" `
    -e MONGO_INITDB_ROOT_U=$U `
    -e MONGO_INITDB_ROOT_P=$P `
    -e MONGO_INITDB_DATABASE=$DN `
    -v "${VolumeName}:/data/db" `
    --restart unless-stopped `
    mongo:latest

if ($LASTEXITCODE -eq 0) {
    Write-Host "`MongoDB nContainer successfully started!" -ForegroundColor Green
    Write-Host "------------------------------------------------" -ForegroundColor Gray
    Write-Host "Container Name    : $CN"
    Write-Host "HostPort          : $HostPort"
    Write-Host "Database Name     : $DN"
    Write-Host "User              : $U"
    Write-Host "Volume            : $VolumeName"
    Write-Host "Connection String : mongodb://${U}:${P}@localhost:${HostPort}/${DN}?authSource=admin"
    Write-Host "------------------------------------------------" -ForegroundColor Gray
    "mongodb://${U}:${P}@localhost:${HostPort}/${DN}?authSource=admin" | Set-Clipboard
}
else {
    Write-Host "`nFalha ao iniciar o container do MongoDB." -ForegroundColor Red
}