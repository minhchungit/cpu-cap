# CpuCap

Tiện ích system-tray nhỏ gọn cho **máy Mac Intel chạy Windows qua Boot Camp**.

Chặn turbo boost của CPU (để mát máy/tiết kiệm pin), điều khiển quạt thủ công
qua driver kernel AppleSMC, và hiển thị xung nhịp, nhiệt độ, tốc độ quạt theo
thời gian thực — tất cả chỉ từ 1 icon trên khay hệ thống. Không dependency,
không service chạy nền, chỉ 1 file EXE nhỏ.

> Mac Apple Silicon không có Boot Camp — tool này chỉ dành cho Mac Intel
> (MacBook Pro/Air, iMac, Mac mini, Mac Pro 2012–2020).

## Tính năng

- **Bật/tắt turbo boost** — `Turbo OFF` khóa CPU ở base clock
  (ví dụ ~2.9 GHz thay vì boost ~4.3 GHz) qua setting ẩn `PERFBOOSTMODE`.
  Áp dụng cho cả pin (DC) lẫn sạc (AC).
- **Điều khiển quạt** — preset Auto / 3000 / 4000 / MAX, ghi thẳng vào SMC
  (`F0Md`/`F1Md` + `F0Tg`/`F1Tg`), tự clamp theo min/max từng quạt.
- **Icon tray động** — GHz hiện tại (xanh lá = turbo bật, cam = tắt) kèm
  badge chế độ quạt (`Au` / `3k` / `4k` / `Mx`). Hover để xem chi tiết.
- **Nhiệt độ** — CPU, GPU, SSD, RAM, bo mạch (đọc qua key `sp78` của SMC).
- **Watchdog (60 giây)** — tự áp lại turbo/quạt nếu Windows hay power scheme
  bị reset; tự init lại kết nối SMC nếu driver chưa sẵn sàng khi app khởi động.
- **Lưu trạng thái** — `desired.json` cạnh file EXE, tự áp lại khi mở app.
- **Không cần quyền admin để chạy** — scheduled task `CpuCap-Apply`
  (chạy SYSTEM, kích hoạt lúc logon) lo phần việc cần quyền cao.

## Cài đặt

1. Tải `CpuCap-Setup.exe` từ mục
   [Releases](../../releases/latest) rồi chạy (sẽ hỏi quyền admin).
2. Bộ cài sẽ:
   - copy app vào `%LOCALAPPDATA%\Programs\CpuCap`
   - cài `applesmc.sys` vào `System32\drivers` và đăng ký service `AppleSMC`
     kiểu **auto-start** (driver tự load mỗi lần boot)
   - tạo scheduled task `CpuCap-Apply` (SYSTEM, trigger lúc logon)
   - tạo shortcut Desktop/Start Menu và bật autostart (HKCU Run)
3. Icon xuất hiện trên khay hệ thống. Xong.

## Cách dùng

| Menu               | Tác dụng                                          |
|--------------------|----------------------------------------------------|
| Turbo ON           | `PERFBOOSTMODE = 2` (aggressive) cho AC + DC        |
| Turbo OFF          | `PERFBOOSTMODE = 0` (tắt) — tối đa base clock       |
| Fan: Auto          | trả quạt về SMC tự điều khiển (`FxMd = 0`)          |
| Fan: 3000/4000/MAX | set RPM thủ công (`FxMd = 1`, `FxTg = rpm`)         |
| Start with Windows | bật/tắt autostart (HKCU Run)                        |
| Exit               | thoát app, quạt tự về chế độ auto                   |

Click chuột trái hoặc phải vào icon đều mở được menu.

## Nguyên lý hoạt động

- **Boost** — `powercfg /set{dc,ac}valueindex SCHEME_CURRENT SUB_PROCESSOR
  be337238-0d82-4146-a960-4f3749d470c7 {0|2}` rồi `/setactive`.
  App đọc lại setting trong registry
  (`...\PowerSchemes\<scheme>\54533251-...\be337238-...`) để phát hiện drift.
