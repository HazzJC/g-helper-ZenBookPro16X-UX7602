using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// DialProbe: enumerate HID collections, dump their capabilities (descriptor equivalent), listen to input
// reports, and read/write feature reports.  Usage:
//   DialProbe list [filter]                 - list HID paths (filter = substring of path, default "asus2020")
//   DialProbe caps [filter]                 - dump caps (usage pages, report ids, field layout)
//   DialProbe listen [filter] [seconds]     - passively print input reports from every matching collection
//   DialProbe getfeature <filter> <id> [n]  - read feature report id (n = total length incl. id)
//   DialProbe setfeature <filter> <hex...>  - write feature report (first byte = report id)
static class P
{
    static readonly Guid HidGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pd);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pd);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pd, byte[] caps);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, byte[] caps, ref ushort len, IntPtr pd);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, byte[] caps, ref ushort len, IntPtr pd);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attr a);
    [StructLayout(LayoutKind.Sequential)] struct Attr { public int Size; public ushort Vid, Pid, Ver; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr t);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadFile(SafeFileHandle h, byte[] buf, int len, out int read, IntPtr ov);

    [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SPDI data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SPDI data, IntPtr detail, int size, out int req, IntPtr dev);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [StructLayout(LayoutKind.Sequential)] struct SPDI { public int cb; public Guid g; public int flags; public IntPtr res; }

    static List<string> Paths(string filter)
    {
        var g = HidGuid;
        var set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        var res = new List<string>();
        for (int i = 0; ; i++)
        {
            var d = new SPDI { cb = Marshal.SizeOf<SPDI>() };
            if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
            SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out int req, IntPtr.Zero);
            IntPtr buf = Marshal.AllocHGlobal(req);
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out _, IntPtr.Zero))
            {
                string p = Marshal.PtrToStringUni(buf + 4);
                if (filter == "*" || p.Contains(filter, StringComparison.OrdinalIgnoreCase)) res.Add(p);
            }
            Marshal.FreeHGlobal(buf);
        }
        SetupDiDestroyDeviceInfoList(set);
        return res;
    }

    static SafeFileHandle Open(string path, bool rw = true)
    {
        var h = CreateFile(path, rw ? GENERIC_READ | GENERIC_WRITE : 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid && rw) h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid && rw) h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        return h;
    }

    static string Hex(byte[] b, int n) => BitConverter.ToString(b, 0, n).Replace('-', ' ');

    static void DumpCaps(string path)
    {
        using var h = Open(path, false);
        if (h.IsInvalid) { Console.WriteLine($"{path}\n  open failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}"); return; }
        var a = new Attr { Size = 12 }; HidD_GetAttributes(h, ref a);
        var sb = new byte[256]; HidD_GetProductString(h, sb, sb.Length);
        Console.WriteLine($"\n{path}\n  VID={a.Vid:X4} PID={a.Pid:X4} ver={a.Ver:X4} product='{System.Text.Encoding.Unicode.GetString(sb).TrimEnd('\0')}'");
        if (!HidD_GetPreparsedData(h, out var pd)) { Console.WriteLine("  no preparsed"); return; }
        var caps = new byte[64]; HidP_GetCaps(pd, caps);
        ushort U(int o) => BitConverter.ToUInt16(caps, o);
        Console.WriteLine($"  TopUsage page=0x{U(2):X2} usage=0x{U(0):X2}  InLen={U(4)} OutLen={U(6)} FeatLen={U(8)}");
        // NumberInputButtonCaps@44? layout: 5 ushorts(10) + 17 reserved(34) = 44 ; then 10 ushorts
        int n = 44;
        ushort linkNodes = U(n), inB = U(n + 2), inV = U(n + 4), outB = U(n + 10), outV = U(n + 12), feB = U(n + 16), feV = U(n + 18);
        Console.WriteLine($"  buttons in/out/feat = {inB}/{outB}/{feB}   values in/out/feat = {inV}/{outV}/{feV}");
        string[] names = { "INPUT", "OUTPUT", "FEATURE" };
        for (int t = 0; t < 3; t++)
        {
            ushort bc = t == 0 ? inB : t == 1 ? outB : feB, vc = t == 0 ? inV : t == 1 ? outV : feV;
            if (bc > 0)
            {
                var b = new byte[72 * bc]; ushort len = bc; HidP_GetButtonCaps(t, b, ref len, pd);
                for (int i = 0; i < len; i++)
                {
                    int o = i * 72;
                    bool range = b[o + 12] != 0;
                    ushort page = BitConverter.ToUInt16(b, o);
                    ushort u0 = BitConverter.ToUInt16(b, o + 56), u1 = BitConverter.ToUInt16(b, o + 58);
                    Console.WriteLine($"  [{names[t]} BTN] id=0x{b[o + 2]:X2} page=0x{page:X2} usage={(range ? $"0x{u0:X2}..0x{u1:X2}" : $"0x{u0:X2}")} link={BitConverter.ToUInt16(b, o + 6)}");
                }
            }
            if (vc > 0)
            {
                var v = new byte[72 * vc]; ushort len = vc; HidP_GetValueCaps(t, v, ref len, pd);
                for (int i = 0; i < len; i++)
                {
                    int o = i * 72;
                    bool range = v[o + 12] != 0;
                    ushort page = BitConverter.ToUInt16(v, o);
                    ushort u0 = BitConverter.ToUInt16(v, o + 56), u1 = BitConverter.ToUInt16(v, o + 58);
                    Console.WriteLine($"  [{names[t]} VAL] id=0x{v[o + 2]:X2} page=0x{page:X2} usage={(range ? $"0x{u0:X2}..0x{u1:X2}" : $"0x{u0:X2}")} bits={BitConverter.ToUInt16(v, o + 18)} count={BitConverter.ToUInt16(v, o + 20)} logical={BitConverter.ToInt32(v, o + 40)}..{BitConverter.ToInt32(v, o + 44)} phys={BitConverter.ToInt32(v, o + 48)}..{BitConverter.ToInt32(v, o + 52)} unitsExp={BitConverter.ToInt32(v, o + 32)} units=0x{BitConverter.ToInt32(v, o + 36):X} abs={v[o + 15]} null={v[o + 16]} link={BitConverter.ToUInt16(v, o + 6)}");
                }
            }
        }
        HidD_FreePreparsedData(pd);
    }

    static void Listen(List<string> paths, int seconds)
    {
        var start = DateTime.UtcNow;
        var lk = new object();
        foreach (var p in paths)
        {
            var h = Open(p);
            string tag = p.Contains("col0") ? p[(p.IndexOf("col0") + 3)..(p.IndexOf("col0") + 6)].ToUpper() : "DEV";
            if (h.IsInvalid) { Console.WriteLine($"{tag}: open failed {new Win32Exception(Marshal.GetLastWin32Error()).Message}"); continue; }
            if (!HidD_GetPreparsedData(h, out var pd)) continue;
            var caps = new byte[64]; HidP_GetCaps(pd, caps); int inLen = BitConverter.ToUInt16(caps, 4);
            HidD_FreePreparsedData(pd);
            if (inLen == 0) { Console.WriteLine($"{tag}: no input reports"); continue; }
            Console.WriteLine($"{tag}: listening (input len {inLen})");
            new Thread(() =>
            {
                var buf = new byte[inLen];
                while (true)
                {
                    if (!ReadFile(h, buf, inLen, out int n, IntPtr.Zero) || n == 0) { lock (lk) Console.WriteLine($"{tag}: read ended"); return; }
                    lock (lk) Console.WriteLine($"{(DateTime.UtcNow - start).TotalSeconds,8:F3}  {tag}  {Hex(buf, n)}");
                }
            }) { IsBackground = true }.Start();
        }
        Thread.Sleep(seconds * 1000);
    }

    static void Main(string[] a)
    {
        string cmd = a.Length > 0 ? a[0] : "list";
        string filter = a.Length > 1 ? a[1] : "asus2020"; if (cmd == "disasm" || cmd == "callers" || cmd == "strat") filter = "";
        switch (cmd)
        {
            case "disasm": Dis.Disasm(a[1], Convert.ToUInt64(a[2], 16), int.Parse(a[3])); break;
            case "callers": Dis.Callers(a[1], Convert.ToUInt64(a[2], 16), a.Length > 3 ? int.Parse(a[3]) : 80); break;
            case "strat": { var d = File.ReadAllBytes(a[1]); foreach (var v in a.Skip(2)) Dis.StrAt(d, a[1], Convert.ToUInt64(v, 16)); break; }
            case "list": foreach (var p in Paths(filter)) Console.WriteLine(p); break;
            case "caps": foreach (var p in Paths(filter)) DumpCaps(p); break;
            case "listen": Listen(Paths(filter), a.Length > 2 ? int.Parse(a[2]) : 20); break;
            case "getfeature":
                {
                    byte id = Convert.ToByte(a[2], 16); int n = a.Length > 3 ? int.Parse(a[3]) : 64;
                    foreach (var p in Paths(filter))
                    {
                        using var h = Open(p); if (h.IsInvalid) continue;
                        var b = new byte[n]; b[0] = id;
                        bool ok = HidD_GetFeature(h, b, n);
                        Console.WriteLine($"{p}\n  ok={ok} err={(ok ? 0 : Marshal.GetLastWin32Error())} {Hex(b, n)}");
                    }
                    break;
                }
            case "setfeature":
                {
                    var b = a.Skip(2).Select(x => Convert.ToByte(x, 16)).ToArray();
                    foreach (var p in Paths(filter))
                    {
                        using var h = Open(p); if (h.IsInvalid) continue;
                        bool ok = HidD_SetFeature(h, b, b.Length);
                        Console.WriteLine($"{p}\n  ok={ok} err={(ok ? 0 : Marshal.GetLastWin32Error())}");
                    }
                    break;
                }
        }
    }
}
