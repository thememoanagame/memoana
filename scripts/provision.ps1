[CmdletBinding()]
param(
    [string]$CredentialsDirectory,
    [string]$EnvironmentFile,
    [switch]$SkipBrowser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($CredentialsDirectory)) {
    $CredentialsDirectory = Join-Path $env:USERPROFILE '.memoana\google-credentials'
}
if ([string]::IsNullOrWhiteSpace($EnvironmentFile)) {
    $EnvironmentFile = Join-Path $RepositoryRoot '.env'
}

function Read-RequiredValue([string]$Prompt) {
    do { $value = (Read-Host $Prompt).Trim() } while ([string]::IsNullOrWhiteSpace($value))
    return $value
}

function Find-JsonValue($Object, [string]$PropertyName) {
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($PropertyName)) { return [string]$Object[$PropertyName] }
        foreach ($key in $Object.Keys) {
            $found = Find-JsonValue $Object[$key] $PropertyName
            if ($found) { return $found }
        }
    }
    elseif ($Object -is [pscustomobject]) {
        $property = $Object.PSObject.Properties[$PropertyName]
        if ($property) { return [string]$property.Value }
        foreach ($property in $Object.PSObject.Properties) {
            $found = Find-JsonValue $property.Value $PropertyName
            if ($found) { return $found }
        }
    }
    elseif ($Object -is [array]) {
        foreach ($item in $Object) {
            $found = Find-JsonValue $item $PropertyName
            if ($found) { return $found }
        }
    }
    return $null
}

function Import-CredentialJson([string]$Kind, [string]$DestinationName) {
    Write-Host ''
    Write-Host "Crie o cliente OAuth do tipo $Kind no Google Auth Platform > Clients." -ForegroundColor Cyan
    $source = Read-RequiredValue "Caminho completo do JSON baixado para o cliente $Kind"
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Arquivo JSON não encontrado: $source"
    }

    $json = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
    $clientId = Find-JsonValue $json 'client_id'
    $clientSecret = Find-JsonValue $json 'client_secret'
    if ([string]::IsNullOrWhiteSpace($clientId)) {
        throw 'O JSON informado não contém client_id. Verifique se é o arquivo OAuth correto.'
    }

    $destination = Join-Path $CredentialsDirectory $DestinationName
    Copy-Item -LiteralPath $source -Destination $destination -Force
    return [pscustomobject]@{
        ClientId = $clientId
        ClientSecret = $clientSecret
        File = $destination
    }
}

function Set-EnvValue([string[]]$Lines, [string]$Key, [string]$Value) {
    $escaped = $Value.Replace([Environment]::NewLine, '')
    $pattern = '^\s*' + [regex]::Escape($Key) + '='
    $replacement = "$Key=$escaped"
    $found = $false
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match $pattern) {
            $Lines[$i] = $replacement
            $found = $true
        }
    }
    if (-not $found) { $Lines += $replacement }
    return ,$Lines
}

Write-Host 'MemoAna - provisionamento de credenciais Google Play Games' -ForegroundColor Green
Write-Host 'A criação dos OAuth clients é concluída no Google Auth Platform. Este script orienta o processo, importa os JSONs e configura o .env.'

