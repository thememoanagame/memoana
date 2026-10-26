#requires -Version 7.0

<#
.SYNOPSIS
    Cria e gerencia os assets de assinatura e publicação Android do MemoAna,
    além de gerar os artefatos AAB e APK para publicação/testes.

.DESCRIPTION
    O script:
      1. Configura a cultura do processo para en-US.
      2. Solicita um diretório raiz para armazenamento dos certificados.
      3. Persiste esse diretório em uma variável de ambiente Machine.
      4. Cria uma estrutura de armazenamento específica para o MemoAna.
      5. Valida Java, keytool, Android SDK e .NET SDK.
      6. Cria ou reutiliza o keystore de assinatura.
      7. Armazena os metadados da publicação em publishing-info.txt.
      8. Cria backups versionados do keystore e dos metadados.
      9. Executa dotnet publish para AAB e APK Release.
     10. Copia os artefatos para a pasta de release.
     11. Exibe todos os caminhos relevantes ao final.

.NOTES
    Cultura:
      - Interface/logs: en-US.
      - Comentários: pt-BR.

    Segurança:
      - O arquivo publishing-info.txt contém senhas em texto claro,
        conforme solicitado.
      - O diretório de assinatura deve permanecer fora do repositório Git.
      - Nunca adicione o diretório configurado por
        MEMOANA_ANDROID_SIGNING_ROOT ao controle de versão.

    Requisitos:
      - PowerShell 7+
      - .NET SDK 10
      - Java/JDK compatível com Android SDK
      - Android SDK
      - keytool
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# -----------------------------------------------------------------------------
# Culture
# -----------------------------------------------------------------------------

try {
    [System.Threading.Thread]::CurrentThread.CurrentCulture =
    [System.Globalization.CultureInfo]::GetCultureInfo('en-US')

    [System.Threading.Thread]::CurrentThread.CurrentUICulture =
    [System.Globalization.CultureInfo]::GetCultureInfo('en-US')

    $PSDefaultParameterValues['Out-File:Encoding'] = 'utf8'
}
catch {
    Write-Error "Failed to configure process culture: $($_.Exception.Message)"
    exit 1
}

# -----------------------------------------------------------------------------
# Constants
# -----------------------------------------------------------------------------

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepositoryRoot = Split-Path -Parent $ScriptRoot

$ProjectPath = Join-Path `
    $RepositoryRoot `
    'src\MemoAna\MemoAna.csproj'

$AndroidTargetFramework = 'net10.0-android'
$Configuration = 'Release'

$ApplicationName = 'MemoAna'
$ApplicationId = 'com.remotecodehub.memoana'

$SigningRootEnvironmentVariable = 'MEMOANA_ANDROID_SIGNING_ROOT'

$DefaultApplicationSigningFolderName = 'MemoAna'

$KeyStoreFileName = 'memoana-upload-keystore.jks'
$PublishingInfoFileName = 'publishing-info.txt'

$KeystoreDirectoryName = 'keystore'
$MetadataDirectoryName = 'metadata'
$BackupDirectoryName = 'backup'
$ReleasesDirectoryName = 'releases'

$EnvironmentVariableTarget = [System.EnvironmentVariableTarget]::Machine

# -----------------------------------------------------------------------------
# Logging
# -----------------------------------------------------------------------------

function Write-Log {
    param(
        [Parameter(Mandatory)]
        [string] $Message,

        [ValidateSet(
            'Info',
            'Success',
            'Warning',
            'Error',
            'Step'
        )]
        [string] $Level = 'Info'
    )

    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

    switch ($Level) {
        'Info' {
            $color = 'Gray'
            $prefix = '[INFO]'
        }

        'Success' {
            $color = 'Green'
            $prefix = '[ OK ]'
        }

        'Warning' {
            $color = 'Yellow'
            $prefix = '[WARN]'
        }

        'Error' {
            $color = 'Red'
            $prefix = '[FAIL]'
        }

        'Step' {
            $color = 'Cyan'
            $prefix = '[STEP]'
        }
    }

    Write-Host "$timestamp $prefix $Message" -ForegroundColor $color
}

