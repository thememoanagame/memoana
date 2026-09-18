$isElevated = if ($IsWindows -or $PSVersionTable.PSVersion.Major -lt 6) {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} else {
    (id -u) -eq 0
}

if (-not $isElevated) {
    if ($IsWindows -or $PSVersionTable.PSVersion.Major -lt 6) {
        Write-Host "Elevating for Administrator permissions..." -ForegroundColor Yellow
        
        $processInfo = New-Object System.Diagnostics.ProcessStartInfo
        $processInfo.FileName = "pwsh.exe" # Use "powershell.exe" se estiver usando o Windows PowerShell 5.1
        $processInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
        $processInfo.Verb = "RunAs"
        
        try {
            [System.Diagnostics.Process]::Start($processInfo) | Out-Null
        } catch {
            Write-Error "Privilege elevation was cancelled."
        }
        exit
    } else {
        Write-Host "Needs sudo..." -ForegroundColor Yellow
        
        exec sudo pwsh -NoProfile -ExecutionPolicy Bypass -File "$PSCommandPath"
        exit
    }
}

# Solicita os dados do certificado
$subjectName = Read-Host "CN (e.g.: CN=ayllu.local)"
$validYears = Read-Host "Digite a validade em anos (ex: 2)"
$pfxPassword = Read-Host "Digite a senha para proteger o arquivo PFX"
$certName = Read-Host "Digite o nome do arquivo"
$dns = $subjectName -replace "CN=", ""
$rootFolder = Resolve-Path "$PSScriptRoot\..\"
$certFolder = Join-Path "$rootFolder\certs"
New-Item -ItemType Directory -Path $certFolder -Force | Out-Null
$cerPath = "$certFolder\$certName.cer"
$pfxPath = "$certFolder\$certName.pfx"
$crtPath = "$certFolder\$certName.crt"

# Cria o certificado autoassinado
$cert = New-SelfSignedCertificate `
    -Subject $subjectName `
    -DnsName $dns, "localhost", "127.0.0.1" `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -KeyExportPolicy Exportable `
    -KeySpec Signature `
    -KeyLength 2048 `
    -HashAlgorithm sha256 `
    -NotAfter (Get-Date).AddYears([int]$validYears)

# Exporta oz certificadoz (.cer, .pfx, .crt)
Export-Certificate -Cert $cert -FilePath $cerPath
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password (ConvertTo-SecureString -String $pfxPassword -Force -AsPlainText)
Export-Certificate -Cert $cert -FilePath $crtPath

# Converter .crt binário (DER) -> PEM (Base64)
$bytes = [System.IO.File]::ReadAllBytes($crtPath)
$base64 = [System.Convert]::ToBase64String($bytes, [System.Base64FormattingOptions]::InsertLineBreaks)
$pemContent = "-----BEGIN CERTIFICATE-----`n$base64`n-----END CERTIFICATE-----"
[System.IO.File]::WriteAllText($crtPath, $pemContent)

# Adiciona o certificado na loja de Autoridades Confiaveis
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root","LocalMachine")
$store.Open("ReadWrite")
$store.Add($cert)
$store.Close()

Write-Host "Certificado gerado e registrado com sucesso!"
Write-Host "Arquivos salvos em: $certFolder"
