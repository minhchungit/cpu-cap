$dir = Split-Path $MyInvocation.MyCommand.Path
$log = "$dir\install.log"
function L($m) { "[$(Get-Date -Format 'HH:mm:ss')] $m" | Tee-Object $log -Append }

# 1. AppleSMC service (driver da duoc extract vao System32\drivers boi installer)
sc.exe delete AppleSMC 2>$null | Out-Null
sc.exe create AppleSMC type= kernel start= auto error= normal binPath= "C:\Windows\System32\drivers\applesmc.sys" DisplayName= "Apple SMC service" | Out-Null
Start-Service AppleSMC -ErrorAction SilentlyContinue
L "[OK] AppleSMC service created+started"

# 2. Scheduled task SYSTEM: apply power profile + start AppleSMC o moi logon
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$dir\apply.ps1`""
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName 'CpuCap-Apply' -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
L "[OK] Task CpuCap-Apply (SYSTEM, logon)"

# 3. Shortcuts
$w = New-Object -ComObject WScript.Shell
$lnk = $w.CreateShortcut("$env:USERPROFILE\Desktop\CpuCap.lnk")
$lnk.TargetPath = "$dir\CpuCap.exe"; $lnk.Save()
L "[OK] Desktop shortcut"
$lnk2 = $w.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\CpuCap.lnk")
$lnk2.TargetPath = "$dir\CpuCap.exe"; $lnk2.Save()
$lnk3 = $w.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\CpuCap-Uninstall.lnk")
$lnk3.TargetPath = "$dir\Uninstall.exe"; $lnk3.Save()
L "[OK] Start Menu shortcuts (app + uninstall)"

# 4. Default: start with Windows = ON (app toggles qua menu)
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'CpuCap' -Value "$dir\CpuCap.exe"
L "[OK] Autostart enabled (HKCU Run)"

# 5. Migrate desired.json cu neu co
$old = "$env:USERPROFILE\Documents\optimize\cpu-cap\desired.json"
if ((Test-Path $old) -and -not (Test-Path "$dir\desired.json")) {
    Copy-Item $old "$dir\desired.json"
    L "[OK] migrated desired.json"
}
L "DONE"
