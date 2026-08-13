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
    foreach ($name in $TaskName, ($TaskName + 'Api')) {
        Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    }
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
    # Both processes have to go, not just the worker. They publish to the same
    # folder on purpose (D-19: shared config\ and .env), which means they also
    # share every class library in it - Domain, Collector, Parsers, Scoring,
    # Notifier. A running api holds all of those open, so stopping only the
    # worker left -Rebuild failing with MSB3021 on five dlls it had never
    # touched, and the error named the api rather than the thing being built.
    foreach ($name in $TaskName, ($TaskName + 'Api')) {
        Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    }

    Get-Process ProcurementRadar.Worker -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process ProcurementRadar.Api -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    Write-Host 'Yayinlaniyor...'
    & dotnet publish (Join-Path $repo 'src\ProcurementRadar.Worker') -c Release -o $publish --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    # The api goes to the same folder and shares the config and .env the worker
    # reads. Two processes, one deployment: an api pointing at a different copy
    # of the settings would be an interface that shows something the collector
    # is not doing.
    & dotnet publish (Join-Path $repo 'src\ProcurementRadar.Api') -c Release -o $publish --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed for the api.' }

    # The worker reads config/*.json at run time and finds it by walking up from
    # its own directory, so a copy has to sit beside the published files.
    #
    # Removed first, deliberately. Copy-Item with -Recurse onto an existing
    # directory copies *into* it - publish\config\config - and the worker goes on
    # reading the first install's copy forever. Every config change after that is
    # silently ignored by the running worker while looking applied in the repo,
    # which is the worst kind of wrong: nothing fails, the behaviour is just old.
    $configTarget = Join-Path $publish 'config'
    if (Test-Path $configTarget) { Remove-Item $configTarget -Recurse -Force }
    Copy-Item (Join-Path $repo 'config') $configTarget -Recurse
} else {
    Write-Host 'Yayin zaten var, atlaniyor. Kod degistiyse: -Rebuild'
}

# --- the browser EKAP needs ---------------------------------------------------
# A scheduled task cannot carry environment variables of its own, so the path
# goes in .env, which the worker reads at startup. Installing the browser
# somewhere the running worker does not look is worse than not installing it:
# thirty-one sources keep working and only EKAP fails, which reads as EKAP being
# down. It cost ten failures and an auto-disabled source to learn that once.
$browsers = Join-Path $repo '.playwright'
$env:PLAYWRIGHT_BROWSERS_PATH = $browsers

if (-not (Test-Path $browsers)) {
    Write-Host 'EKAP icin Chromium indiriliyor (~150 MB, bir kez)...'
    & (Join-Path $publish 'playwright.ps1') install chromium
}

$envPath = Join-Path $repo '.env'
if (Test-Path $envPath) {
    $lines = @(Get-Content $envPath)
    if (-not ($lines -match '^PLAYWRIGHT_BROWSERS_PATH=')) {
        Add-Content $envPath "PLAYWRIGHT_BROWSERS_PATH=$browsers"
        Write-Host '.env icine PLAYWRIGHT_BROWSERS_PATH eklendi.'
    }
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

# The api is a second process and needs its own task. Without one it only ran
# while somebody had a terminal open, which is how /swagger was unreachable the
# first time it was opened.
$apiTaskName = "$TaskName" + 'Api'
$apiAction = New-ScheduledTaskAction `
    -Execute (Join-Path $publish 'ProcurementRadar.Api.exe') `
    -WorkingDirectory $publish

Register-ScheduledTask -TaskName $apiTaskName -Action $apiAction -Trigger $trigger `
    -Settings $settings -Description 'Procurement Radar api (swagger, interface)' -Force | Out-Null

Write-Host "Registered '$apiTaskName' to start at logon (http://localhost:60080/swagger)."

# The backup (D-24). A third task rather than a loop inside the worker, because
# it shells out to docker for pg_dump - that binary is in the container and not
# on this machine - and the worker has no other reason to know docker exists.
#
# Nightly at three, with StartWhenAvailable, which is the setting that matters
# here: D-10 is honest that this machine is off or asleep much of the time, and
# without it every backup scheduled while the lid was shut would simply never
# happen. With it, a missed run goes as soon as the machine is back.
#
# `dotnet run` rather than a published exe: the cli is not published (D-19 puts
# only the worker and the api in publish/), and a backup that needs a build step
# is a backup that stops working the first time somebody cleans the repository.
$backupTaskName = "$TaskName" + 'Backup'
$backupAction = New-ScheduledTaskAction `
    -Execute 'dotnet' `
    -Argument "run --project `"$(Join-Path $repo 'src\ProcurementRadar.Cli')`" -c Release -- backup" `
    -WorkingDirectory $repo

$backupTrigger = New-ScheduledTaskTrigger -Daily -At 3am

$backupSettings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2)

