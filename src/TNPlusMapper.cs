// Terra Nova Plus - DOSBox key map. A DOSBox key map (mapperfile) that moves keyboard keys stacks with the KEYBOARD
// tab: both remap, so the keys end up somewhere else (an AZERTY map putting strafe on Q/D, plus the AZERTY layout of
// the tab, made Q turn). When the tool starts the game it gives DOSBox a copy of that map with every keyboard key
// back in its place, gamepad and joystick bindings kept. Attached to a DOSBox started elsewhere, it warns.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

static partial class TNPlus
{
    // DOSBox Staging's own map: DOS key -> host key (SDL scancode, the key's place on a US keyboard)
    static readonly Dictionary<string, int> MAPPER_KEYS = ParseMapperDefaults(
        "esc 41 f1 58 f2 59 f3 60 f4 61 f5 62 f6 63 f7 64 f8 65 f9 66 f10 67 f11 68 f12 69 grave 53 1 30 2 31 3 32 4 33 " +
        "5 34 6 35 7 36 8 37 9 38 0 39 minus 45 equals 46 bspace 42 tab 43 q 20 w 26 e 8 r 21 t 23 y 28 u 24 i 12 o 18 " +
        "p 19 lbracket 47 rbracket 48 enter 40 capslock 57 a 4 s 22 d 7 f 9 g 10 h 11 j 13 k 14 l 15 semicolon 51 " +
        "quote 52 backslash 49 lshift 225 oem102 100 z 29 x 27 c 6 v 25 b 5 n 17 m 16 comma 54 period 55 slash 56 " +
        "abnt1 135 rshift 229 lctrl 224 lgui 227 lalt 226 space 44 ralt 230 rgui 231 rctrl 228 printscreen 70 " +
        "scrolllock 71 pause 72 insert 73 home 74 pageup 75 delete 76 end 77 pagedown 78 up 82 left 80 down 81 right 79 " +
        "numlock 83 kp_divide 84 kp_multiply 85 kp_minus 86 kp_7 95 kp_8 96 kp_9 97 kp_plus 87 kp_4 92 kp_5 93 kp_6 94 " +
        "kp_1 89 kp_2 90 kp_3 91 kp_enter 88 kp_0 98 kp_period 99");

    static Dictionary<string, int> ParseMapperDefaults(string s)
    {
        Dictionary<string, int> d = new Dictionary<string, int>();
        string[] t = s.Split(' ');
        for (int i = 0; i + 1 < t.Length; i += 2) d[t[i]] = int.Parse(t[i + 1]);
        return d;
    }

    // mapperfile of the [sdl] section of a DOSBox config, null when not set
    static string ConfMapper(string conf)
    {
        string found = null, section = "";
        try
        {
            foreach (string raw in File.ReadAllLines(conf))
            {
                string l = raw.Trim();
                if (l.StartsWith("[")) { section = l.ToLowerInvariant(); continue; }
                if (section != "[sdl]") continue;
                Match m = Regex.Match(l, @"^mapperfile\s*=\s*(.*)$", RegexOptions.IgnoreCase);
                if (m.Success) found = m.Groups[1].Value.Trim().Trim('"');
            }
        }
        catch { }
        return found == "" ? null : found;
    }

    // where DOSBox Staging finds a map given by name: as is, its config folder, next to it, its bundled maps
    static string ResolveMapper(string name, string dosboxDir)
    {
        if (name == null) return null;
        List<string> c = new List<string>();
        if (Path.IsPathRooted(name)) c.Add(name);
        else
        {
            c.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DOSBox", name));
            if (dosboxDir != null) { c.Add(Path.Combine(dosboxDir, name)); c.Add(Path.Combine(dosboxDir, "resources", "mapperfiles", name)); }
        }
        foreach (string p in c) if (File.Exists(p)) return p;
        return null;
    }

