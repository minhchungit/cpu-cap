using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

public class Profile {
    public string name;
    public int dcBoost;
    public int acBoost;
    public int fanRpm;   // 0 = auto, >0 = manual RPM ca 2 quat (clamp theo min/max tung quat)
}

static class Smc {
    const uint IOCTL_READ  = 0x220000;
    const uint IOCTL_WRITE = 0x220004;
    const uint IOCTL_INFO  = 0x22000C;

    static SafeFileHandle dev;
    static bool triedStart;
    public static bool Available { get; private set; }
    public static readonly float[] FanMin = new float[2];
    public static readonly float[] FanMax = new float[2];

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr sec, uint disp, uint attr, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code,
        byte[] inb, int inlen, byte[] outb, int outlen, out int retlen, IntPtr ovl);

    static bool Open() {
        if (dev != null && !dev.IsInvalid) return true;
        dev = CreateFile("\\\\.\\APPLESMC", 0x80000000u | 0x40000000u, 0,
            IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (dev.IsInvalid && !triedStart) {
            triedStart = true;
            try {
                // task CpuCap-Apply chay SYSTEM: start service AppleSMC
                var p = Process.Start(new ProcessStartInfo {
                    FileName = "schtasks", Arguments = "/run /tn CpuCap-Apply",
                    CreateNoWindow = true, UseShellExecute = false });
                p.WaitForExit(3000);
                System.Threading.Thread.Sleep(1500);
                dev.Close();
                dev = CreateFile("\\\\.\\APPLESMC", 0x80000000u | 0x40000000u, 0,
                    IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            } catch {}
        }
        return dev != null && !dev.IsInvalid;
    }

    static byte[] Ioctl(uint code, byte[] input, int outlen) {
        if (!Open()) throw new IOException("smc device not open");
        var outb = new byte[Math.Max(outlen, 1)];
        int n;
        if (!DeviceIoControl(dev, code, input, input == null ? 0 : input.Length,
                             outb, outlen, out n, IntPtr.Zero)) {
            var h = dev; dev = null;
            try { h.Close(); } catch {}
            throw new IOException("ioctl 0x" + code.ToString("X8") + " err " + Marshal.GetLastWin32Error());
        }
        var r = new byte[n]; Array.Copy(outb, r, n); return r;
    }

    static byte[] Info(string key) {
        var r = Ioctl(IOCTL_INFO, Encoding.ASCII.GetBytes(key), 6);
        return r.Length >= 6 ? r : null;
    }

    public static byte[] Read(string key) {
        var i = Info(key);
        if (i == null || i[0] == 0 || i[0] > 32) return null;
        var req = new byte[5];
        Encoding.ASCII.GetBytes(key).CopyTo(req, 0);
        req[4] = i[0];
        var r = Ioctl(IOCTL_READ, req, 32);
        return r.Length == i[0] ? r : null;
    }

    public static void Write(string key, byte[] data) {
        var req = new byte[5 + data.Length];
        Encoding.ASCII.GetBytes(key).CopyTo(req, 0);
        req[4] = (byte)data.Length;
        data.CopyTo(req, 5);
        Ioctl(IOCTL_WRITE, req, 1);
    }

    public static bool Init() {
        Available = false;
        try {
            if (!Open()) return false;
            for (int f = 0; f < 2; f++) {
                var mn = Read("F" + f + "Mn");
                var mx = Read("F" + f + "Mx");
                FanMin[f] = (mn != null && mn.Length == 4) ? BitConverter.ToSingle(mn, 0) : 2000f;
                FanMax[f] = (mx != null && mx.Length == 4) ? BitConverter.ToSingle(mx, 0) : 6000f;
            }
            Available = true;
        } catch { try { if (dev != null) dev.Close(); dev = null; } catch {} }
        return Available;
    }

    public static float ReadFloat(string key) {
        var v = Read(key);
        return (v != null && v.Length == 4) ? BitConverter.ToSingle(v, 0) : -1f;
    }

    public static double ReadSp78(string key) {
        var v = Read(key);
        if (v == null || v.Length < 2) return double.NaN;
        return (sbyte)v[0] + v[1] / 256.0;
    }

    public static float ReadRpm(int fan) { return ReadFloat("F" + fan + "Ac"); }
    public static int ReadMode(int fan) {
        var v = Read("F" + fan + "Md");
        return (v != null && v.Length == 1) ? v[0] : -1;
    }

    public static void SetAuto() {
        Write("F0Md", new byte[] { 0 });
        System.Threading.Thread.Sleep(30);
        Write("F1Md", new byte[] { 0 });
    }

    public static void SetManual(float rpm) {
        for (int f = 0; f < 2; f++) {
            float t = Math.Min(FanMax[f], Math.Max(FanMin[f], rpm));
            Write("F" + f + "Md", new byte[] { 1 });
            System.Threading.Thread.Sleep(30);
            Write("F" + f + "Tg", BitConverter.GetBytes(t));
            System.Threading.Thread.Sleep(30);
        }
    }

    public static void Close() {
        try { if (dev != null) dev.Close(); } catch {}
        dev = null;
    }
}

public class App : ApplicationContext {
    const string SUB = "SUB_PROCESSOR";
    const string GUID_BOOST = "be337238-0d82-4146-a960-4f3749d470c7";
    const string SCHEME_KEY = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
    static string Dir = AppDomain.CurrentDomain.BaseDirectory;
    string DesiredPath = Path.Combine(Dir, "desired.json");

    NotifyIcon tray;
    ToolStripMenuItem miStatus, miFan, miTemp, miOn, miOff, miWatch, miStartup;
    ToolStripMenuItem miFanAuto, miFan3k, miFan4k, miFanMax;
    IntPtr lastIcon = IntPtr.Zero;
    JavaScriptSerializer ser = new JavaScriptSerializer();
    PerformanceCounter pc;
    int baseMHz = 2900;
    int curMHz = 0;
    bool turboOn = true;
    bool? desiredTurbo = null;
    int desiredFanRpm = 0;     // 0 = auto
    int fanRpm0 = -1, fanRpm1 = -1;
    double tCpu = double.NaN, tGpu = double.NaN, tSsd = double.NaN,
           tRam = double.NaN, tMb = double.NaN;
    int mhzViol = 0;
    int resyncs = 0;

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);

    public App() {
        try {
            using (var s = new ManagementObjectSearcher("select MaxClockSpeed from Win32_Processor"))
                foreach (var o in s.Get()) baseMHz = Convert.ToInt32(o["MaxClockSpeed"]);
        } catch {}
        try {
            pc = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
            pc.NextValue();
        } catch { pc = null; }

        try {
            if (File.Exists(DesiredPath)) {
                var p = ser.Deserialize<Profile>(File.ReadAllText(DesiredPath));
                desiredTurbo = p.acBoost != 0 || p.dcBoost != 0;
                desiredFanRpm = p.fanRpm;
            }
        } catch {}

        Smc.Init();
        if (desiredFanRpm > 0) ApplyFan(desiredFanRpm);

        var m = new ContextMenuStrip();
        miStatus = new ToolStripMenuItem("") { Enabled = false };
        miFan = new ToolStripMenuItem("") { Enabled = false };
        miTemp = new ToolStripMenuItem("") { Enabled = false };
        miWatch = new ToolStripMenuItem("") { Enabled = false, Visible = false };
        miOn = new ToolStripMenuItem("Turbo ON  (~4.3 GHz)", null, (s, e) => SetTurbo(true));
        miOff = new ToolStripMenuItem("Turbo OFF (max ~2.9 GHz)", null, (s, e) => SetTurbo(false));
        miFanAuto = new ToolStripMenuItem("Fan: Auto", null, (s, e) => SetFan(0));
        miFan3k = new ToolStripMenuItem("Fan: 3000 RPM", null, (s, e) => SetFan(3000));
        miFan4k = new ToolStripMenuItem("Fan: 4000 RPM", null, (s, e) => SetFan(4000));
        miFanMax = new ToolStripMenuItem("Fan: MAX", null, (s, e) => SetFan(5999));
        m.Items.Add(miStatus);
        m.Items.Add(miFan);
        m.Items.Add(miTemp);
        m.Items.Add(miWatch);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(miOn);
        m.Items.Add(miOff);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(miFanAuto);
        m.Items.Add(miFan3k);
        m.Items.Add(miFan4k);
        m.Items.Add(miFanMax);
        m.Items.Add(new ToolStripSeparator());
        miStartup = new ToolStripMenuItem("Start with Windows");
        miStartup.CheckOnClick = true;
        miStartup.Click += (s, e) => ToggleStartup();
        m.Items.Add(miStartup);
        m.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => {
            if (MessageBox.Show("Exit CpuCap? Fans will return to auto mode.",
                "CpuCap", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                Cleanup();
                Application.Exit();
            }
        }));
        m.Opening += (s, e) => RefreshMenu();

        tray = new NotifyIcon { Visible = true, ContextMenuStrip = m };
        tray.MouseUp += (s, e) => {
            if (e.Button == MouseButtons.Left)
                typeof(NotifyIcon).GetMethod("ShowContextMenu",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(tray, null);
        };
        Application.ApplicationExit += (s, e) => Cleanup();

        var t = new Timer { Interval = 3000 };
        t.Tick += (s, e) => Tick();
        t.Start();
        var w = new Timer { Interval = 60000 };
        w.Tick += (s, e) => Watchdog();
        w.Start();
        Tick();
    }

    void Cleanup() {
        try {
            Smc.SetAuto();
            System.Threading.Thread.Sleep(120);
            if (Smc.ReadMode(0) != 0 || Smc.ReadMode(1) != 0) Smc.SetAuto();
        } catch {}
        Smc.Close();
        tray.Visible = false;
    }

    string SchemeGuid() {
        using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SCHEME_KEY))
            return k != null ? k.GetValue("ActivePowerScheme") as string : null;
    }

    bool ReadTurbo() {
        try {
            string scheme = SchemeGuid();
            if (scheme == null) return true;
            string path = SCHEME_KEY + "\\" + scheme + "\\54533251-82be-4824-96c1-47b60b740d00\\" + GUID_BOOST;
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path)) {
                if (k == null) return true;
                bool onBattery = SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;
                object v = k.GetValue(onBattery ? "DCSettingIndex" : "ACSettingIndex");
                if (v == null) v = k.GetValue(onBattery ? "ACSettingIndex" : "DCSettingIndex");
                if (v == null) return true;
                return Convert.ToInt32(v) != 0;
            }
        } catch { return true; }
    }

    int ReadMHz() {
        try {
            if (pc != null) {
                float pct = pc.NextValue();
                if (pct > 0) return (int)(baseMHz * pct / 100.0);
            }
        } catch {}
        try {
            using (var s = new ManagementObjectSearcher("select CurrentClockSpeed from Win32_Processor"))
                foreach (var o in s.Get()) return Convert.ToInt32(o["CurrentClockSpeed"]);
        } catch {}
        return 0;
    }

    void Tick() {
        curMHz = ReadMHz();
        turboOn = ReadTurbo();
        if (Smc.Available) {
            try {
                float r0 = Smc.ReadRpm(0), r1 = Smc.ReadRpm(1);
                if (r0 > 0) fanRpm0 = (int)r0;
                if (r1 > 0) fanRpm1 = (int)r1;
                tCpu = Rd("TCXC"); if (double.IsNaN(tCpu)) tCpu = Rd("TC0E");
                tGpu = Rd("TGDD"); if (double.IsNaN(tGpu)) tGpu = Rd("TG0P");
                tSsd = Rd("Th1H"); if (double.IsNaN(tSsd)) tSsd = Rd("TH0a");
                tRam = Rd("TM0P");
                tMb  = Rd("Tm0P");
            } catch {}
        }
        string src = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online ? "AC" : "DC";
        tray.Text = string.Format("CPU Cap | {0} | {1} MHz | turbo {2}", src, curMHz, turboOn ? "ON" : "OFF");
        DrawIcon();
    }

    double Rd(string key) {
        try { return Smc.ReadSp78(key); } catch { return double.NaN; }
    }

    static string T(double v) { return double.IsNaN(v) ? "--" : v.ToString("0") + "\u00B0C"; }

    void Watchdog() {
        bool mismatch = false;
        if (desiredTurbo != null) {
            mismatch = ReadTurbo() != desiredTurbo.Value;
            if (!mismatch && !desiredTurbo.Value && ReadMHz() > (int)(baseMHz * 1.05)) {
                mhzViol++;
                if (mhzViol >= 2) mismatch = true;
            } else if (!mismatch) {
                mhzViol = 0;
            }
            if (mismatch) {
                ApplyBoost(desiredTurbo.Value);
                Log("resync -> turbo " + (desiredTurbo.Value ? "ON" : "OFF"));
            }
        }

        if (!Smc.Available) {
            if (Smc.Init()) {
                Log("smc re-init ok");
                if (desiredFanRpm > 0) {
                    ApplyFan(desiredFanRpm);
                    Log("resync -> fan manual " + desiredFanRpm + " RPM");
                    mismatch = true;
                }
            }
        } else if (desiredFanRpm > 0) {
            try {
                if (Smc.ReadMode(0) != 1 || Smc.ReadMode(1) != 1) {
                    ApplyFan(desiredFanRpm);
                    Log("resync -> fan manual " + desiredFanRpm + " RPM");
                    mismatch = true;
                }
            } catch {}
        }

        if (mismatch) resyncs++;
    }

    void Log(string msg) {
        try { File.AppendAllText(Path.Combine(Dir, "watchdog.log"),
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine); } catch {}
    }

    void RefreshMenu() {
        string src = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online ? "AC" : "DC";
        miStatus.Text = string.Format("CPU: {0} MHz | {1} | turbo: {2}", curMHz, src, turboOn ? "ON" : "OFF");
        if (fanErr)
            miFan.Text = "Fan: SMC write ERROR (see watchdog.log)";
        else if (Smc.Available)
            miFan.Text = string.Format("Fans: {0}/{1} RPM{2}",
                fanRpm0 >= 0 ? fanRpm0.ToString() : "?",
                fanRpm1 >= 0 ? fanRpm1.ToString() : "?",
                (desiredFanRpm > 0 && Math.Abs(fanRpm0 - desiredFanRpm) > 300)
                    ? " -> " + desiredFanRpm : "");
        else
            miFan.Text = "Fans: SMC unavailable";
        miTemp.Visible = Smc.Available;
        miTemp.Text = string.Format("CPU {0}  GPU {1}  SSD {2}  RAM {3}  MB {4}",
            T(tCpu), T(tGpu), T(tSsd), T(tRam), T(tMb));
        miWatch.Visible = resyncs > 0;
        miWatch.Text = "watchdog re-applied " + resyncs + " times";
        miOn.Checked = turboOn;
        miOff.Checked = !turboOn;
        bool fanOk = Smc.Available;
        miFanAuto.Enabled = miFan3k.Enabled = miFan4k.Enabled = miFanMax.Enabled = fanOk;
        miFanAuto.Checked = desiredFanRpm == 0;
        miFan3k.Checked = desiredFanRpm == 3000;
        miFan4k.Checked = desiredFanRpm == 4000;
        miFanMax.Checked = desiredFanRpm > 4000;
        miStartup.Checked = StartupEnabled();
    }

    const string RUNKEY = @"Software\Microsoft\Windows\CurrentVersion\Run";

    bool StartupEnabled() {
        try {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RUNKEY)) {
                return k != null &&
                    string.Equals(k.GetValue("CpuCap") as string,
                        Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
        } catch { return false; }
    }

    void ToggleStartup() {
        try {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RUNKEY, true)) {
                if (k == null) return;
                if (miStartup.Checked) k.SetValue("CpuCap", Application.ExecutablePath);
                else k.DeleteValue("CpuCap", false);
            }
        } catch {}
    }

    void DrawIcon() {
        try {
            string txt = (curMHz / 1000.0).ToString("0.0");
            string fan = desiredFanRpm <= 0 ? "Au"
                : desiredFanRpm >= 5000 ? "Mx" : (desiredFanRpm / 1000) + "k";
            using (var b = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(b)) {
                g.Clear(turboOn ? Color.FromArgb(0, 90, 40) : Color.FromArgb(160, 60, 20));
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using (var f = new Font("Segoe UI", 11, FontStyle.Bold))
                    g.DrawString(txt, f, Brushes.White, new PointF(-2, -3));
                using (var f2 = new Font("Segoe UI", 9, FontStyle.Bold))
                    g.DrawString(fan, f2,
                        desiredFanRpm > 0 ? Brushes.Yellow : Brushes.LightGray,
                        new PointF(0, 15));
                IntPtr h = b.GetHicon();
                tray.Icon = Icon.FromHandle(h);
                if (lastIcon != IntPtr.Zero) DestroyIcon(lastIcon);
                lastIcon = h;
            }
        } catch {}
    }

    int Cfg(string args) {
        var pi = new ProcessStartInfo {
            FileName = "powercfg", Arguments = args,
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        var pr = Process.Start(pi);
        pr.WaitForExit();
        return pr.ExitCode;
    }

    void ApplyBoost(bool on) {
        int v = on ? 2 : 0;
        int fails = 0;
        fails += Cfg("/setdcvalueindex SCHEME_CURRENT " + SUB + " " + GUID_BOOST + " " + v);
        fails += Cfg("/setacvalueindex SCHEME_CURRENT " + SUB + " " + GUID_BOOST + " " + v);
        fails += Cfg("/setactive SCHEME_CURRENT");
        if (fails != 0) {
            Process.Start(new ProcessStartInfo {
                FileName = "schtasks", Arguments = "/run /tn CpuCap-Apply",
                CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }

    bool fanErr = false;

    void ApplyFan(int rpm) {
        if (!Smc.Available && !Smc.Init()) { fanErr = true; return; }
        fanErr = false;
        for (int attempt = 0; attempt < 3; attempt++) {
            try {
                if (rpm <= 0) Smc.SetAuto(); else Smc.SetManual(rpm);
                System.Threading.Thread.Sleep(150);
                int m0 = Smc.ReadMode(0), m1 = Smc.ReadMode(1);
                int want = rpm <= 0 ? 0 : 1;
                if (m0 == want && m1 == want) return;
            } catch (Exception ex) {
                Log("fan apply fail: " + ex.Message);
            }
            System.Threading.Thread.Sleep(200);
        }
        fanErr = true;
        Log("fan verify failed after retries, target=" + rpm);
    }

    void SaveDesired() {
        try {
            var p = new Profile { dcBoost = 2, acBoost = 2 };
            if (File.Exists(DesiredPath)) {
                try {
                    var old = ser.Deserialize<Profile>(File.ReadAllText(DesiredPath));
                    if (old != null) { p.dcBoost = old.dcBoost; p.acBoost = old.acBoost; }
                } catch {}
            }
            if (desiredTurbo != null) {
                p.name = desiredTurbo.Value ? "Turbo ON" : "Turbo OFF";
                p.dcBoost = p.acBoost = desiredTurbo.Value ? 2 : 0;
            }
            p.fanRpm = desiredFanRpm;
            File.WriteAllText(DesiredPath, ser.Serialize(p));
        } catch {}
    }

    void SetTurbo(bool on) {
        desiredTurbo = on;
        SaveDesired();
        ApplyBoost(on);
        Tick();
    }

    void SetFan(int rpm) {
        desiredFanRpm = rpm;
        SaveDesired();
        ApplyFan(rpm);
        tray.BalloonTipTitle = "CpuCap";
        tray.BalloonTipText = rpm <= 0 ? "Fan -> Auto"
            : "Fan manual -> " + Math.Min(Smc.FanMax[0], rpm).ToString("0") + " RPM";
        try { tray.ShowBalloonTip(1500); } catch {}
        Tick();
    }

    [STAThread]
    static void Main() {
        bool created;
        using (var mtx = new System.Threading.Mutex(true, "CpuCap.SingleInstance", out created)) {
            if (!created) {
                MessageBox.Show("CpuCap is already running.\nCheck the system tray icon.",
                    "CpuCap", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Application.EnableVisualStyles();
            Application.Run(new App());
        }
    }
}
