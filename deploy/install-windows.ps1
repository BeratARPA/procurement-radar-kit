# Registers Procurement Radar to start when you log in, and reports anything
# that would stop it from working. Run it from the repository root.
#
# A scheduled task rather than a Windows service, and the reason is Docker: on
# Windows the engine lives inside Docker Desktop, which needs a user session and
# starts at sign-in, not at boot. A service would start earlier, find no
# database, and retry into an empty room. Logging in is what actually brings this
# machine up, so that is what the worker hangs off.

[CmdletBinding()]
param(
    [string] $TaskName = 'ProcurementRadar',
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publish = Join-Path $repo 'publish'

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Removed the scheduled task '$TaskName'."
    return
}

# --- publish -----------------------------------------------------------------
Write-Host 'Publishing the worker...'
& dotnet publish (Join-Path $repo 'src\ProcurementRadar.Worker') -c Release -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# The worker reads config/*.json at run time and finds it by walking up from its
# own directory, so a copy has to sit beside the published files.
Copy-Item (Join-Path $repo 'config') (Join-Path $publish 'config') -Recurse -Force

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
Write-Host 'Start it now without waiting for a logon:'
Write-Host "    Start-ScheduledTask -TaskName $TaskName"
Write-Host 'Watch it:'
Write-Host "    Get-Content '$publish\logs\procurement-radar-*.log' -Tail 20 -Wait"