    // the map DOSBox uses with these configs (the last one setting it wins), else the one of its primary config
    static string EffectiveMapper(List<string> confs, string dosboxDir)
    {
        string name = null;
        foreach (string f in confs) { string v = ConfMapper(f); if (v != null) name = v; }
        if (name == null)
            name = ConfMapper(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DOSBox", "dosbox-staging.conf"));
        return ResolveMapper(name, dosboxDir);
    }

    // DOS keys whose keyboard binding is not DOSBox's own (moved, added or removed)
    static List<string> MapperMoves(string map)
    {
        List<string> moved = new List<string>();
        try
        {
            foreach (string raw in File.ReadAllLines(map))
            {
                Match m = Regex.Match(raw.Trim(), @"^key_(\S+)\s*(.*)$");
                int def;
                if (!m.Success || !MAPPER_KEYS.TryGetValue(m.Groups[1].Value, out def)) continue;
                List<string> keys = new List<string>();
                foreach (Match t in Regex.Matches(m.Groups[2].Value, "\"([^\"]*)\""))
                    if (t.Groups[1].Value.StartsWith("key ")) keys.Add(t.Groups[1].Value.Trim());
                if (keys.Count != 1 || keys[0] != "key " + def) moved.Add(m.Groups[1].Value.ToUpperInvariant());
            }
        }
        catch { }
        return moved;
    }

    // copy of the map with every keyboard key back in its place, its other bindings (gamepad, joystick) kept
    static string NeutralMapper(string map)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string raw in File.ReadAllLines(map))
        {
            Match m = Regex.Match(raw.Trim(), @"^key_(\S+)\s*(.*)$");
            int def;
            if (!m.Success || !MAPPER_KEYS.TryGetValue(m.Groups[1].Value, out def)) { sb.Append(raw).Append("\r\n"); continue; }
            sb.Append("key_").Append(m.Groups[1].Value).Append(" \"key ").Append(def).Append("\" ");
            foreach (Match t in Regex.Matches(m.Groups[2].Value, "\"([^\"]*)\""))
                if (!t.Groups[1].Value.StartsWith("key ")) sb.Append('"').Append(t.Groups[1].Value).Append("\" ");
            sb.Append("\r\n");
        }
        string path = Path.Combine(ExeDir, "TNPlus_keys.map");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    static bool KeysChanged() { return GameKeys.Exists(g => g.Changed && !g.Fixed); }

    // launch: [sdl] mapperfile for the tool's config when the game's map moves keys and the KEYBOARD tab changes some
    static string LaunchMapper(string db)
    {
        if (!KeysChanged()) return "";
        string map = EffectiveMapper(new List<string> { Path.Combine(db, "dosbox_terranova_windows.conf") }, db);
        if (map == null) return "";
        List<string> moved = MapperMoves(map);
        if (moved.Count == 0) return "";
        try
        {
            string copy = NeutralMapper(map);
            Console.WriteLine("DOSBox key map " + Path.GetFileName(map) + " moves " + string.Join(" ", moved.ToArray()) +
                ": the game starts with a copy that leaves the keyboard to the KEYBOARD tab (" + Path.GetFileName(copy) +
                ", gamepad bindings kept).");
            return "[sdl]\r\nmapperfile = " + copy + "\r\n";
        }
        catch (Exception e)
        {
            Console.WriteLine("DOSBox key map " + Path.GetFileName(map) + " moves keys and could not be copied (" + e.Message +
                "): choose the GAME layout in the KEYBOARD tab.");
            return "";
        }
    }

    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr buf, int len, out int ret);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

    static string ProcessCommandLine(IntPtr h)
    {
        int len = 0x4000;
        for (int tries = 0; tries < 2; tries++)
        {
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                int ret;
                if (NtQueryInformationProcess(h, 60, buf, len, out ret) == 0)   // ProcessCommandLineInformation
                {
                    int n = Marshal.ReadInt16(buf) & 0xffff;                   // UNICODE_STRING: length in bytes, buffer
                    return Marshal.PtrToStringUni(Marshal.ReadIntPtr(buf, IntPtr.Size), n / 2);
                }
                if (ret <= len) return null;
                len = ret;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return null;
    }

    // attached to a DOSBox the tool did not start: its configs come from its command line
    static void WarnAttachedMapper(IntPtr h)
    {
        if (!KeysChanged()) return;
        try
        {
            StringBuilder sb = new StringBuilder(1024); int size = sb.Capacity;
            if (!QueryFullProcessImageName(h, 0, sb, ref size)) return;
            string dir = Path.GetDirectoryName(sb.ToString()), cmd = ProcessCommandLine(h);
            if (cmd == null) return;
            List<string> confs = new List<string>();
            foreach (Match m in Regex.Matches(cmd, "-conf\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase))
            {
                string f = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                confs.Add(Path.IsPathRooted(f) ? f : Path.Combine(dir, f));
            }
            string map = EffectiveMapper(confs, dir);
            if (map == null) return;
            List<string> moved = MapperMoves(map);
            if (moved.Count == 0) return;
            Say("Warning: this DOSBox's key map " + Path.GetFileName(map) + " already moves " + string.Join(" ", moved.ToArray()) +
                ". With the KEYBOARD tab on top, those keys end up elsewhere: start the game from this tool, " +
                "or choose the GAME layout.", 300, 300);
        }
        catch { }
    }
}
