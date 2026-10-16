using Iced.Intel;

static class Dis
{
    class Sec { public string Name; public uint Va, VSize, Raw, RawSize; }
    static byte[] data; static List<Sec> secs; static ulong imageBase;

    static void Load(string path)
    {
        data = File.ReadAllBytes(path);
        int pe = BitConverter.ToInt32(data, 0x3C);
        int nsec = BitConverter.ToUInt16(data, pe + 6), optSize = BitConverter.ToUInt16(data, pe + 20);
        imageBase = BitConverter.ToUInt64(data, pe + 24 + 24);
        secs = new();
        int so = pe + 24 + optSize;
        for (int i = 0; i < nsec; i++)
        {
            int o = so + i * 40;
            secs.Add(new Sec { Name = System.Text.Encoding.ASCII.GetString(data, o, 8).TrimEnd('\0'), VSize = BitConverter.ToUInt32(data, o + 8), Va = BitConverter.ToUInt32(data, o + 12), RawSize = BitConverter.ToUInt32(data, o + 16), Raw = BitConverter.ToUInt32(data, o + 20) });
        }
    }
    static int Off(ulong va) { uint rva = (uint)(va - imageBase); foreach (var s in secs) if (rva >= s.Va && rva < s.Va + Math.Max(s.VSize, s.RawSize)) return (int)(s.Raw + rva - s.Va); return -1; }
    static ulong Va(int off) { foreach (var s in secs) if (off >= s.Raw && off < s.Raw + s.RawSize) return imageBase + s.Va + (uint)(off - s.Raw); return 0; }

    public static void StrAt(byte[] _, string path, ulong va)
    {
        Load(path); int off = Off(va); if (off < 0) { Console.WriteLine($"{va:X}: n/a"); return; }
        int e = off; while (e < data.Length && data[e] != 0 && e - off < 200) e++;
        var s = System.Text.Encoding.ASCII.GetString(data, off, e - off);
        if (s.Length < 2) { e = off; while (e + 1 < data.Length && (data[e] != 0 || data[e + 1] != 0) && e - off < 400) e += 2; s = System.Text.Encoding.Unicode.GetString(data, off, e - off); }
        Console.WriteLine($"{va:X}: \"{s}\"");
    }

    public static void Disasm(string path, ulong va, int len)
    {
        Load(path);
        int off = Off(va); if (off < 0) { Console.WriteLine("va not in file"); return; }
        var rd = new ByteArrayCodeReader(data, off, len);
        var dec = Decoder.Create(64, rd); dec.IP = va;
        var fmt = new NasmFormatter(); fmt.Options.FirstOperandCharIndex = 10;
        var o = new StringOutput();
        while (rd.CanReadByte)
        {
            dec.Decode(out var ins); if (ins.IsInvalid) { Console.WriteLine($"{ins.IP:X}: ??"); continue; }
            fmt.Format(in ins, o); Console.WriteLine($"{ins.IP:X}: {o.ToStringAndReset()}");
        }
    }

    // find direct calls (E8) / jumps (E9) to va in executable sections
    public static void Callers(string path, ulong target, int ctx)
    {
        Load(path);
        var text = secs.Where(s => s.Name == ".text").ToList();
        foreach (var s in text)
            for (long i = s.Raw; i < s.Raw + s.RawSize - 5; i++)
                if (data[i] == 0xE8)
                {
                    ulong ip = imageBase + s.Va + (ulong)(i - s.Raw);
                    ulong tgt = ip + 5 + (ulong)(long)BitConverter.ToInt32(data, (int)i + 1);
                    if (tgt == target) { Console.WriteLine($"\n== caller at {ip:X}"); Disasm(path, ip - (ulong)ctx, ctx + 5); Load(path); }
                }
    }
}
static class DisExt { }
