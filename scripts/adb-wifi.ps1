# ==============================================================================
# Script: adb-wifi-switch.ps1
# Finalidade: Alternar conexao de dispositivo Android fisico USB para Wi-Fi via ADB
# Observacao: Comentarios em PT-BR; saida do terminal em EN-US sem acentos.
# ==============================================================================

# Forcar cultura e codificacao para en-US (evita acentos ou problemas de charset no output)
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
[System.Threading.Thread]::CurrentThread.CurrentUICulture = [System.Globalization.CultureInfo]::InvariantCulture
[Console]::OutputEncoding = [System.Text.Encoding]::ASCII

Clear-Host

# Funcoes auxiliares para logs coloridos
function Log-Info ($msg) {
    Write-Host "[INFO] $msg" -ForegroundColor Cyan
}

function Log-Success ($msg) {
    Write-Host "[SUCCESS] $msg" -ForegroundColor Green
}

function Log-Warn ($msg) {
    Write-Host "[WARN] $msg" -ForegroundColor Yellow
}

function Log-Error ($msg) {
    Write-Host "[ERROR] $msg" -ForegroundColor Red
}

# 1. Verifica se o executavel adb esta disponivel no PATH do sistema
$adbCmd = Get-Command "adb" -ErrorAction SilentlyContinue
if (-not $adbCmd) {
    Log-Error "ADB command not found in PATH. Please install Android Platform Tools."
    exit 1
}

Log-Info "Scanning for physical USB devices..."

# 2. Executa 'adb devices -l' para listar dispositivos detalhados
$rawDevices = adb devices -l

# Filtrar linhas: remover cabecalho ('List of devices attached'), linhas vazias e daemons
$lines = $rawDevices | Where-Object { 
    $_ -match '\bdevice\b' -and 
    $_ -notmatch '^List of devices' -and 
    $_ -notmatch '\* daemon' 
}

$physicalDevices = @()

# 3. Filtrar somente dispositivos fisicos via USB (exclui emuladores e conexoes TCP/IP)
foreach ($line in $lines) {
    $parts = $line -split '\s+'
    $serial = $parts[0]

    # Ignora emuladores padrao (emulator-5554, etc.)
    if ($serial -like "emulator-*") { continue }

    # Ignora conexoes que ja sao por endereco IP (IP:porta)
    if ($serial -match '^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}:\d+$') { continue }

    # Extrai informacoes de modelo para exibicao amigavel
    $model = "Unknown Model"
    if ($line -match 'model:([^\s]+)') { $model = $matches[1] }
    
    $product = ""
    if ($line -match 'product:([^\s]+)') { $product = " (" + $matches[1] + ")" }

    $physicalDevices += [PSCustomObject]@{
        Serial      = $serial
        Model       = $model
        DisplayName = "$model$product - Serial: $serial"
    }
}

# Se nenhum dispositivo fisico via USB for encontrado, encerra a execucao
if ($physicalDevices.Count -eq 0) {
    Log-Warn "No physical USB devices detected."
    Log-Info "Make sure USB Debugging is enabled and your device is plugged in."
    exit 0
}

