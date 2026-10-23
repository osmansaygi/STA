// Salt okunur minidump incelemesi: istisna kodu/adresi ve istisna thread'inin yığın
// slotlarındaki modül adresleri (dbghelp export sembolleri). Çözülmüş yığın değildir.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

static class Program
{
    [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SymInitializeW(IntPtr p, string s, bool i);
    [DllImport("dbghelp.dll")] static extern uint SymSetOptions(uint o);
    [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ulong SymLoadModuleExW(IntPtr p, IntPtr f, string img, string mod, ulong b, uint sz, IntPtr d, uint fl);
    [DllImport("dbghelp.dll", SetLastError = true)] static extern bool SymFromAddr(IntPtr p, ulong a, out ulong disp, IntPtr sym);

    static byte[] data;
    static uint U32(long at) => BitConverter.ToUInt32(data, (int)at);
    static ulong U64(long at) => BitConverter.ToUInt64(data, (int)at);
    static readonly List<(ulong B, uint S, string P)> mods = new List<(ulong, uint, string)>();
    static readonly HashSet<ulong> loaded = new HashSet<ulong>();
    static readonly IntPtr proc = new IntPtr(0x5e1f);

    static string Describe(ulong a)
    {
        foreach (var m in mods)
        {
            if (a < m.B || a >= m.B + m.S) continue;
            if (loaded.Add(m.B)) SymLoadModuleExW(proc, IntPtr.Zero, m.P, null, m.B, m.S, IntPtr.Zero, 0);
            string sym = "";
            IntPtr buf = Marshal.AllocHGlobal(1200);
            try
            {
                for (int i = 0; i < 1200; i++) Marshal.WriteByte(buf, i, 0);
                Marshal.WriteInt32(buf, 0, 88); Marshal.WriteInt32(buf, 80, 1000);
                if (SymFromAddr(proc, a, out ulong d, buf))
                {
                    int len = Marshal.ReadInt32(buf, 76);
                    var bytes = new byte[len]; Marshal.Copy(buf + 84, bytes, 0, len);
                    sym = " " + Encoding.UTF8.GetString(bytes) + "+0x" + d.ToString("x");
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return Path.GetFileName(m.P) + "+0x" + (a - m.B).ToString("x") + sym;
        }
        return null;
    }

    static void Main(string[] args)
    {
        data = File.ReadAllBytes(args[0]);
        int max = args.Length > 1 ? int.Parse(args[1]) : 80;
        var streams = new Dictionary<uint, (uint Size, uint Rva)>();
        for (uint i = 0; i < U32(8); i++) { long at = U32(12) + 12 * i; streams[U32(at)] = (U32(at + 4), U32(at + 8)); }
        long ml = streams[4].Rva;
        for (uint i = 0; i < U32(ml); i++)
        {
            long m = ml + 4 + 108 * i; uint name = U32(m + 20);
            mods.Add((U64(m), U32(m + 8), Encoding.Unicode.GetString(data, (int)name + 4, (int)U32(name))));
        }
        SymSetOptions(0x2 | 0x4 | 0x200);
        SymInitializeW(proc, "", false);
        long exc = streams[6].Rva; uint tid = U32(exc); long ctx = U32(exc + 164);
        ulong rsp = U64(ctx + 152), rip = U64(ctx + 248);
        Console.WriteLine($"Exception code=0x{U32(exc + 8):x} param0=0x{U64(exc + 8 + 24):x} thread={tid} RIP={Describe(rip)}");
        long tl = streams[3].Rva;
        for (uint i = 0; i < U32(tl); i++)
        {
            long t = tl + 4 + 48 * i;
            if (U32(t) != tid) continue;
            ulong sb = U64(t + 24); uint ss = U32(t + 32), sr = U32(t + 36);
            int count = 0;
            for (ulong off = rsp > sb ? rsp - sb : 0; off + 8 <= ss; off += 8)
            {
                string d = Describe(U64(sr + (long)off));
                if (d == null) continue;
                Console.WriteLine($"  RSP+0x{sb + off - rsp:x} {d}");
                if (++count >= max) break;
            }
        }
    }
}
