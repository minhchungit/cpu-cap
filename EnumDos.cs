using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

class EnumDos
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern uint QueryDosDevice(string name, IntPtr target, uint maxChars);

    static void Main()
    {
        IntPtr buf = Marshal.AllocHGlobal(262144 * 2);
        uint n = QueryDosDevice(null, buf, 262144);
        if (n == 0) { Console.WriteLine("fail: " + Marshal.GetLastWin32Error()); return; }

        var names = new List<string>();
        int pos = 0;
        while (pos < n)
        {
            string s = Marshal.PtrToStringUni(IntPtr.Add(buf, pos * 2));
            if (s.Length == 0) break;
            names.Add(s);
            pos += s.Length + 1;
        }
        Console.WriteLine("total devices: " + names.Count);

        var t = new StringBuilder(4096);
        foreach (var d in names)
        {
            if (d.IndexOf("APP0001", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("smc", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                t.Clear();
                var tb = new StringBuilder(4096);
                QueryDosDeviceByName(d, tb);
                Console.WriteLine("\\\\.\\" + d + " -> " + tb);
            }
        }
        Marshal.FreeHGlobal(buf);
        Console.WriteLine("--- first 10 devices ---");
        for (int i = 0; i < Math.Min(10, names.Count); i++) Console.WriteLine(names[i]);
    }

    static void QueryDosDeviceByName(string name, StringBuilder sb)
    {
        IntPtr b = Marshal.AllocHGlobal(4096 * 2);
        uint n = QueryDosDevice(name, b, 4096);
        if (n > 0) sb.Append(Marshal.PtrToStringUni(b));
        else sb.Append("(err " + Marshal.GetLastWin32Error() + ")");
        Marshal.FreeHGlobal(b);
    }
}