function Write-Section {
    param(
        [Parameter(Mandatory)]
        [string] $Title
    )

    Write-Host ''
    Write-Host ('=' * 78) -ForegroundColor DarkCyan
    Write-Host " $Title" -ForegroundColor Cyan
    Write-Host ('=' * 78) -ForegroundColor DarkCyan
    Write-Host ''
}

# -----------------------------------------------------------------------------
# Retry / Cancel pattern
# -----------------------------------------------------------------------------

function Read-Retry-Or-Cancel {
    param(
        [Parameter(Mandatory)]
        [string] $Prompt,

        [Parameter(Mandatory)]
        [scriptblock] $Validator,

        [switch] $Secret,

        [string] $ValidationMessage = 'Invalid value.'
    )

    while ($true) {
        if ($Secret) {
            $value = Read-Host "$Prompt (type CANCEL to abort)" -AsSecureString

            $plainText = [System.Net.NetworkCredential]::new(
                '',
                $value
            ).Password

            if ($plainText -eq 'CANCEL') {
                throw 'Operation cancelled by user.'
            }
        }
        else {
            $value = Read-Host "$Prompt (type CANCEL to abort)"

            if ($value -eq 'CANCEL') {
                throw 'Operation cancelled by user.'
            }
        }

        try {
            $isValid = & $Validator $value

            if ($isValid) {
                return $value
            }
        }
        catch {
            Write-Log $_.Exception.Message -Level Warning
        }

        Write-Log $ValidationMessage -Level Warning

        $action = Read-Host 'Enter R to retry or C to cancel'

        if ($action -match '^[Cc]$') {
            throw 'Operation cancelled by user.'
        }

        if ($action -notmatch '^[Rr]$') {
            Write-Log 'Invalid action. The input will be requested again.' -Level Warning
        }
    }
}

# -----------------------------------------------------------------------------
# Path helpers
# -----------------------------------------------------------------------------

function Ensure-Directory {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        Write-Log "Creating directory: $Path" -Level Info

        New-Item `
            -ItemType Directory `
            -Path $Path `
            -Force `
        | Out-Null
    }
}

function Resolve-ExistingDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    try {
        $resolved = [System.IO.Path]::GetFullPath($Path)
    }
    catch {
        return $false
    }

    return Test-Path -LiteralPath $resolved -PathType Container
}

# -----------------------------------------------------------------------------
# Command helpers
# -----------------------------------------------------------------------------

function Test-CommandAvailable {
    param(
        [Parameter(Mandatory)]
        [string] $Command
    )

    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

function Invoke-CommandChecked {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    Write-Log "Executing: $Command $($Arguments -join ' ')" -Level Info

    & $Command @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

# -----------------------------------------------------------------------------
# Administrator check
# -----------------------------------------------------------------------------

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()

    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )
}

# -----------------------------------------------------------------------------
# Environment variable
# -----------------------------------------------------------------------------

function Get-SigningRoot {
    $machineValue = [Environment]::GetEnvironmentVariable(
        $SigningRootEnvironmentVariable,
        $EnvironmentVariableTarget
    )

    if (-not [string]::IsNullOrWhiteSpace($machineValue)) {
        return [System.IO.Path]::GetFullPath($machineValue)
    }

    $processValue = [Environment]::GetEnvironmentVariable(
        $SigningRootEnvironmentVariable,
        [EnvironmentVariableTarget]::Process
    )

    if (-not [string]::IsNullOrWhiteSpace($processValue)) {
        return [System.IO.Path]::GetFullPath($processValue)
    }

    return $null
}

function Set-SigningRoot {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    Write-Log `
        "Saving $SigningRootEnvironmentVariable as a machine environment variable." `
        -Level Step

    try {
        [Environment]::SetEnvironmentVariable(
            $SigningRootEnvironmentVariable,
            $Path,
            $EnvironmentVariableTarget
        )
    }
    catch {
        throw @"
Unable to save the machine environment variable.

Variable:
  $SigningRootEnvironmentVariable

Value:
  $Path

The script must be executed from an elevated PowerShell session
because the variable is intentionally stored at Machine scope.

Original error:
$($_.Exception.Message)
"@
    }

    # Atualiza também o processo atual, pois uma variável Machine recém-criada
    # não é automaticamente propagada para o processo PowerShell existente.
    [Environment]::SetEnvironmentVariable(
        $SigningRootEnvironmentVariable,
        $Path,
        [EnvironmentVariableTarget]::Process
    )

    Write-Log `
        "Machine environment variable configured successfully." `
        -Level Success
}