- **Quạt / nhiệt độ** — `DeviceIoControl` vào `\\.\APPLESMC`
  (`IOCTL_INFO` 0x22000C / `IOCTL_READ` 0x220000 / `IOCTL_WRITE` 0x220004).
  Key: `F{n}Md` (0=auto, 1=manual), `F{n}Tg` (RPM mục tiêu, float),
  `F{n}Ac`/`F{n}Mn`/`F{n}Mx` (RPM thực/min/max).
- **Nhiệt độ** — key `sp78` (fixed-point): CPU `TCXC`→`TC0E`, GPU `TGDD`→`TG0P`,
  SSD `Th1H`→`TH0a`, RAM `TM0P`, bo mạch `Tm0P`.
- Nếu không mở được driver, app thử start qua task `CpuCap-Apply`, sau đó
  watchdog tiếp tục retry init mỗi 60 giây.

## Gỡ cài đặt

Chạy **CpuCap-Uninstall** trong Start Menu (hoặc `Uninstall.exe` trong thư mục
cài): quạt về auto, gỡ service `AppleSMC` + file `applesmc.sys`, xóa scheduled
task, autostart, shortcuts và thư mục cài đặt.

## Build từ source

Chỉ cần compiler C# có sẵn của .NET Framework 4.x — không cần cài SDK:

```bat
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

:: app chính
%CSC% /target:winexe /out:CpuCap.exe CpuCap.cs ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:System.Management.dll /r:System.Web.Extensions.dll ^
  /win32icon:CpuCap.ico

:: uninstaller
%CSC% /target:winexe /out:Uninstall.exe Uninstaller.cs /r:System.Windows.Forms.dll

:: bộ cài (embed payload + admin manifest)
%CSC% /target:winexe /out:CpuCap-Setup.exe Installer.cs ^
  /r:System.Windows.Forms.dll /win32manifest:admin.manifest /win32icon:CpuCap.ico ^
  /res:CpuCap.exe,payload_CpuCap.exe /res:Uninstall.exe,payload_Uninstall.exe ^
  /res:apply.ps1,payload_apply.ps1 /res:postinstall.ps1,payload_postinstall.ps1 ^
  /res:%SystemRoot%\System32\drivers\applesmc.sys,payload_applesmc.sys
```

## Dev tools

| File             | Chức năng                                                       |
|------------------|-----------------------------------------------------------------|
| `SmcTest.cs`     | CLI probe: đọc quạt/nhiệt độ; `dump` → `smc-keys.txt`, `setfan`  |
| `EnumDos.cs`     | liệt kê DOS device `\\.\`                                       |
| `EnumIf.cs`      | liệt kê network interface                                       |
| `GenIcon.cs`     | tạo lại `CpuCap.ico`                                            |
| `install.ps1`    | cài dev (task + shortcut + service auto-start)                  |
| `apply.ps1`      | body của task SYSTEM: áp `desired.json`/`profiles.json`         |
| `postinstall.ps1`| script chạy sau khi Setup EXE giải nén payload                  |
| `smc-keys.txt`   | dump tham khảo các SMC key tìm được trên MacBook Pro (Boot Camp)|

## Xử lý sự cố

- **"Fans: SMC unavailable"** — driver `AppleSMC` không chạy.
  Mở cmd quyền admin: `sc start AppleSMC` (bản cài chuẩn đăng ký auto-start,
  nên nếu bị lỗi này nghĩa là có gì đó gỡ driver — cài lại bộ setup).
- **Quạt kẹt ở tốc độ cao sau khi kill app** — mở lại CpuCap hoặc chạy
  `SmcTest.exe setfan` để trả về auto.
- **Xung đột với Macs Fan Control** — cả 2 dùng chung driver `applesmc.sys`,
  MFC đăng ký lại service trỏ vào thư mục của nó. Nếu fan control hỏng sau
  khi cài/gỡ MFC, chạy lại `CpuCap-Setup.exe`.
- `watchdog.log` trong thư mục cài đặt ghi lại mọi lần resync.

## Credit / giấy phép

Source code: MIT (xem `LICENSE`).

`applesmc.sys` là driver SMC của
[Macs Fan Control](https://crystalidea.com/macs-fan-control) (CrystalIdea) —
phần mềm miễn phí, bản quyền thuộc tác giả; chỉ kèm vào đây cho tiện cài đặt.
