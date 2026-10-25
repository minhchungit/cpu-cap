using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class Installer
{
    [STAThread]
    static int Main()
    {
        try {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "CpuCap");
            Directory.CreateDirectory(dir);

            // kill app cu neu dang chay
            foreach (var p in Process.GetProcessesByName("CpuCap"))
                try { p.Kill(); } catch {}

            Extract("payload_CpuCap.exe", Path.Combine(dir, "CpuCap.exe"));
            Extract("payload_Uninstall.exe", Path.Combine(dir, "Uninstall.exe"));
            Extract("payload_apply.ps1", Path.Combine(dir, "apply.ps1"));
            Extract("payload_postinstall.ps1", Path.Combine(dir, "postinstall.ps1"));
            Extract("payload_applesmc.sys",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "drivers", "applesmc.sys"));

            var r = Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + dir + "\\postinstall.ps1\"", 30);
            if (r != 0) {
                MessageBox.Show("postinstall.ps1 exit " + r + " - xem " + dir + "\\install.log",
                    "CpuCap Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            MessageBox.Show(
                "Installed to " + dir + "\n\n" +
                "- AppleSMC driver: running\n" +
                "- Task CpuCap-Apply: SYSTEM @ logon\n" +
                "- Desktop shortcut: CpuCap.lnk\n" +
                "- Autostart: ON (toggle via tray menu)",
                "CpuCap Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // mo app voi token user (explorer forward)
            Process.Start("explorer.exe", "\"" + dir + "\\CpuCap.exe\"");
            return 0;
        }
        catch (Exception ex) {
            MessageBox.Show("Install failed: " + ex.Message,
                "CpuCap Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static void Extract(string res, string dst)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
        using (var f = File.Create(dst)) {
            if (s == null) throw new IOException("missing resource " + res);
            s.CopyTo(f);
        }
    }

    static int Run(string exe, string args, int waitSec)
    {
        var p = Process.Start(new ProcessStartInfo {
            FileName = exe, Arguments = args,
            CreateNoWindow = true, UseShellExecute = false });
        if (!p.WaitForExit(waitSec * 1000)) { try { p.Kill(); } catch {} }
        return p.HasExited ? p.ExitCode : -1;
    }
}
