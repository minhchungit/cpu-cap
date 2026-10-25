#requires -RunAsAdministrator
$dir = Split-Path $MyInvocation.MyCommand.Path

$action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$dir\apply.ps1`""
$trigger   = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName 'CpuCap-Apply' -Action $action -Trigger $trigger -Principal $principal -Description 'Apply CPU cap profile (cpu-cap tool)' -Force | Out-Null
Write-Host "[OK] Scheduled task 'CpuCap-Apply' created (SYSTEM, no UAC)"

$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\CpuCap.lnk")
$lnk.TargetPath = "$dir\CpuCap.exe"
$lnk.Arguments  = '-tray'
$lnk.Save()
Write-Host "[OK] Autostart: CpuCap.exe -tray"

$lnk2 = $wsh.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\CpuCap.lnk")
$lnk2.TargetPath = "$dir\CpuCap.exe"
$lnk2.Save()
Write-Host "[OK] Start Menu shortcut: CpuCap"

# AppleSMC driver auto-start (fan control) - khong can MacsFanControl chay
$smc = Get-Service AppleSMC -ErrorAction SilentlyContinue
if ($smc) {
    Set-Service AppleSMC -StartupType Automatic
    if ($smc.Status -ne 'Running') { Start-Service AppleSMC }
    Write-Host "[OK] AppleSMC service: Automatic + Running"
} else {
    Write-Host "[--] AppleSMC service khong co - fan control bi tat"
}

Start-Process "$dir\CpuCap.exe" -ArgumentList '-tray'
schtasks /run /tn 'CpuCap-Apply' | Out-Null
Write-Host "[OK] Done. Tray icon is running."
