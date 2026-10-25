using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

class SmcTest
{
    const uint IOCTL_READ = 0x220000;
    const uint IOCTL_WRITE = 0x220004;
    const uint IOCTL_INFO = 0x22000C;
    const uint IOCTL_PROTO = 0x220020;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr sec, uint disp, uint attr, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code,
        byte[] inb, int inlen, byte[] outb, int outlen, out int retlen, IntPtr ovl);

    static byte[] Ioctl(SafeFileHandle h, uint code, byte[] input, int outlen)
    {
        var outb = new byte[Math.Max(outlen, 1)];
        int n;
        if (!DeviceIoControl(h, code, input, input == null ? 0 : input.Length,
                             outb, outlen, out n, IntPtr.Zero))
            throw new IOException("IOCTL 0x" + code.ToString("X8") +
                " failed: " + Marshal.GetLastWin32Error());
        var r = new byte[n]; Array.Copy(outb, r, n); return r;
    }

    static byte[] ReadKey(SafeFileHandle h, string key, byte len)
    {
        var req = new byte[5];
        Encoding.ASCII.GetBytes(key).CopyTo(req, 0);
        req[4] = len;
        return Ioctl(h, IOCTL_READ, req, 32);
    }

    static void WriteKey(SafeFileHandle h, string key, byte[] data)
    {
        var req = new byte[5 + data.Length];
        Encoding.ASCII.GetBytes(key).CopyTo(req, 0);
        req[4] = (byte)data.Length;
        data.CopyTo(req, 5);
        Ioctl(h, IOCTL_WRITE, req, 1);
    }

    static byte[] GetInfo(SafeFileHandle h, string key)
    {
        return Ioctl(h, IOCTL_INFO, Encoding.ASCII.GetBytes(key), 6);
    }

    static float Flt(byte[] b) { return b.Length == 4 ? BitConverter.ToSingle(b, 0) : -1f; }

    static void Main(string[] a)
    {
        var dev = CreateFile("\\\\.\\APPLESMC", 0x80000000u | 0x40000000u, 0,
            IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (dev.IsInvalid) { Console.WriteLine("OPEN FAIL: " + Marshal.GetLastWin32Error()); return; }
        Console.WriteLine("OPEN OK");

        var proto = Ioctl(dev, IOCTL_PROTO, null, 1);
        Console.WriteLine("PROTOCOL: " + proto[0]);

        var info = GetInfo(dev, "FNum");
        Console.WriteLine("FNum info: len=" + info[0] + " type=" +
            Encoding.ASCII.GetString(info, 1, 4) + " flags=0x" + info[5].ToString("X2"));
        var fn = ReadKey(dev, "FNum", info[0]);
        Console.WriteLine("FNum = " + fn[0]);

        string[] keys = { "F0Ac","F1Ac","F0Md","F1Md","F0Tg","F1Tg","F0Mn","F0Mx","F1Mn","F1Mx" };
        foreach (var k in keys)
        {
            try
            {
                var ki = GetInfo(dev, k);
                string type = Encoding.ASCII.GetString(ki, 1, 4);
                var v = ReadKey(dev, k, ki[0]);
                string val;
                if (type.Trim() == "flt" && v.Length == 4)
                    val = BitConverter.ToSingle(v, 0).ToString("F2") + " (flt)";
                else if (v.Length == 1) val = v[0].ToString();
                else val = BitConverter.ToString(v);
                Console.WriteLine(k + ": type=" + type + " len=" + ki[0] + " -> " + val);
            }
            catch (Exception e) { Console.WriteLine(k + ": " + e.Message); }
        }

        string[] temps = { "TC0D","TC0E","TC0P","TC0C","TB0T","TG0D","Th1H" };
        foreach (var k in temps)
        {
            try
            {
                var ki = GetInfo(dev, k);
                string type = Encoding.ASCII.GetString(ki, 1, 4);
                var v = ReadKey(dev, k, ki[0]);
                Console.WriteLine(k + ": type=" + type + " len=" + ki[0] +
                    " -> " + BitConverter.ToString(v));
            }
            catch (Exception e) { Console.WriteLine(k + ": " + e.Message); }
        }

        if (a.Length > 0 && a[0] == "probe")
        {
            string[] cands = {
                "TC0E","TC0F","TC0P","TC0C","TC0D","TC1C","TC2C","TC3C","TC4C","TC5C","TC6C","TC7C","TC8C",
                "TCXC","TCGC","TCAD","TCSA","TCPG","TCPC","TCCD","TCMc",
                "TG0D","TG0P","TG0T","TG1D","TG1P","TGDD","TG0d","TGC0",
                "TH0a","TH0b","TH0x","TH0e","TH0o","TH1a","TH1b","TN0D","Ts0S","Th1H","Th2H","Th1R","Th2R",
                "TM0P","TM0S","TM1P","TM0a","TM1a","TM8P","TMBS","TW0P",
                "Tm0P","TA0P","TA1P","TA2P","TA3P","Ts0P","TP0P","TV0P","TL0P","TB0T","TB1T","TB2T",
                "Tp0P","Te0S","Te0P","TBLK","Vd0R","Vd1R","VDTR",
                "PPT0","PPT1","PPT2","PPT3","PSTR","PCPT","PC3C","PC0C","PCAM","PCPC","PG0R","PP0R"
            };
            foreach (var k in cands)
            {
                try {
                    var ki = GetInfo(dev, k);
                    if (ki == null || ki.Length < 6) { continue; }
                    string type = Encoding.ASCII.GetString(ki, 1, 4);
                    var v = ReadKey(dev, k, ki[0]);
                    if (v == null) { continue; }
                    string val;
                    if (type == "sp78" && v.Length == 2) val = ((sbyte)v[0] + v[1] / 256.0).ToString("F1") + " C";
                    else if ((type.Trim() == "flt" || type == "fltp") && v.Length == 4)
                        val = BitConverter.ToSingle(v, 0).ToString("F2");
                    else val = "raw:" + BitConverter.ToString(v);
                    Console.WriteLine(k + "  " + type + "  " + val);
                } catch {}
            }
            dev.Close();
            Console.WriteLine("DONE");
            return;
        }

        if (a.Length > 0 && a[0] == "dump")
        {
            var w = new StreamWriter("smc-keys.txt");
            w.WriteLine("idx\tkey\ttype\tlen\tval");
            int misses = 0;
            for (int i = 0; i < 900 && misses < 20; i++)
            {
                string kn = null;
                foreach (var ilen in new[] { 4, 1 })
                {
                    try {
                        var req = ilen == 4 ? BitConverter.GetBytes(i) : new byte[] { (byte)i };
                        var r = Ioctl(dev, 0x220008, req, 4);
                        if (r.Length == 4) { kn = Encoding.ASCII.GetString(r); break; }
                    } catch { System.Threading.Thread.Sleep(50); }
                }
                if (kn == null) { misses++; w.WriteLine(i + "\t<fail>"); continue; }
                misses = 0;
                string type = "", val = ""; int klen = 0;
                try {
                    var ki = GetInfo(dev, kn);
                    klen = ki[0];
                    type = Encoding.ASCII.GetString(ki, 1, 4);
                    if (type == "sp78" || type == "fltp" || type == "flt ") {
                        var v = ReadKey(dev, kn, ki[0]);
                        if (type == "sp78" && v.Length == 2)
                            val = ((sbyte)v[0] + v[1] / 256.0).ToString("F1") + " C";
                        else if (v.Length == 4)
                            val = BitConverter.ToSingle(v, 0).ToString("F2");
                        else val = BitConverter.ToString(v);
                    }
                } catch (Exception e) { val = "ERR " + e.Message; }
                w.WriteLine(i + "\t" + kn + "\t" + type + "\t" + klen + "\t" + val);
            }
            w.Close();
            dev.Close();
            Console.WriteLine("dumped to smc-keys.txt");
            return;
        }

        if (a.Length > 0 && a[0] == "setfan")
        {
            Console.WriteLine("\n=== SET FAN0 MANUAL 2500 RPM ===");
            WriteKey(dev, "F0Md", new byte[] { 1 });
            WriteKey(dev, "F0Tg", BitConverter.GetBytes(2500f));
            Console.WriteLine("written. waiting 4s...");
            System.Threading.Thread.Sleep(4000);
            var v = ReadKey(dev, "F0Ac", GetInfo(dev, "F0Ac")[0]);
            Console.WriteLine("F0Ac now: " + Flt(v).ToString("F0") + " RPM");
            Console.WriteLine("restoring auto...");
            WriteKey(dev, "F0Md", new byte[] { 0 });
            System.Threading.Thread.Sleep(2000);
            v = ReadKey(dev, "F0Ac", GetInfo(dev, "F0Ac")[0]);
            Console.WriteLine("F0Ac restored-auto: " + Flt(v).ToString("F0") + " RPM");
        }

        dev.Close();
        Console.WriteLine("DONE");
    }
}