# 4. Menu interativo com navegacao por setas do teclado
function Show-InteractiveMenu ($items) {
    $selectedIndex = 0
    $total = $items.Count

    # Oculta o cursor durante a selecao interativa
    [Console]::CursorVisible = $false

    try {
        while ($true) {
            Clear-Host
            Write-Host "=========================================" -ForegroundColor DarkGray
            Write-Host "  ANDROID DEVICE SELECTOR (ADB WI-FI)    " -ForegroundColor Cyan
            Write-Host "=========================================" -ForegroundColor DarkGray
            Write-Host "Use [UP] and [DOWN] arrows to navigate, [ENTER] to select, [Q] to quit.`n" -ForegroundColor Gray

            for ($i = 0; $i -lt $total; $i++) {
                if ($i -eq $selectedIndex) {
                    Write-Host " > [$($items[$i].DisplayName)]" -ForegroundColor Black -BackgroundColor Cyan
                } else {
                    Write-Host "   $($items[$i].DisplayName)" -ForegroundColor White
                }
            }

            $key = [Console]::ReadKey($true)

            switch ($key.Key) {
                'UpArrow' {
                    $selectedIndex--
                    if ($selectedIndex -lt 0) { $selectedIndex = $total - 1 }
                }
                'DownArrow' {
                    $selectedIndex++
                    if ($selectedIndex -ge $total) { $selectedIndex = 0 }
                }
                'Enter' {
                    return $items[$selectedIndex]
                }
                'Q' {
                    Write-Host "`nOperation cancelled by user." -ForegroundColor Yellow
                    exit 0
                }
                'Escape' {
                    Write-Host "`nOperation cancelled by user." -ForegroundColor Yellow
                    exit 0
                }
            }
        }
    }
    finally {
        # Restaura o cursor antes de sair
        [Console]::CursorVisible = $true
    }
}

# Seleciona o dispositivo caso haja multiplos, ou seleciona automaticamente se houver apenas 1
if ($physicalDevices.Count -eq 1) {
    $selectedDevice = $physicalDevices[0]
    Log-Info "Only one physical device detected: $($selectedDevice.DisplayName)"
} else {
    $selectedDevice = Show-InteractiveMenu -items $physicalDevices
}

Clear-Host
Log-Info "Selected target device: $($selectedDevice.DisplayName)"

# 5. Obtencao do endereco IP da interface Wi-Fi do aparelho selecionado
Log-Info "Querying device Wi-Fi IP address..."

# Tentativa 1: Via comando ip route (porta de saida da interface wlan0)
$deviceIp = (adb -s $selectedDevice.Serial shell "ip route | grep -oE 'src [0-9]+\.[0-9]+\.[0-9]+\.[0-9]+' | cut -d ' ' -f 2" 2>$null).Trim()

# Tentativa 2: Fallback via ip addr show wlan0 caso a rota retorne vazia
if (-not $deviceIp -or $deviceIp -notmatch '^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$') {
    $ipOutput = adb -s $selectedDevice.Serial shell "ip -f inet addr show wlan0" 2>$null
    if ($ipOutput -match 'inet\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)/') {
        $deviceIp = $matches[1]
    }
}

# Validacao do IP obtido
if (-not $deviceIp -or $deviceIp -notmatch '^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$') {
    Log-Error "Could not retrieve Wi-Fi IP address."
    Log-Warn "Please check if the device is connected to the same Wi-Fi network as this PC."
    exit 1
}

Log-Success "Found device IP address: $deviceIp"

# 6. Reiniciar o daemon ADB do dispositivo em modo TCP/IP na porta 5555
$tcpPort = "5555"
Log-Info "Configuring device to listen on TCP/IP port $tcpPort..."
$tcpResult = adb -s $selectedDevice.Serial tcpip $tcpPort

if ($LASTEXITCODE -ne 0) {
    Log-Error "Failed to switch ADB to TCP/IP mode."
    exit 1
}

Log-Success "ADB daemon restarted in TCP mode on port $tcpPort."

# 7. Aguardar propagacao e realizar a conexao via Wi-Fi
Log-Info "Waiting 2 seconds for TCP socket to initialize..."
Start-Sleep -Seconds 2

Log-Info "Connecting to $deviceIp`:$tcpPort..."
$connectResult = adb connect "$deviceIp`:$tcpPort"

if ($connectResult -match "connected to") {
    Log-Success "Successfully connected to $deviceIp`:$tcpPort over Wi-Fi!"
    Write-Host ""
    Log-Info "You can now safely disconnect the USB cable."
} else {
    Log-Error "Failed to connect over Wi-Fi: $connectResult"
    Log-Warn "Ensure no AP isolation is enabled on your router and firewall allows port 5555."
    exit 1
}