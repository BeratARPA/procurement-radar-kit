# Registers Procurement Radar to start when you log in, and reports anything
# that would stop it from working. Run it from the repository root.
#
# A scheduled task rather than a Windows service, and the reason is Docker: on
# Windows the engine lives inside Docker Desktop, which needs a user session and
# starts at sign-in, not at boot. A service would start earlier, find no
# database, and retry into an empty room. Logging in is what actually brings this
# machine up, so that is what the worker hangs off.

#   .\deploy\install-windows.ps1              kur (ilk sefer yayınlar)
#   .\deploy\install-windows.ps1 -Uninstall    kaldır
#   .\deploy\install-windows.ps1 -Status       durum
#
# Kurmak ve kaldırmak tek komut ve saniyeler sürer. Yayınlama zaten yapılmışsa
# tekrar kurulum onu atlar, yani fikir değiştirmek ucuz - bu kasıtlı.

[CmdletBinding()]
param(
    [string] $TaskName = 'ProcurementRadar',
    [switch] $Uninstall,
    [switch] $Status,
    [switch] $Rebuild
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publish = Join-Path $repo 'publish'

if ($Uninstall) {
    # Stop first: an unregister while it is mid-poll leaves the process orphaned.
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Get-Process ProcurementRadar.Worker -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Host "Kaldirildi: '$TaskName'. Geri kurmak icin ayni betigi parametresiz calistirin."
    return
}

if ($Status) {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $task) { Write-Host "Kurulu degil."; return }
    $info = Get-ScheduledTaskInfo -TaskName $TaskName
    Write-Host "Durum      : $($task.State)"
    Write-Host "Son calisma: $($info.LastRunTime)"
    Write-Host "Son sonuc  : $($info.LastTaskResult)  (0 = sorunsuz, 267009 = su an calisiyor)"
    Write-Host "Sonraki    : $($info.NextRunTime)"
    $running = Get-Process ProcurementRadar.Worker -ErrorAction SilentlyContinue
    Write-Host "Surec      : $(if ($running) { "calisiyor, pid $($running.Id)" } else { 'yok' })"
    return
}

# --- publish -----------------------------------------------------------------
# Skipped when it is already there, so re-installing after an uninstall is
# instant. -Rebuild forces it after a code change.
$worker = Join-Path $publish 'ProcurementRadar.Worker.exe'

if ($Rebuild -or -not (Test-Path $worker)) {
    Write-Host 'Yayinlaniyor...'
    & dotnet publish (Join-Path $repo 'src\ProcurementRadar.Worker') -c Release -o $publish --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    # The worker reads config/*.json at run time and finds it by walking up from
    # its own directory, so a copy has to sit beside the published files.
    Copy-Item (Join-Path $repo 'config') (Join-Path $publish 'config') -Recurse -Force
} else {
    Write-Host 'Yayin zaten var, atlaniyor. Kod degistiyse: -Rebuild'
}

# --- the browser EKAP needs ---------------------------------------------------
$browsers = Join-Path $repo '.playwright'
if (-not (Test-Path $browsers)) {
    Write-Host 'Installing Chromium for the EKAP sources (about 150 MB, once)...'
    $env:PLAYWRIGHT_BROWSERS_PATH = $browsers
    & (Join-Path $publish 'playwright.ps1') install chromium
}

# --- the task -----------------------------------------------------------------
$action = New-ScheduledTaskAction `
    -Execute (Join-Path $publish 'ProcurementRadar.Worker.exe') `
    -WorkingDirectory $publish

# At logon, because that is when Docker Desktop starts. The delay gives the
# engine time to come up; the worker survives a database that is not ready yet,
# but there is no reason to make it spend its first minute failing.
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$trigger.Delay = 'PT2M'

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartInterval (New-TimeSpan -Minutes 5) `
    -RestartCount 999 `
    -ExecutionTimeLimit ([TimeSpan]::Zero)

# On a laptop the battery settings are not a detail. Without them Windows stops
# the task the moment the charger comes out, which is exactly when nobody is
# watching.

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Settings $settings -Description 'Procurement Radar worker' -Force | Out-Null

Write-Host "Registered '$TaskName' to start at logon."

# --- what would still stop it -------------------------------------------------
Write-Host ''
Write-Host 'Checks:'

$env = Join-Path $repo '.env'
if (Test-Path $env) {
    $missing = @()
    foreach ($name in 'PR_DB_CONNECTION_STRING', 'PR_DATA_DIR', 'PR_CONTACT_EMAIL') {
        if (-not (Select-String -Path $env -Pattern "^$name=.+" -Quiet)) { $missing += $name }
    }
    if ($missing) { Write-Host "  MISSING in .env: $($missing -join ', ')" } else { Write-Host '  .env: required values present' }

    if (-not (Select-String -Path $env -Pattern '^PR_TELEGRAM_BOT_TOKEN=.+' -Quiet)) {
        Write-Host '  Telegram: no token, so nothing will be notified. Collection and health still run.'
    }
} else {
    Write-Host '  MISSING: .env (copy .env.example and fill it in)'
}

$sleep = (powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 2>$null | Out-String)
if ($sleep -match 'Current AC Power Setting Index:\s*0x00000000') {
    Write-Host '  Sleep on AC: off. Good.'
} else {
    Write-Host '  Sleep on AC is ON. A sleeping laptop collects nothing, and some listings'
    Write-Host '  keep only a few days of history. To turn it off:'
    Write-Host '      powercfg /change standby-timeout-ac 0'
    Write-Host '      powercfg /change hibernate-timeout-ac 0'
    Write-Host '  Closing the lid also sleeps it unless you change that in Control Panel.'
}

Write-Host ''
Write-Host '--- gunluk kullanim ---'
Write-Host "  Baslat  : Start-ScheduledTask -TaskName $TaskName"
Write-Host "  Durdur  : Stop-ScheduledTask -TaskName $TaskName"
Write-Host "  Durum   : .\deploy\install-windows.ps1 -Status"
Write-Host "  Kaldir  : .\deploy\install-windows.ps1 -Uninstall"
Write-Host "  Geri kur: .\deploy\install-windows.ps1"
Write-Host "  Izle    : Get-Content '$publish\logs\procurement-radar-*.log' -Tail 20 -Wait"