# -----------------------------------------------------------------------------
# Project information
# -----------------------------------------------------------------------------

function Read-ProjectProperty {
    param(
        [Parameter(Mandatory)]
        [string] $PropertyName
    )

    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
        throw "Project file not found: $ProjectPath"
    }

    $content = Get-Content `
        -LiteralPath $ProjectPath `
        -Raw

    $pattern = "<$PropertyName>(?<value>[^<]+)</$PropertyName>"

    $match = [regex]::Match(
        $content,
        $pattern,
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )

    if (-not $match.Success) {
        return $null
    }

    return $match.Groups['value'].Value.Trim()
}

# -----------------------------------------------------------------------------
# Tool validation
# -----------------------------------------------------------------------------

function Test-Prerequisites {
    Write-Section 'Validating build prerequisites'

    if (-not (Test-CommandAvailable 'dotnet')) {
        throw 'dotnet was not found in PATH.'
    }

    if (-not (Test-CommandAvailable 'keytool')) {
        throw 'keytool was not found in PATH. Install a compatible JDK and configure PATH.'
    }

    Write-Log 'dotnet and keytool were found.' -Level Success

    Write-Log 'Checking .NET SDK...' -Level Step

    $dotnetVersion = & dotnet --version

    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to determine the installed .NET SDK version.'
    }

    Write-Log "Detected .NET SDK: $dotnetVersion" -Level Success

    if (-not $dotnetVersion.StartsWith('10.')) {
        throw "The project targets .NET 10. Detected SDK: $dotnetVersion"
    }

    Write-Log 'Checking Java...' -Level Step

    & java -version 2>&1 | ForEach-Object {
        Write-Log $_ -Level Info
    }

    if ($LASTEXITCODE -ne 0) {
        throw 'Java was not found or could not be executed.'
    }

    Write-Log 'Checking Android SDK...' -Level Step

    $androidSdk = $env:ANDROID_HOME

    if ([string]::IsNullOrWhiteSpace($androidSdk)) {
        $androidSdk = $env:ANDROID_SDK_ROOT
    }

    if ([string]::IsNullOrWhiteSpace($androidSdk)) {
        throw 'ANDROID_HOME or ANDROID_SDK_ROOT must be configured.'
    }

    if (-not (Test-Path -LiteralPath $androidSdk -PathType Container)) {
        throw "Android SDK directory does not exist: $androidSdk"
    }

    Write-Log "Android SDK: $androidSdk" -Level Success

    Write-Log 'Checking project...' -Level Step

    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
        throw "MemoAna project was not found: $ProjectPath"
    }

    Write-Log "Project: $ProjectPath" -Level Success
}

# -----------------------------------------------------------------------------
# Signing configuration
# -----------------------------------------------------------------------------

function Get-PasswordHash {
    param(
        [Parameter(Mandatory)]
        [string] $Password
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Password)

    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)

    return [Convert]::ToHexString($hash)
}

function Test-PasswordStrength {
    param(
        [Parameter(Mandatory)]
        [string] $Password
    )

    if ($Password.Length -lt 12) {
        return $false
    }

    $hasUpper = $Password -cmatch '[A-Z]'
    $hasLower = $Password -cmatch '[a-z]'
    $hasNumber = $Password -match '\d'
    $hasSpecial = $Password -match '[^a-zA-Z0-9]'

    return $hasUpper -and $hasLower -and $hasNumber -and $hasSpecial
}

