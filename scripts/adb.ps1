# ==============================================================================
# Script: adb-toolkit.ps1
# Finalidade: CLI interativa para gestao de dispositivos fisicos e AVDs via ADB
# Observacao: Comentarios em PT-BR; saida do terminal em EN-US sem acentos.
# ==============================================================================

# Definir cultura invariant e encoding compativel
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
[System.Threading.Thread]::CurrentThread.CurrentUICulture = [System.Globalization.CultureInfo]::InvariantCulture
[Console]::OutputEncoding = [System.Text.Encoding]::ASCII

# Funcoes de logging colorido
function Log-Info ($msg)    { Write-Host "[INFO] $msg" -ForegroundColor Cyan }
function Log-Success ($msg) { Write-Host "[SUCCESS] $msg" -ForegroundColor Green }
function Log-Warn ($msg)    { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Log-Error ($msg)   { Write-Host "[ERROR] $msg" -ForegroundColor Red }

# 1. Localizacao de binarios (ADB e Emulator)
$adbCmd = Get-Command "adb" -ErrorAction SilentlyContinue
if (-not $adbCmd) {
    Log-Error "ADB command not found in PATH. Please install Android Platform Tools."
    exit 1
}

# Tenta localizar o binario do emulador no PATH ou no ANDROID_HOME / ANDROID_SDK_ROOT
$emulatorCmd = Get-Command "emulator" -ErrorAction SilentlyContinue
if (-not $emulatorCmd) {
    $sdkPaths = @(
        $env:ANDROID_HOME,
        $env:ANDROID_SDK_ROOT,
        "$env:LOCALAPPDATA\Android\Sdk"
    )
    foreach ($sdk in $sdkPaths) {
        if ($sdk -and (Test-Path "$sdk\emulator\emulator.exe")) {
            $emulatorCmd = "$sdk\emulator\emulator.exe"
            break
        }
    }
}

# Funcao para pausar o console antes de redesenhar a tela
function Pause-Screen {
    Write-Host "`nPress any key to return to menu..." -ForegroundColor DarkGray
    [Console]::ReadKey($true) | Out-Null
}

# 2. Coleta de dados dos dispositivos conectados
function Get-AdbDeviceList {
    $raw = adb devices -l
    $lines = $raw | Where-Object { 
        $_ -match '\S' -and 
        $_ -notmatch '^List of devices attached' -and 
        $_ -notmatch '^\* daemon' 
    }

    $devices = @()
    foreach ($line in $lines) {
        $parts = $line -split '\s+'
        $serial = $parts[0]
        $state  = $parts[1]

        # Determina o tipo e o modo de conexao
        $type = "USB Physical"
        if ($serial -like "emulator-*") {
            $type = "Emulator"
        } elseif ($serial -match '^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}:\d+$') {
            $type = "Wi-Fi (TCP/IP)"
        }

        $model = "Generic Device"
        if ($line -match 'model:([^\s]+)') { $model = $matches[1] }

        $devices += [PSCustomObject]@{
            Serial = $serial
            State  = $state
            Type   = $type
            Model  = $model
            Raw    = $line
        }
    }
    return $devices
}

# 3. Componente de menu interativo generico com setas do teclado
function Show-InteractiveMenu ($title, $items, [string]$displayProp = "") {
    if ($items.Count -eq 0) { return $null }

    $selectedIndex = 0
    $total = $items.Count
    [Console]::CursorVisible = $false

    try {
        while ($true) {
            Clear-Host
            Write-Host "===========================================================" -ForegroundColor DarkGray
            Write-Host "  $title" -ForegroundColor Cyan
            Write-Host "===========================================================" -ForegroundColor DarkGray
            Write-Host "Navigate: [UP/DOWN] | Confirm: [ENTER] | Cancel: [ESC/Q]`n" -ForegroundColor Gray

            for ($i = 0; $i -lt $total; $i++) {
                $itemText = if ($displayProp) { $items[$i].$displayProp } else { $items[$i] }

                if ($i -eq $selectedIndex) {
                    Write-Host " > $itemText " -ForegroundColor Black -BackgroundColor Cyan
                } else {
                    Write-Host "   $itemText" -ForegroundColor White
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
                'Escape' { return $null }
                'Q'      { return $null }
            }
        }
    }
    finally {
        [Console]::CursorVisible = $true
    }
}

# 4. Acoes da Ferramenta

# Alterna dispositivo fisico USB para Wi-Fi
function Switch-DeviceToWifi {
    $devices = Get-AdbDeviceList | Where-Object { $_.Type -eq "USB Physical" -and $_.State -eq "device" }
    if ($devices.Count -eq 0) {
        Log-Warn "No active physical USB devices available."
        Pause-Screen
        return
    }

    $target = Show-InteractiveMenu "SELECT USB DEVICE TO SWITCH TO WI-FI" $devices "Serial"
    if (-not $target) { return }

    Clear-Host
    Log-Info "Querying device IP on $($target.Serial)..."

    # Tentativa 1: Rota padrao wlan0
    $ip = (adb -s $target.Serial shell "ip route | grep -oE 'src [0-9]+\.[0-9]+\.[0-9]+\.[0-9]+' | cut -d ' ' -f 2" 2>$null).Trim()
    
    # Tentativa 2: Fallback por interface direta
    if (-not $ip -or $ip -notmatch '^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$') {
        $out = adb -s $target.Serial shell "ip -f inet addr show wlan0" 2>$null
        if ($out -match 'inet\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)/') { $ip = $matches[1] }
    }

    if (-not $ip -or $ip -notmatch '^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$') {
        Log-Error "Could not retrieve Wi-Fi IP address. Ensure device is on the local network."
        Pause-Screen
        return
    }

    Log-Info "Device IP: $ip"
    Log-Info "Restarting ADB daemon in TCP mode (port 5555)..."
    adb -s $target.Serial tcpip 5555 | Out-Null
    Start-Sleep -Seconds 2

    Log-Info "Connecting to $ip`:5555..."
    $res = adb connect "$ip`:5555"

    if ($res -match "connected to") {
        Log-Success "Connected successfully! You may now disconnect the USB cable."
    } else {
        Log-Error "Failed to connect: $res"
    }
    Pause-Screen
}

# Retorna modo do dispositivo para USB
function Switch-DeviceToUsb {
    $devices = Get-AdbDeviceList | Where-Object { $_.State -eq "device" }
    if ($devices.Count -eq 0) {
        Log-Warn "No active devices available."
        Pause-Screen
        return
    }

    $target = Show-InteractiveMenu "SELECT DEVICE TO RETURN TO USB MODE" $devices "Serial"
    if (-not $target) { return }

    Clear-Host
    Log-Info "Setting ADB mode to USB for $($target.Serial)..."
    adb -s $target.Serial usb
    Pause-Screen
}

# Conectar manualmente por IP:Porta
function Connect-ManualIp {
    Clear-Host
    Write-Host "=== CONNECT BY IP:PORT ===" -ForegroundColor Cyan
    $ip = Read-Host "Enter device IP address (e.g. 192.168.1.50)"
    if ([string]::IsNullOrWhiteSpace($ip)) { return }

    $port = Read-Host "Enter port [Default: 5555]"
    if ([string]::IsNullOrWhiteSpace($port)) { $port = "5555" }

    Log-Info "Attempting to connect to $ip`:$port..."
    $res = adb connect "$ip`:$port"
    Write-Host $res
    Pause-Screen
}

# Desconectar dispositivos Wi-Fi
function Disconnect-WifiDevices {
    $devices = Get-AdbDeviceList | Where-Object { $_.Type -eq "Wi-Fi (TCP/IP)" }
    if ($devices.Count -eq 0) {
        Log-Warn "No active Wi-Fi connections detected."
        Pause-Screen
        return
    }

    $options = @($devices.Serial) + @("[Disconnect ALL Wi-Fi devices]")
    $choice = Show-InteractiveMenu "SELECT DEVICE TO DISCONNECT" $options

    if (-not $choice) { return }

    Clear-Host
    if ($choice -eq "[Disconnect ALL Wi-Fi devices]") {
        Log-Info "Disconnecting all wireless connections..."
        adb disconnect
    } else {
        Log-Info "Disconnecting $choice..."
        adb disconnect $choice
    }
    Pause-Screen
}

# Listar e iniciar Emulador (AVD)
function Start-AndroidEmulator {
    if (-not $emulatorCmd) {
        Log-Error "Android 'emulator' binary not found. Check ANDROID_HOME/PATH."
        Pause-Screen
        return
    }

    $avds = & $emulatorCmd -list-avds
    if (-not $avds -or $avds.Count -eq 0) {
        Log-Warn "No installed AVDs found."
        Log-Info "Create an AVD via Android Studio AVD Manager or 'avdmanager' tool."
        Pause-Screen
        return
    }

    $targetAvd = Show-InteractiveMenu "SELECT AVD TO LAUNCH" $avds
    if (-not $targetAvd) { return }

    Clear-Host
    Log-Info "Starting emulator: $targetAvd in background..."
    Start-Process -FilePath $emulatorCmd -ArgumentList "-avd `"$targetAvd`"" -WindowStyle Minimized
    Log-Success "Emulator launched. It will appear on the device list once booted."
    Pause-Screen
}

# Encerrar Emulador em execucao
function Stop-AndroidEmulator {
    $emulators = Get-AdbDeviceList | Where-Object { $_.Type -eq "Emulator" }
    if ($emulators.Count -eq 0) {
        Log-Warn "No running emulators detected."
        Pause-Screen
        return
    }

    $target = Show-InteractiveMenu "SELECT EMULATOR TO SHUT DOWN" $emulators "Serial"
    if (-not $target) { return }

    Clear-Host
    Log-Info "Shutting down $($target.Serial)..."
    adb -s $target.Serial emu kill
    Log-Success "Shutdown signal sent."
    Pause-Screen
}

# Captura de tela do aparelho salva no computador
function Take-DeviceScreenshot {
    $devices = Get-AdbDeviceList | Where-Object { $_.State -eq "device" }
    if ($devices.Count -eq 0) {
        Log-Warn "No active devices available."
        Pause-Screen
        return
    }

    $target = Show-InteractiveMenu "SELECT DEVICE FOR SCREENSHOT" $devices "Serial"
    if (-not $target) { return }

    $fileName = "screenshot_$(Get-Date -Format 'yyyyMMdd_HHmmss').png"
    Clear-Host
    Log-Info "Taking screenshot on $($target.Serial)..."
    adb -s $target.Serial exec-out screencap -p > $fileName

    if (Test-Path $fileName) {
        Log-Success "Screenshot saved to current folder: $fileName"
    } else {
        Log-Error "Failed to capture screenshot."
    }
    Pause-Screen
}

# Stream de logcat em tempo real
function Show-LiveLogcat {
    $devices = Get-AdbDeviceList | Where-Object { $_.State -eq "device" }
    if ($devices.Count -eq 0) {
        Log-Warn "No active devices available."
        Pause-Screen
        return
    }

    $target = Show-InteractiveMenu "SELECT DEVICE FOR LOGCAT" $devices "Serial"
    if (-not $target) { return }

    Clear-Host
    Log-Info "Clearing logcat buffer on $($target.Serial)..."
    adb -s $target.Serial logcat -c
    Log-Info "Starting live logcat stream. Press [CTRL+C] to stop streaming and return."
    Start-Sleep -Seconds 1

    try {
        adb -s $target.Serial logcat -v color
    } catch {
        # Interrupcao normal via CTRL+C
    }
    Pause-Screen
}

# Reiniciar servidor local do ADB
function Restart-AdbServer {
    Clear-Host
    Log-Info "Killing ADB server..."
    adb kill-server
    Log-Info "Starting ADB server..."
    adb start-server
    Log-Success "ADB Server reinitialized."
    Pause-Screen
}

# 5. Loop Principal da CLI
$mainMenuItems = @(
    "1. Switch Physical USB Device to Wi-Fi",
    "2. Switch Device back to USB Mode",
    "3. Connect Manually by IP:Port",
    "4. Disconnect Wi-Fi Device(s)",
    "5. Launch Android Emulator (AVD)",
    "6. Kill Running Emulator",
    "7. Take Screenshot from Device",
    "8. View Live Logcat Stream",
    "9. Restart ADB Server",
    "0. Exit"
)

$selectedIndex = 0
$totalItems = $mainMenuItems.Count
[Console]::CursorVisible = $false

try {
    while ($true) {
        Clear-Host

        # Cabecalho da CLI
        Write-Host "================================================================================" -ForegroundColor Cyan
        Write-Host "                           ANDROID ADB TOOLKIT CLI                              " -ForegroundColor Black -BackgroundColor Cyan
        Write-Host "================================================================================" -ForegroundColor Cyan

        # Dashboard de Dispositivos e Status
        $deviceList = Get-AdbDeviceList
        Write-Host "`n[ATTACHED DEVICES & STATUS]" -ForegroundColor Yellow

        if ($deviceList.Count -eq 0) {
            Write-Host "  No devices found. Connect via USB or start an emulator.`n" -ForegroundColor DarkGray
        } else {
            $format = "  {0,-20} {1,-14} {2,-16} {3}"
            Write-Host ($format -f "SERIAL / IP", "STATE", "TYPE", "MODEL") -ForegroundColor DarkGray
            Write-Host ($format -f "-----------", "-----", "----", "-----") -ForegroundColor DarkGray

            foreach ($dev in $deviceList) {
                $color = "Green"
                if ($dev.State -ne "device") { $color = "Red" }
                elseif ($dev.Type -eq "Wi-Fi (TCP/IP)") { $color = "Magenta" }
                elseif ($dev.Type -eq "Emulator") { $color = "Yellow" }

                Write-Host ($format -f $dev.Serial, $dev.State, $dev.Type, $dev.Model) -ForegroundColor $color
            }
            Write-Host ""
        }

        # Renderizacao do Menu Interativo
        Write-Host "================================================================================" -ForegroundColor DarkGray
        Write-Host "MENU ACTIONS [Use UP/DOWN arrows, ENTER to select, Q to quit]:" -ForegroundColor Gray
        Write-Host "================================================================================" -ForegroundColor DarkGray

        for ($i = 0; $i -lt $totalItems; $i++) {
            if ($i -eq $selectedIndex) {
                Write-Host " > $($mainMenuItems[$i]) " -ForegroundColor Black -BackgroundColor Cyan
            } else {
                Write-Host "   $($mainMenuItems[$i])" -ForegroundColor White
            }
        }

        # Captura de Teclas
        $key = [Console]::ReadKey($true)
        switch ($key.Key) {
            'UpArrow' {
                $selectedIndex--
                if ($selectedIndex -lt 0) { $selectedIndex = $totalItems - 1 }
            }
            'DownArrow' {
                $selectedIndex++
                if ($selectedIndex -ge $totalItems) { $selectedIndex = 0 }
            }
            'Q' {
                Clear-Host
                Log-Info "Exiting ADB Toolkit."
                exit 0
            }
            'Enter' {
                switch ($selectedIndex) {
                    0 { Switch-DeviceToWifi }
                    1 { Switch-DeviceToUsb }
                    2 { Connect-ManualIp }
                    3 { Disconnect-WifiDevices }
                    4 { Start-AndroidEmulator }
                    5 { Stop-AndroidEmulator }
                    6 { Take-DeviceScreenshot }
                    7 { Show-LiveLogcat }
                    8 { Restart-AdbServer }
                    9 {
                        Clear-Host
                        Log-Info "Exiting ADB Toolkit."
                        exit 0
                    }
                }
            }
        }
    }
}
finally {
    [Console]::CursorVisible = $true
}