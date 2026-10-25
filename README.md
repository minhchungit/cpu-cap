# CpuCap

A tiny Windows system-tray utility for **Intel Macs running Windows via Boot Camp**.

It caps CPU turbo boost (for heat/battery), takes manual control of the Mac's
internal fans through the AppleSMC kernel driver, and shows live clock speed,
temperatures, and fan speeds — all from a single tray icon. No dependencies,
no background service, one small EXE.

> Apple Silicon Macs can't run Boot Camp — this is for Intel Macs only
> (MacBook Pro/Air, iMac, Mac mini, Mac Pro 2012–2020).

## Features

- **Turbo boost toggle** — `Turbo OFF` caps the CPU at base clock
  (e.g. ~2.9 GHz instead of ~4.3 GHz) via the hidden
  `PERFBOOSTMODE` power setting. Applies to both AC and DC.
- **Fan control** — Auto / 3000 / 4000 / MAX presets, written straight to the
  SMC (`F0Md`/`F1Md` + `F0Tg`/`F1Tg`), clamped to each fan's min/max.
- **Live tray icon** — current GHz (green = turbo on, orange = off) with a fan
  badge (`Au` / `3k` / `4k` / `Mx`). Hover for details.
- **Temperatures** — CPU, GPU, SSD, RAM, logic board (`sp78` SMC keys).
- **Watchdog (60 s)** — re-applies your chosen turbo/fan state if Windows or a
  power-scheme change resets it, and automatically re-initializes the SMC
  connection if the driver wasn't ready when the app started.
- **Persistent state** — `desired.json` next to the EXE; reapplied on launch.
- **Unprivileged friendly** — runs as a normal user; a SYSTEM scheduled task
  (`CpuCap-Apply`) performs the privileged powercfg/driver work at logon.

## Install

1. Download `CpuCap-Setup.exe` from
   [Releases](../../releases/latest) and run it (it asks for admin).
2. The installer:
   - copies the app to `%LOCALAPPDATA%\Programs\CpuCap`
   - installs `applesmc.sys` to `System32\drivers` and registers the
     `AppleSMC` service as **auto-start** (driver loads at every boot)
   - creates the `CpuCap-Apply` SYSTEM scheduled task (logon trigger)
   - adds desktop/start-menu shortcuts and enables autostart (HKCU Run)
3. The tray icon appears. Done.

## Usage

| Menu item          | Effect                                              |
|--------------------|-----------------------------------------------------|
| Turbo ON           | `PERFBOOSTMODE = 2` (aggressive) on AC + DC         |
| Turbo OFF          | `PERFBOOSTMODE = 0` (disabled) — max base clock     |
| Fan: Auto          | hands control back to the SMC (`FxMd = 0`)          |
| Fan: 3000/4000/MAX | manual target RPM (`FxMd = 1`, `FxTg = rpm`)        |
| Start with Windows | toggles the HKCU Run entry                          |
| Exit               | fans return to auto mode                            |

Left-click or right-click the icon to open the menu.

## How it works

- **Boost** — `powercfg /set{dc,ac}valueindex SCHEME_CURRENT SUB_PROCESSOR
  be337238-0d82-4146-a960-4f3749d470c7 {0|2}` then `/setactive`.
  The app also reads the setting back from the registry
  (`...\PowerSchemes\<scheme>\54533251-...\be337238-...`) to detect drift.
- **Fans / temps** — `DeviceIoControl` against `\\.\APPLESMC`
  (`IOCTL_INFO` 0x22000C / `IOCTL_READ` 0x220000 / `IOCTL_WRITE` 0x220004).
  Keys: `F{n}Md` (0=auto, 1=manual), `F{n}Tg` (target RPM, float),
  `F{n}Ac`/`F{n}Mn`/`F{n}Mx` (actual/min/max RPM).
- **Temps** — `sp78` fixed-point keys: CPU `TCXC`→`TC0E`, GPU `TGDD`→`TG0P`,
  SSD `Th1H`→`TH0a`, RAM `TM0P`, board `Tm0P`.
- If the app can't reach the driver it first tries to start it via the
  `CpuCap-Apply` task, then keeps retrying init in the watchdog loop.

## Uninstall

Run **CpuCap-Uninstall** from the Start Menu (or `Uninstall.exe` in the
install folder): fans return to auto, the `AppleSMC` service and
`applesmc.sys` are removed, the scheduled task, autostart entry, shortcuts,
and install folder are deleted.

## Build from source

Only needs the in-box .NET Framework 4.x C# compiler — no SDK required:

```bat
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

:: app
%CSC% /target:winexe /out:CpuCap.exe CpuCap.cs ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:System.Management.dll /r:System.Web.Extensions.dll ^
  /win32icon:CpuCap.ico

:: uninstaller
%CSC% /target:winexe /out:Uninstall.exe Uninstaller.cs /r:System.Windows.Forms.dll

:: setup (embeds payloads + admin manifest)
%CSC% /target:winexe /out:CpuCap-Setup.exe Installer.cs ^
  /r:System.Windows.Forms.dll /win32manifest:admin.manifest /win32icon:CpuCap.ico ^
  /res:CpuCap.exe,payload_CpuCap.exe /res:Uninstall.exe,payload_Uninstall.exe ^
  /res:apply.ps1,payload_apply.ps1 /res:postinstall.ps1,payload_postinstall.ps1 ^
  /res:%SystemRoot%\System32\drivers\applesmc.sys,payload_applesmc.sys
```

## Dev tools

| File            | Purpose                                                        |
|-----------------|----------------------------------------------------------------|
| `SmcTest.cs`    | CLI probe: reads fans/temps; `dump` → `smc-keys.txt`, `setfan`  |
| `EnumDos.cs`    | lists `\\.\` DOS device objects                                 |
| `EnumIf.cs`     | enumerates network interfaces                                   |
| `GenIcon.cs`    | regenerates `CpuCap.ico`                                        |
| `install.ps1`   | dev install (task + shortcuts + service auto-start)             |
| `apply.ps1`     | SYSTEM task body: applies `desired.json`/`profiles.json`        |
| `postinstall.ps1`| what the Setup EXE runs after extracting payloads              |
| `smc-keys.txt`  | reference dump of SMC keys found on a MacBook Pro (Boot Camp)   |

## Troubleshooting

- **"Fans: SMC unavailable"** — the `AppleSMC` driver isn't running.
  Admin prompt: `sc start AppleSMC` (installed setups register it
  auto-start, so this means something removed it — reinstall the setup).
- **Fans stuck/loud after force-killing the app** — relaunch CpuCap or run
  `SmcTest.exe setfan` to restore auto mode.
- **Macs Fan Control conflict** — both use the same `applesmc.sys` driver and
  MFC re-registers the service pointing to its own folder. If fan control
  breaks after installing/removing MFC, re-run `CpuCap-Setup.exe`.
- `watchdog.log` in the install folder records every resync.

## Credits / license

Source code: MIT (see `LICENSE`).

`applesmc.sys` is the SMC driver from
[Macs Fan Control](https://crystalidea.com/macs-fan-control) (CrystalIdea) —
freeware, all rights belong to its author; bundled here only for convenience.