function Create-Keystore {
    param(
        [Parameter(Mandatory)]
        [string] $KeystorePath,

        [Parameter(Mandatory)]
        [string] $Alias,

        [Parameter(Mandatory)]
        [string] $StorePassword,

        [Parameter(Mandatory)]
        [string] $KeyPassword,

        [Parameter(Mandatory)]
        [string] $DistinguishedName
    )

    if (Test-Path -LiteralPath $KeystorePath -PathType Leaf) {
        throw "Keystore already exists: $KeystorePath"
    }

    Write-Section 'Creating Android upload keystore'

    $arguments = @(
        '-genkeypair'
        '-v'
        '-keystore'
        $KeystorePath
        '-alias'
        $Alias
        '-keyalg'
        'RSA'
        '-keysize'
        '4096'
        '-validity'
        '10000'
        '-storepass'
        $StorePassword
        '-keypass'
        $KeyPassword
        '-dname'
        $DistinguishedName
    )

    Invoke-CommandChecked `
        -Command 'keytool' `
        -Arguments $arguments

    if (-not (Test-Path -LiteralPath $KeystorePath -PathType Leaf)) {
        throw 'keytool completed but the keystore was not created.'
    }

    Write-Log "Keystore created: $KeystorePath" -Level Success
}

# -----------------------------------------------------------------------------
# Publishing metadata
# -----------------------------------------------------------------------------

function Write-PublishingInfo {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [hashtable] $Configuration
    )

    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'

    $passwordHash = Get-PasswordHash `
        -Password $Configuration.StorePassword

    $content = @"
MemoAna Android Publishing Information
======================================

Generated:
$timestamp

Application:
$ApplicationName

Application ID:
$ApplicationId

Target Framework:
$AndroidTargetFramework

Configuration:
$ConfigurationName

Upload Keystore:
$($Configuration.KeystorePath)

Keystore Alias:
$($Configuration.Alias)

Store Password:
$($Configuration.StorePassword)

Key Password:
$($Configuration.KeyPassword)

Certificate Subject:
$($Configuration.DistinguishedName)

Store Password SHA-256:
$passwordHash

IMPORTANT
---------
This file contains sensitive signing credentials.

Do NOT commit this file to Git.
Do NOT upload this file to a public repository.
Keep an independent secure backup.

The SHA-256 value above is informational only and must not be
used as a replacement for the actual password.
"@

    Set-Content `
        -LiteralPath $Path `
        -Value $content `
        -Encoding utf8

    Write-Log "Publishing metadata written: $Path" -Level Success
}

# -----------------------------------------------------------------------------
# Backup
# -----------------------------------------------------------------------------