Register-ScheduledTask -TaskName $backupTaskName -Action $backupAction -Trigger $backupTrigger `
    -Settings $backupSettings -Description 'Procurement Radar backup (D-24)' -Force | Out-Null

Write-Host "Registered '$backupTaskName' to run nightly at 03:00."

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

# Docker Desktop is what holds the database, and on Windows it starts at sign-in
# rather than at boot - if it starts at all. With auto-start off, a reboot leaves
# the worker running against nothing: every cycle fails, the health alarms fire,
# and the cause is a checkbox.
$dockerSettings = "$env:APPDATA\Docker\settings-store.json"
if (-not (Test-Path $dockerSettings)) { $dockerSettings = "$env:APPDATA\Docker\settings.json" }

if (Test-Path $dockerSettings) {
    $docker = Get-Content $dockerSettings -Raw | ConvertFrom-Json
    if ($docker.AutoStart -eq $true) {
        Write-Host '  Docker Desktop: oturum acilisinda basliyor. Iyi.'
    } else {
        Write-Host '  Docker Desktop OTURUM ACILISINDA BASLAMIYOR. Veritabani Docker icinde,'
        Write-Host '  yani makine yeniden baslayinca worker calisir ama baglanacak bir sey bulamaz.'
        Write-Host '  Acin: Docker Desktop > Settings > General > "Start Docker Desktop when you sign in".'
    }
} else {
    Write-Host '  Docker Desktop ayar dosyasi bulunamadi; oturum acilisinda basladigindan emin olun.'
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

# --- ucus oncesi kontrol (D-17) ---------------------------------------------
# Birim testler dort kez yesil kalirken canliyi kirdi: arayuzun arkasina sahte
# konmus bir test kodu dogrular, surucuyu degil. Bu on saniye gercek
# veritabanina ve gercek setting tablosuna karsi kosuyor. Gecmezse deploy burada
# durur, iki saat sonra logda degil.
Write-Host ''
Write-Host '--- ucus oncesi kontrol ---'
Push-Location $repo
try {
    & dotnet run --project src/ProcurementRadar.Cli -c Release --no-launch-profile -- preflight 2>&1 |
        Select-String 'Preflight|differs|Problem' |
        ForEach-Object { Write-Host ('  ' + $_.Line.Substring(0, [Math]::Min(160, $_.Line.Length))) }
    $preflight = $LASTEXITCODE
}
finally { Pop-Location }

if ($preflight -ne 0) {
    Write-Host ''
    Write-Host '  KONTROL BASARISIZ. Worker baslatilmadi. Yukaridaki hatalari duzeltin.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '--- gunluk kullanim ---'
Write-Host "  Baslat  : Start-ScheduledTask -TaskName $TaskName"
Write-Host "  Durdur  : Stop-ScheduledTask -TaskName $TaskName"
Write-Host "  Durum   : .\deploy\install-windows.ps1 -Status"
Write-Host "  Kaldir  : .\deploy\install-windows.ps1 -Uninstall"
Write-Host "  Geri kur: .\deploy\install-windows.ps1"
Write-Host "  Izle    : Get-Content '$publish\logs\procurement-radar-*.log' -Tail 20 -Wait"
