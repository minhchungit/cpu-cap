using System;
using System.Runtime.InteropServices;
using System.Text;

class EnumIf
{
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(IntPtr ClassGuid, string Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr devs, IntPtr devInfo,
        ref Guid InterfaceClassGuid, int MemberIndex, ref SP_DEVICE_INTERFACE_DATA didata);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr devs,
        ref SP_DEVICE_INTERFACE_DATA didata, IntPtr detail, int detailSize,
        out int requiredSize, ref SP_DEVINFO_DATA devinfo);

    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr devs);

    static void Main()
    {
        const int DIGCF_ALLCLASSES = 0x4, DIGCF_DEVICEINTERFACE = 0x10, DIGCF_PRESENT = 0x2;
        IntPtr devs = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero,
            DIGCF_ALLCLASSES | DIGCF_DEVICEINTERFACE | DIGCF_PRESENT);
        if (devs == new IntPtr(-1)) { Console.WriteLine("enum fail " + Marshal.GetLastWin32Error()); return; }

        int found = 0;
        for (int i = 0; ; i++)
        {
            var did = new SP_DEVICE_INTERFACE_DATA();
            did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            var g = Guid.Empty;
            if (!SetupDiEnumDeviceInterfaces(devs, IntPtr.Zero, ref g, i, ref did)) break;

            int size = 0; var dinf = new SP_DEVINFO_DATA();
            dinf.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
            SetupDiGetDeviceInterfaceDetailW(devs, ref did, IntPtr.Zero, 0, out size, ref dinf);
            if (size == 0) continue;
            IntPtr buf = Marshal.AllocHGlobal(size);
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            try
            {
                dinf = new SP_DEVINFO_DATA();
                dinf.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                if (SetupDiGetDeviceInterfaceDetailW(devs, ref did, buf, size, out size, ref dinf))
                {
                    string path = Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
                    if (path != null && path.IndexOf("APP0001", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine("PATH: " + path);
                        Console.WriteLine("GUID: " + did.InterfaceClassGuid);
                        found++;
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        SetupDiDestroyDeviceInfoList(devs);
        Console.WriteLine("found " + found);
    }
}