function Backup-SigningAssets {
    param(
        [Parameter(Mandatory)]
        [string] $ApplicationRoot,

        [Parameter(Mandatory)]
        [string] $KeystorePath,

        [Parameter(Mandatory)]
        [string] $PublishingInfoPath
    )

    Write-Section 'Creating signing assets backup'

    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'

    $backupPath = Join-Path `
        $ApplicationRoot `
        "$BackupDirectoryName\$timestamp"

    Ensure-Directory $backupPath

    Copy-Item `
        -LiteralPath $KeystorePath `
        -Destination (Join-Path $backupPath $KeyStoreFileName) `
        -Force

    Copy-Item `
        -LiteralPath $PublishingInfoPath `
        -Destination (Join-Path $backupPath $PublishingInfoFileName) `
        -Force

    $manifestPath = Join-Path `
        $backupPath `
        'backup-manifest.txt'

    @"
MemoAna signing backup
======================

Created:
$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')

Application:
$ApplicationName

Application ID:
$ApplicationId

Keystore:
$KeyStoreFileName

Publishing information:
$PublishingInfoFileName

Purpose:
Long-term backup of the Android upload signing assets.
"@ | Set-Content `
        -LiteralPath $manifestPath `
        -Encoding utf8

    Write-Log "Backup created: $backupPath" -Level Success

    return $backupPath
}

# -----------------------------------------------------------------------------
# Build
# -----------------------------------------------------------------------------

function Publish-Android {
    param(
        [Parameter(Mandatory)]
        [string] $KeystorePath,

        [Parameter(Mandatory)]
        [string] $Alias,

        [Parameter(Mandatory)]
        [string] $StorePassword,

        [Parameter(Mandatory)]
        [string] $KeyPassword
    )

    Write-Section 'Publishing Android Release'

    $publishRoot = Join-Path `
        $RepositoryRoot `
        'artifacts\android\publish'

    Ensure-Directory $publishRoot

    $arguments = @(
        'publish'
        $ProjectPath
        '-f'
        $AndroidTargetFramework
        '-c'
        $Configuration
        '-p:AndroidKeyStore=true'
        "-p:AndroidSigningKeyStore=$KeystorePath"
        "-p:AndroidSigningKeyAlias=$Alias"
        "-p:AndroidSigningStorePass=$StorePassword"
        "-p:AndroidSigningKeyPass=$KeyPassword"
        '--no-restore'
    )

    Invoke-CommandChecked `
        -Command 'dotnet' `
        -Arguments $arguments

    Write-Log 'Android Release publish completed.' -Level Success

    return $publishRoot
}

# -----------------------------------------------------------------------------
# Locate artifacts
# -----------------------------------------------------------------------------

function Find-AndroidArtifacts {
    $publishRoot = Join-Path `
        $RepositoryRoot `
        "src\MemoAna\bin\$Configuration\$AndroidTargetFramework"

    if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
        throw "Android publish output directory was not found: $publishRoot"
    }

    $aab = Get-ChildItem `
        -LiteralPath $publishRoot `
        -Filter '*.aab' `
        -Recurse `
        -File `
    | Sort-Object LastWriteTime -Descending `
    | Select-Object -First 1

    $apk = Get-ChildItem `
        -LiteralPath $publishRoot `
        -Filter '*.apk' `
        -Recurse `
        -File `
    | Sort-Object LastWriteTime -Descending `
    | Select-Object -First 1

    return @{
        Root = $publishRoot
        Aab  = $aab
        Apk  = $apk
    }
}

# -----------------------------------------------------------------------------
# Release asset storage
# -----------------------------------------------------------------------------

function Store-ReleaseArtifacts {
    param(
        [Parameter(Mandatory)]
        [string] $ApplicationRoot,

        [Parameter(Mandatory)]
        [System.IO.FileInfo] $Aab,

        [System.IO.FileInfo] $Apk,

        [Parameter(Mandatory)]
        [string] $PublishingInfoPath
    )

    $versionName = Read-ProjectProperty `
        -PropertyName 'ApplicationDisplayVersion'

    $versionCode = Read-ProjectProperty `
        -PropertyName 'ApplicationVersion'

    if ([string]::IsNullOrWhiteSpace($versionName)) {
        $versionName = 'unknown'
    }

    if ([string]::IsNullOrWhiteSpace($versionCode)) {
        $versionCode = 'unknown'
    }

    $releaseDirectoryName = "$versionName-$versionCode"

    $releasePath = Join-Path `
        $ApplicationRoot `
        "$ReleasesDirectoryName\$releaseDirectoryName"

    Ensure-Directory $releasePath

    Copy-Item `
        -LiteralPath $Aab.FullName `
        -Destination (Join-Path $releasePath $Aab.Name) `
        -Force

    if ($null -ne $Apk) {
        Copy-Item `
            -LiteralPath $Apk.FullName `
            -Destination (Join-Path $releasePath $Apk.Name) `
            -Force
    }

    Copy-Item `
        -LiteralPath $PublishingInfoPath `
        -Destination (Join-Path $releasePath $PublishingInfoFileName) `
        -Force

    $manifestPath = Join-Path `
        $releasePath `
        'release-info.txt'

    @"
MemoAna Android Release
=======================

Application:
$ApplicationName

Application ID:
$ApplicationId

Version:
$versionName

Version Code:
$versionCode

Generated:
$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')

AAB:
$($Aab.Name)

APK:
$(if ($null -ne $Apk) { $Apk.Name } else { 'Not generated' })

Purpose:
AAB is the artifact intended for Google Play Console.
APK is retained for local/device testing when available.
"@ | Set-Content `
        -LiteralPath $manifestPath `
        -Encoding utf8

    Write-Log "Release assets stored: $releasePath" -Level Success

    return @{
        Path        = $releasePath
        Version     = $versionName
        VersionCode = $versionCode
    }
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

try {
    Clear-Host

    Write-Section 'MemoAna Android Publishing'

    Write-Log 'Starting Android publishing preparation.' -Level Info
    Write-Log "Application: $ApplicationName" -Level Info
    Write-Log "Application ID: $ApplicationId" -Level Info

    # A variável Machine exige privilégios administrativos.
    if (-not (Test-IsAdministrator)) {
        throw @"
This script must run as Administrator.

The signing repository location is intentionally persisted as a
Machine environment variable named:

$SigningRootEnvironmentVariable
"@
    }

    Test-Prerequisites

    # -------------------------------------------------------------------------
    # Signing repository
    # -------------------------------------------------------------------------

    Write-Section 'Signing repository'

    $existingSigningRoot = Get-SigningRoot

    if ($null -ne $existingSigningRoot) {
        Write-Log `
            "Existing $SigningRootEnvironmentVariable detected:" `
            -Level Info

        Write-Log `
            $existingSigningRoot `
            -Level Info

        $useExisting = Read-Host `
            'Use this directory? [Y]es / [N]o / [C]ancel'

        if ($useExisting -match '^[Cc]$') {
            throw 'Operation cancelled by user.'
        }

        if ($useExisting -notmatch '^[Yy]$') {
            $existingSigningRoot = $null
        }
    }

    if ($null -eq $existingSigningRoot) {
        $existingSigningRoot = Read-Retry-Or-Cancel `
            -Prompt 'Enter the root directory for application signing assets' `
            -Validator {
            param($value)

            if ([string]::IsNullOrWhiteSpace($value)) {
                throw 'The signing root cannot be empty.'
            }

            try {
                [System.IO.Path]::GetFullPath($value) | Out-Null
                return $true
            }
            catch {
                throw 'The specified path is invalid.'
            }
        } `
            -ValidationMessage `
            'Enter a valid absolute or relative Windows path.'

        $existingSigningRoot = [System.IO.Path]::GetFullPath(
            $existingSigningRoot
        )

        Ensure-Directory $existingSigningRoot

        Set-SigningRoot $existingSigningRoot
    }
    else {
        Ensure-Directory $existingSigningRoot
    }

    $SigningRoot = $existingSigningRoot

    # -------------------------------------------------------------------------
    # Application-specific repository
    # -------------------------------------------------------------------------

    $ApplicationRoot = Join-Path `
        $SigningRoot `
        $DefaultApplicationSigningFolderName

    $KeystoreDirectory = Join-Path `
        $ApplicationRoot `
        $KeystoreDirectoryName

    $MetadataDirectory = Join-Path `
        $ApplicationRoot `
        $MetadataDirectoryName

    $BackupDirectory = Join-Path `
        $ApplicationRoot `
        $BackupDirectoryName

    $ReleasesDirectory = Join-Path `
        $ApplicationRoot `
        $ReleasesDirectoryName

    Ensure-Directory $ApplicationRoot
    Ensure-Directory $KeystoreDirectory
    Ensure-Directory $MetadataDirectory
    Ensure-Directory $BackupDirectory
    Ensure-Directory $ReleasesDirectory

    $KeystorePath = Join-Path `
        $KeystoreDirectory `
        $KeyStoreFileName

    $PublishingInfoPath = Join-Path `
        $MetadataDirectory `
        $PublishingInfoFileName

    Write-Log "Application signing repository: $ApplicationRoot" -Level Success

    # -------------------------------------------------------------------------
    # Keystore
    # -------------------------------------------------------------------------

    if (Test-Path -LiteralPath $KeystorePath -PathType Leaf) {
        Write-Log "Existing keystore detected: $KeystorePath" -Level Warning

        $reuseKeystore = Read-Host `
            'Reuse this keystore? [Y]es / [N]o / [C]ancel'

        if ($reuseKeystore -match '^[Cc]$') {
            throw 'Operation cancelled by user.'
        }

        if ($reuseKeystore -notmatch '^[Yy]$') {
            throw @"
The existing keystore must not be overwritten automatically.

Move or rename the existing keystore if a completely new signing identity
is intentionally required.
"@
        }
    }
    else {
        Write-Section 'Creating signing identity'

        $Alias = Read-Retry-Or-Cancel `
            -Prompt 'Enter the keystore alias' `
            -Validator {
            param($value)

            if ($value -notmatch '^[A-Za-z0-9._-]{3,64}$') {
                throw `
                    'Alias must contain 3-64 characters using letters, numbers, dot, underscore or hyphen.'
            }

            return $true
        } `
            -ValidationMessage `
            'Invalid alias.'

        $StorePassword = Read-Retry-Or-Cancel `
            -Prompt 'Enter the keystore password' `
            -Secret `
            -Validator {
            param($value)

            if (-not (Test-PasswordStrength $value)) {
                throw @'
Password must contain at least 12 characters and include:
- uppercase letter
- lowercase letter
- number
- special character
'@
            }

            return $true
        } `
            -ValidationMessage `
            'The password does not meet the required complexity.'

        $KeyPassword = Read-Retry-Or-Cancel `
            -Prompt 'Enter the key password' `
            -Secret `
            -Validator {
            param($value)

            if (-not (Test-PasswordStrength $value)) {
                throw @'
Password must contain at least 12 characters and include:
- uppercase letter
- lowercase letter
- number
- special character
'@
            }

            return $true
        } `
            -ValidationMessage `
            'The key password does not meet the required complexity.'

        $confirmation = Read-Host `
            'Confirm the key password'

        if ($confirmation -ne $KeyPassword) {
            throw 'Key password confirmation did not match.'
        }

        $DistinguishedName = Read-Retry-Or-Cancel `
            -Prompt 'Enter certificate distinguished name' `
            -Validator {
            param($value)

            if ([string]::IsNullOrWhiteSpace($value)) {
                throw 'Distinguished name cannot be empty.'
            }

            return $true
        } `
            -ValidationMessage `
            'Distinguished name cannot be empty.'

        Create-Keystore `
            -KeystorePath $KeystorePath `
            -Alias $Alias `
            -StorePassword $StorePassword `
            -KeyPassword $KeyPassword `
            -DistinguishedName $DistinguishedName
    }

    # -------------------------------------------------------------------------
    # Existing keystore credentials
    # -------------------------------------------------------------------------

    if (-not (Get-Variable -Name Alias -ErrorAction SilentlyContinue)) {
        $Alias = Read-Retry-Or-Cancel `
            -Prompt 'Enter the existing keystore alias' `
            -Validator {
            param($value)

            if ($value -notmatch '^[A-Za-z0-9._-]{1,64}$') {
                throw 'Invalid keystore alias.'
            }

            return $true
        } `
            -ValidationMessage `
            'Invalid keystore alias.'
    }

    if (-not (Get-Variable -Name StorePassword -ErrorAction SilentlyContinue)) {
        $StorePassword = Read-Retry-Or-Cancel `
            -Prompt 'Enter the existing keystore password' `
            -Secret `
            -Validator {
            param($value)

            return -not [string]::IsNullOrWhiteSpace($value)
        } `
            -ValidationMessage `
            'Keystore password cannot be empty.'
    }

    if (-not (Get-Variable -Name KeyPassword -ErrorAction SilentlyContinue)) {
        $KeyPassword = Read-Retry-Or-Cancel `
            -Prompt 'Enter the existing key password' `
            -Secret `
            -Validator {
            param($value)

            return -not [string]::IsNullOrWhiteSpace($value)
        } `
            -ValidationMessage `
            'Key password cannot be empty.'
    }

    # -------------------------------------------------------------------------
    # Validate keystore credentials before publishing
    # -------------------------------------------------------------------------

    Write-Section 'Validating signing credentials'

    $keytoolArguments = @(
        '-list'
        '-v'
        '-keystore'
        $KeystorePath
        '-alias'
        $Alias
        '-storepass'
        $StorePassword
        '-keypass'
        $KeyPassword
    )

    Invoke-CommandChecked `
        -Command 'keytool' `
        -Arguments $keytoolArguments

    Write-Log 'Keystore credentials validated successfully.' -Level Success

    # -------------------------------------------------------------------------
    # Publishing metadata
    # -------------------------------------------------------------------------

    Write-Section 'Saving publishing metadata'

    $publishingConfiguration = @{
        Alias             = $Alias
        StorePassword     = $StorePassword
        KeyPassword       = $KeyPassword
        KeystorePath      = $KeystorePath
        DistinguishedName = if (
            Get-Variable -Name DistinguishedName -ErrorAction SilentlyContinue
        ) {
            $DistinguishedName
        }
        else {
            'Existing certificate'
        }
    }

    Write-PublishingInfo `
        -Path $PublishingInfoPath `
        -Configuration $publishingConfiguration

    # -------------------------------------------------------------------------
    # Backup
    # -------------------------------------------------------------------------

    $backupPath = Backup-SigningAssets `
        -ApplicationRoot $ApplicationRoot `
        -KeystorePath $KeystorePath `
        -PublishingInfoPath $PublishingInfoPath

    # -------------------------------------------------------------------------
    # Build
    # -------------------------------------------------------------------------

    $publishRoot = Publish-Android `
        -KeystorePath $KeystorePath `
        -Alias $Alias `
        -StorePassword $StorePassword `
        -KeyPassword $KeyPassword

    # -------------------------------------------------------------------------
    # Find artifacts
    # -------------------------------------------------------------------------

    Write-Section 'Locating generated Android artifacts'

    $artifacts = Find-AndroidArtifacts

    if ($null -eq $artifacts.Aab) {
        throw @"
The Android publish completed, but no AAB was found.

Expected search root:
$($artifacts.Root)
"@
    }

    Write-Log "AAB: $($artifacts.Aab.FullName)" -Level Success

    if ($null -ne $artifacts.Apk) {
        Write-Log "APK: $($artifacts.Apk.FullName)" -Level Success
    }
    else {
        Write-Log `
            'No APK was found in the publish output. The AAB was generated successfully.' `
            -Level Warning
    }

    # -------------------------------------------------------------------------
    # Store release
    # -------------------------------------------------------------------------

    $release = Store-ReleaseArtifacts `
        -ApplicationRoot $ApplicationRoot `
        -Aab $artifacts.Aab `
        -Apk $artifacts.Apk `
        -PublishingInfoPath $PublishingInfoPath

    # -------------------------------------------------------------------------
    # Final report
    # -------------------------------------------------------------------------

    Write-Section 'Publishing assets'

    Write-Host "Application" -ForegroundColor Cyan
    Write-Host "  $ApplicationName"

    Write-Host ''
    Write-Host "Application ID" -ForegroundColor Cyan
    Write-Host "  $ApplicationId"

    Write-Host ''
    Write-Host "Signing repository" -ForegroundColor Cyan
    Write-Host "  $SigningRoot"

    Write-Host ''
    Write-Host "Application assets" -ForegroundColor Cyan
    Write-Host "  $ApplicationRoot"

    Write-Host ''
    Write-Host "Keystore" -ForegroundColor Cyan
    Write-Host "  $KeystorePath"

    Write-Host ''
    Write-Host "Publishing information / password" -ForegroundColor Cyan
    Write-Host "  $PublishingInfoPath"

    Write-Host ''
    Write-Host "Backup" -ForegroundColor Cyan
    Write-Host "  $backupPath"

    Write-Host ''
    Write-Host "AAB - Google Play Console" -ForegroundColor Cyan
    Write-Host "  $($artifacts.Aab.FullName)"

    if ($null -ne $artifacts.Apk) {
        Write-Host ''
        Write-Host "APK - device/testing" -ForegroundColor Cyan
        Write-Host "  $($artifacts.Apk.FullName)"
    }

    Write-Host ''
    Write-Host "Release assets" -ForegroundColor Cyan
    Write-Host "  $($release.Path)"

    Write-Host ''
    Write-Host ('-' * 78) -ForegroundColor DarkCyan

    Write-Log `
        'Android publishing preparation completed successfully.' `
        -Level Success

    Write-Log `
        'The AAB is the artifact intended for Google Play Console.' `
        -Level Success

    Write-Log `
        "Signing repository: $ApplicationRoot" `
        -Level Info

    Write-Log `
        "Environment variable: $SigningRootEnvironmentVariable" `
        -Level Info

    Write-Host ''
}
catch {
    Write-Host ''
    Write-Log $_.Exception.Message -Level Error
    Write-Host ''

    Write-Log `
        'The operation was not completed. No existing signing keystore was overwritten automatically.' `
        -Level Warning

    exit 1
}