$resolvedCredentials = [System.IO.Path]::GetFullPath($CredentialsDirectory)
if ($resolvedCredentials.StartsWith($RepositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "O diretório de credenciais deve ficar FORA do repositório: $resolvedCredentials"
}
New-Item -ItemType Directory -Path $resolvedCredentials -Force | Out-Null
$CredentialsDirectory = $resolvedCredentials

Write-Host "Arquivos de credenciais locais encontrados em $CredentialsDirectory"
$existing = @(Get-ChildItem -LiteralPath $CredentialsDirectory -File -Filter '*.json' -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
    $existing | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
    $cleanup = Read-Host 'Deseja limpar JSONs locais antigos antes de provisionar? (s/N)'
    if ($cleanup -match '^(s|sim)$') {
        foreach ($file in $existing) {
            Remove-Item -LiteralPath $file.FullName -Force
            Write-Host "Removido: $($file.Name)"
        }
    }
}
else { Write-Host 'Nenhum JSON local anterior encontrado.' }

$webName = Read-RequiredValue 'Nome para o OAuth client Web (identificação no Console)'
$androidName = Read-RequiredValue 'Nome para o OAuth client Android (identificação no Console)'
$applicationId = Read-RequiredValue 'Google Play Games Services Application ID (não é OAuth Client ID)'
$packageName = Read-RequiredValue 'Package name Android cadastrado no Play Console'
$sha1 = Read-RequiredValue 'SHA-1 do certificado de assinatura Android cadastrado no Play Console'

if (-not $SkipBrowser) { Start-Process 'https://console.cloud.google.com/auth/clients' }

Write-Host ''
Write-Host "Crie um cliente Web chamado '$webName'." -ForegroundColor Yellow
Write-Host 'Não adicione Authorized JavaScript origins nem Authorized redirect URIs: o backend atual não possui endpoint OAuth callback.'
Write-Host 'Baixe o JSON do cliente Web e informe o caminho abaixo.'
$web = Import-CredentialJson 'Web application' 'web-client.json'
if ([string]::IsNullOrWhiteSpace($web.ClientSecret)) {
    throw 'O JSON Web não contém client_secret. Não prossiga com um arquivo de cliente diferente.'
}

Write-Host ''
Write-Host "Crie um cliente Android chamado '$androidName'." -ForegroundColor Yellow
Write-Host "Package name: $packageName"
Write-Host "SHA-1: $sha1"
Write-Host 'Baixe/exporte o JSON do cliente Android e informe o caminho abaixo.'
$android = Import-CredentialJson 'Android' 'android-client.json'

$lines = @()
if (Test-Path -LiteralPath $EnvironmentFile) {
    $lines = @(Get-Content -LiteralPath $EnvironmentFile)
}
else {
    $example = Join-Path $RepositoryRoot '.env.example'
    if (Test-Path -LiteralPath $example) { $lines = @(Get-Content -LiteralPath $example) }
}

$values = [ordered]@{
    GOOGLE_PLAY_GAMES_APPLICATION_ID = $applicationId
    GOOGLE_PLAY_GAMES_ANDROID_CLIENT_ID = $android.ClientId
    GOOGLE_PLAY_GAMES_WEB_CLIENT_ID = $web.ClientId
    GOOGLE_PLAY_GAMES_WEB_CLIENT_SECRET = $web.ClientSecret
    GOOGLE_PLAY_GAMES_AUTHORITY = 'https://accounts.google.com'
    GOOGLE_PLAY_GAMES_AUDIENCE = $web.ClientId
    GOOGLE_PLAY_GAMES_VALID_AUDIENCE = $web.ClientId
    GOOGLE_PLAY_GAMES_REDIRECT_URI = ''
}

foreach ($entry in $values.GetEnumerator()) {
    $lines = Set-EnvValue $lines $entry.Key ([string]$entry.Value)
}

$envDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($EnvironmentFile))
New-Item -ItemType Directory -Path $envDirectory -Force | Out-Null
Set-Content -LiteralPath $EnvironmentFile -Value $lines -Encoding utf8

Write-Host ''
Write-Host ".env atualizado: $EnvironmentFile" -ForegroundColor Green
Write-Host "JSON Web: $($web.File)"
Write-Host "JSON Android: $($android.File)"
Write-Host ''
Write-Host 'Inventário dos arquivos locais de credenciais:' -ForegroundColor Cyan
Get-ChildItem -LiteralPath $CredentialsDirectory -File -Filter '*.json' |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize

Write-Host 'IMPORTANTE:' -ForegroundColor Yellow
Write-Host '- O inventário representa JSONs locais, não confirma o estado ativo dos OAuth clients no Google Cloud.'
Write-Host '- A limpeza opcional remove somente cópias locais; não exclui credenciais remotas.'
Write-Host '- Para listar, desativar ou excluir clientes ativos, use Google Auth Platform > Clients.'
Write-Host '- Proteja a pasta de credenciais e o .env. Nunca os versione nem compartilhe.'
