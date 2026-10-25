using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

static class Uninstaller
{
    const uint IOCTL_WRITE = 0x220004;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr sec, uint disp, uint attr, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code,
        byte[] inb, int inlen, byte[] outb, int outlen, out int retlen, IntPtr ovl);

    static void SmcWrite(SafeFileHandle dev, string key, byte[] data)
    {
        var req = new byte[5 + data.Length];
        Encoding.ASCII.GetBytes(key).CopyTo(req, 0);
        req[4] = (byte)data.Length;
        data.CopyTo(req, 5);
        var outb = new byte[1];
        int n;
        DeviceIoControl(dev, IOCTL_WRITE, req, req.Length, outb, 1, out n, IntPtr.Zero);
    }

    static void FansToAuto()
    {
        try {
            var dev = CreateFile("\\\\.\\APPLESMC", 0x80000000u | 0x40000000u, 0,
                IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            if (dev.IsInvalid) return;
            SmcWrite(dev, "F0Md", new byte[] { 0 });
            SmcWrite(dev, "F1Md", new byte[] { 0 });
            dev.Close();
        } catch {}
    }

    static void Run(string exe, string args)
    {
        try {
            var p = Process.Start(new ProcessStartInfo {
                FileName = exe, Arguments = args,
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true });
            p.WaitForExit(15000);
        } catch {}
    }

    static void Del(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch {}
    }

    [STAThread]
    static int Main()
    {
        if (MessageBox.Show(
            "Uninstall CpuCap?\n\nFans will return to auto mode.\nAppleSMC driver will be removed.",
            "CpuCap Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return 0;

        string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

        foreach (var p in Process.GetProcessesByName("CpuCap"))
            try { p.Kill(); } catch {}
        System.Threading.Thread.Sleep(500);

        // quat ve auto truoc khi go driver
        FansToAuto();

        Run("sc.exe", "stop AppleSMC");
        Run("sc.exe", "delete AppleSMC");
        Del(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers", "applesmc.sys"));

        Run("schtasks", "/delete /tn CpuCap-Apply /f");

        try {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                if (k != null) k.DeleteValue("CpuCap", false);
        } catch {}

        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Del(Path.Combine(user, "Desktop", "CpuCap.lnk"));
        Del(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "CpuCap.lnk"));
        Del(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs", "CpuCap.lnk"));
        Del(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs", "CpuCap-Uninstall.lnk"));

        // tu xoa thu muc cai sau khi process thoat
        Process.Start(new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + dir + "\"",
            CreateNoWindow = true, UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden });

        MessageBox.Show("CpuCap uninstalled.", "CpuCap Uninstall",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
}
