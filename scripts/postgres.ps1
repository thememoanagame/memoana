<#
.SYNOPSIS
    Script for instantiate an Postgres container through Docker.
.DESCRIPTION
    Creates an local volume for data persistency and runs the container with
    settings loaded through script parameters or defaults if not supplied.
#>

param(
    [string]$CN = "postgres",
    [string]$PH = "5432",
    [string]$DN = "postgres",
    [string]$U = "postgres",
    [string]$P = "p057_6R35",
    [string]$VN = "postgres-data",
    [string]$IN = "postgres:17") 

$volumeExists = docker volume ls --q -f "name=^${VolumeName}$"
if (-not $volumeExists) {
    Write-Host "Creating data volume: $VN..." -ForegroundColor Cyan
    docker volume create $VN | Out-Null
}

$containerExists = docker ps -a -q -f "name=^${ContainerName}$"

if ($containerExists) {
    $status = docker inspect --format='{{.State.Running}}' $CN
    
    if ($status -eq "true") {
        Write-Host "The container '$CN' is already running." -ForegroundColor Green
    } else {
        Write-Host "Starting existing container '$CN'..." -ForegroundColor Yellow
        docker start $CN | Out-Null
        Write-Host "Container successfully started!" -ForegroundColor Green
    }
} else {
    Write-Host "Creating and starting a new container '$CN'..." -ForegroundColor Cyan
    
    docker run -d `
        --name $CN `
        -p "${PH}:5432" `
        -e POSTGRES_DB=$DN `
        -e POSTGRES_USER=$U `
        -e POSTGRES_PASSWORD=$P `
        -v "${VN}:/var/lib/postgresql/data" `
        --restart unless-stopped `
        $IN | Out-Null

    Write-Host "Started container '$CN' at port $PH!" -ForegroundColor Green
}

Write-Host ""
Write-Host "Container Status:" -ForegroundColor Green
docker ps -f "name=^${ContainerName}$"