// Terra Nova Plus - automatic tests: what can be checked without the game. Built by tests\run_tests.bat from the same
// sources as TNPlus.exe (the .cs files of build.bat's compiler line) plus tests\*.cs, with this entry point; exit code
// 1 when a test fails. "TNPlusTests.exe Keys" runs the tests whose name contains Keys.
//
// A test is a static void method marked [Test] in TNPlus.Tests (any file of tests\): as a nested class of TNPlus it
// calls the tool's own code directly. Each test runs in an AppDomain of its own, so it starts from the program's
// defaults (its settings and state are all static), with an empty temporary folder as the tool's own folder (ExeDir:
// TNPlus.ini, the launch conf, the save backups...). Game files and game memory are made up by the tests: nothing
// from the game is needed, nothing from it is ever added to the repository.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

[AttributeUsage(AttributeTargets.Method)]
sealed class TestAttribute : Attribute { }

static class TNPlusTestMain
{
    static int Main(string[] args) { return TNPlus.Tests.RunAll(args); }
}

static partial class TNPlus
{
    internal static partial class Tests
    {
        const int TIMEOUT_MS = 60000;               // one test; past that the run stops (failed)

        // ------------------------------------------------------------------ runner (the default AppDomain)
        internal static int RunAll(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            List<MethodInfo> tests = new List<MethodInfo>();
            foreach (MethodInfo m in typeof(Tests).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.IsDefined(typeof(TestAttribute), false) && (filter == null || m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                    tests.Add(m);
            tests.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));   // the order of the files and of the code
            bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            Console.WriteLine("Terra Nova Plus tests: " + tests.Count + " (" + Environment.OSVersion + ", CLR " + Environment.Version +
                ", " + (IntPtr.Size * 8) + "-bit)" + (filter != null ? ", filter \"" + filter + "\"" : ""));
            if (tests.Count == 0) { Console.WriteLine("No test found."); return 1; }

            string exe = Assembly.GetExecutingAssembly().Location;
            // the exe and its side files (debug symbols: the failures give the test's line)
            string[] files = Directory.GetFiles(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + ".*");
            string root = Path.Combine(Path.GetTempPath(), "TNPlusTests-" + Process.GetCurrentProcess().Id);
            int failed = 0, skipped = 0, n = 0;
            Stopwatch all = Stopwatch.StartNew();
            foreach (MethodInfo m in tests)
            {
                n++;
                string result;
                if (!m.IsStatic || m.ReturnType != typeof(void) || m.GetParameters().Length != 0 || m.IsGenericMethodDefinition)
                    result = "FAIL a test is a static void method without parameters";
                else
                {
                    // the test's own folder: the tool's folder for it (a copy of this exe, that its AppDomain loads)
                    string dir = Path.Combine(root, n.ToString("0000"));
                    Directory.CreateDirectory(dir);
                    foreach (string f in files) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
                    result = RunInDomain(dir, m.Name);
                    DeleteDir(dir);
                }
                if (result.StartsWith("SKIP") && windows) result = "FAIL skipped on Windows: " + result.Substring(4).Trim();
                if (result.StartsWith("PASS")) Console.WriteLine("  ok    " + m.Name);
                else if (result.StartsWith("SKIP")) { skipped++; Console.WriteLine("  skip  " + m.Name + ": " + result.Substring(4).Trim()); }
                else
                {
                    failed++;
                    Console.WriteLine("  FAIL  " + m.Name);
                    foreach (string l in result.Substring(4).Trim().Split('\n')) Console.WriteLine("        " + l.TrimEnd('\r'));
                }
                if (result.StartsWith("FAIL TIMEOUT")) break;
            }
            DeleteDir(root);
            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "ALL " + (n - skipped) + " TESTS PASSED" + (skipped > 0 ? " (" + skipped + " skipped: Windows only)" : "") + " in " + all.Elapsed.TotalSeconds.ToString("0.0") + " s"
                : failed + " OF " + n + " TESTS FAILED");
            return failed == 0 && n == tests.Count ? 0 : 1;
        }

        // one test in a new AppDomain whose base folder (AppDomain.CurrentDomain.BaseDirectory = ExeDir) is dir
        static string RunInDomain(string dir, string name)
        {
            AppDomainSetup setup = new AppDomainSetup();
            setup.ApplicationBase = dir;
            AppDomain d = AppDomain.CreateDomain("TNPlus test " + name, null, setup);
            string result = null;
            Thread t = new Thread(delegate()
            {
                try
                {
                    Runner r = (Runner)d.CreateInstanceAndUnwrap(typeof(Runner).Assembly.FullName, typeof(Runner).FullName, false,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, null, null, null);
                    result = r.Run(name);
                }
                catch (Exception e) { result = "FAIL test harness: " + e.GetType().Name + ": " + e.Message; }
            });
            t.IsBackground = true;
            t.Start();
            if (!t.Join(TIMEOUT_MS)) return "FAIL TIMEOUT: still running after " + TIMEOUT_MS / 1000 + " s (the run stops here)";
            try { AppDomain.Unload(d); } catch { }
            return result ?? "FAIL test harness: no result";
        }

        static void DeleteDir(string dir)
        {
            for (int i = 0; i < 3; i++)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
                catch { Thread.Sleep(100); }
            }
        }

        // runs the test in its AppDomain; "PASS", "SKIP why" or "FAIL what" (with the line of the test and the console)
        public sealed class Runner : MarshalByRefObject
        {
            public string Run(string name)
            {
                StringWriter console = new StringWriter();
                Console.SetOut(console); Console.SetError(console);   // what the tool prints (Say...), shown on a failure
                MethodInfo m = typeof(Tests).GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Exception error = null;
                try { m.Invoke(null, null); }
                catch (TargetInvocationException e) { error = e.InnerException ?? e; }
                catch (Exception e) { error = e; }
                finally { if (fake != null) fake.Dispose(); }
                if (error == null) return "PASS";
                if (error is Skipped) return "SKIP " + error.Message;
                StringBuilder sb = new StringBuilder("FAIL ");
                sb.Append(error is Failure ? error.Message : error.GetType().Name + ": " + error.Message);
                string at = TestLine(error);
                if (at != null) sb.Append("\n(" + at + ")");
                if (!(error is Failure)) sb.Append("\n" + error.StackTrace);
                string said = console.ToString().Trim();
                if (said != "")
                {
                    string[] lines = said.Split('\n');
                    sb.Append("\nconsole:");
                    for (int i = Math.Max(0, lines.Length - 12); i < lines.Length; i++) sb.Append("\n  " + lines[i].TrimEnd('\r'));
                }
                return sb.ToString();
            }

            static string TestLine(Exception e)
            {
                foreach (StackFrame f in new StackTrace(e, true).GetFrames() ?? new StackFrame[0])
                {
                    MethodBase mb = f.GetMethod();
                    if (mb != null && mb.IsDefined(typeof(TestAttribute), false))
                        return mb.Name + (f.GetFileName() != null ? ", " + Path.GetFileName(f.GetFileName()) + " line " + f.GetFileLineNumber() : "");
                }
                return null;
            }
        }

        // ------------------------------------------------------------------ assertions
        sealed class Failure : Exception { public Failure(string m) : base(m) { } }
        sealed class Skipped : Exception { public Skipped(string m) : base(m) { } }

        static void Fail(string what) { throw new Failure(what); }
        static void Skip(string why) { throw new Skipped(why); }
        static void Ok(bool cond, string what) { if (!cond) Fail(what); }
        static void No(bool cond, string what) { if (cond) Fail(what); }

        static void Eq<T>(T expected, T actual, string what)
        {
            if (!Equals(expected, actual)) Fail(what + ": expected " + Show(expected) + ", got " + Show(actual));
        }

        static void EqBytes(byte[] expected, byte[] actual, string what)
        {
            if (expected == null || actual == null) { if (expected != actual) Fail(what + ": expected " + Show(expected) + ", got " + Show(actual)); return; }
            int n = Math.Min(expected.Length, actual.Length), i = 0;
            while (i < n && expected[i] == actual[i]) i++;
            if (i == n && expected.Length == actual.Length) return;
            Fail(what + ": " + (i < n ? "first difference at byte " + i + " (0x" + i.ToString("X") + "): expected " + expected[i].ToString("X2") + ", got " + actual[i].ToString("X2")
                : "expected " + expected.Length + " bytes, got " + actual.Length) +
                (Math.Max(expected.Length, actual.Length) <= 64 ? "\n  expected " + HexOf(expected) + "\n  got      " + HexOf(actual) : ""));
        }

        static void EqList<T>(IList<T> expected, IList<T> actual, string what)
        {
            if (expected.Count != actual.Count) Fail(what + ": expected " + expected.Count + " items " + Show(expected) + ", got " + actual.Count + " " + Show(actual));
            for (int i = 0; i < expected.Count; i++)
                if (!Equals(expected[i], actual[i])) Fail(what + ": item " + i + ": expected " + Show(expected[i]) + ", got " + Show(actual[i]));
        }

        static void Has(string text, string part, string what)
        {
            if (text == null || text.IndexOf(part, StringComparison.Ordinal) < 0) Fail(what + ": " + Show(part) + " not in " + Show(text));
        }

        static void HasNot(string text, string part, string what)
        {
            if (text != null && text.IndexOf(part, StringComparison.Ordinal) >= 0) Fail(what + ": " + Show(part) + " should not be in " + Show(text));
        }

        static T Throws<T>(Action a, string what) where T : Exception
        {
            try { a(); }
            catch (T e) { return e; }
            catch (Exception e) { Fail(what + ": expected " + typeof(T).Name + ", got " + e.GetType().Name + ": " + e.Message); }
            Fail(what + ": expected " + typeof(T).Name + ", nothing thrown");
            return null;
        }

        static string Show(object o)
        {
            if (o == null) return "null";
            string s = o as string;
            if (s != null)
                return "\"" + (s.Length > 600 ? s.Substring(0, 600) + "..." : s).Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\0", "\\0") + "\"";
            byte[] b = o as byte[];
            if (b != null) return b.Length <= 64 ? "[" + HexOf(b) + "]" : "[" + b.Length + " bytes]";
            if (o is char) return "'" + o + "'";
            if (o is uint) return "0x" + ((uint)o).ToString("X");
            IEnumerable e = o as IEnumerable;
            if (e != null)
            {
                List<string> items = new List<string>();
                foreach (object x in e) { if (items.Count == 20) { items.Add("..."); break; } items.Add(Show(x)); }
                return "{ " + string.Join(", ", items.ToArray()) + " }";
            }
            return o.ToString();
        }

        // ------------------------------------------------------------------ helpers
        // bytes from hex ("8B15 (....)" style spaces allowed)
        static byte[] Bytes(string hex)
        {
            hex = hex.Replace(" ", "");
            byte[] b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);
            return b;
        }

        static string HexOf(byte[] b) { return BitConverter.ToString(b).Replace("-", ""); }

        // a path in the test's own folder (ExeDir), its parent folders made
        static string TestDir(params string[] parts)
        {
            string p = ExeDir;
            foreach (string s in parts) p = Path.Combine(p, s);
            Directory.CreateDirectory(p);
            return p;
        }

        static string PutFile(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        static string PutFile(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
            return path;
        }

        // ------------------------------------------------------------------ game memory, made up
        // A pinned buffer of this process stands for DOSBox's guest RAM (guest address 0 = its first byte): the tool's own
        // Read / Write / Scan go through ReadProcessMemory / WriteProcessMemory on this process, as on DOSBox. Windows
        // only (kernel32); the test is skipped elsewhere (never on Windows: the runner counts that as a failure).
        static FakeGame fake;                       // disposed by the runner after the test
        internal sealed class FakeGame : IDisposable
        {
            public readonly byte[] Mem;
            GCHandle pin;

            public FakeGame() : this(0x480000) { }  // up to past the free area of the prepared exe (0x465000-0x46B000)
            public FakeGame(int size)
            {
                Mem = new byte[size];
                pin = GCHandle.Alloc(Mem, GCHandleType.Pinned);
                guestBase = regStart = pin.AddrOfPinnedObject().ToInt64(); regSize = size;
                hProc = new IntPtr(-1);             // GetCurrentProcess()
                gamePid = Process.GetCurrentProcess().Id;
                Sound = false;                      // no beeps from Say
                fake = this;
                try
                {
                    Mem[0] = 0x5A;
                    byte[] probe = RawRead(guestBase, 1);
                    if (probe == null || probe[0] != 0x5A) throw new Exception("ReadProcessMemory on this process failed");
                    Mem[0] = 0;
                }
                catch (DllNotFoundException) { Dispose(); Skip("made-up game memory (kernel32 ReadProcessMemory): Windows only"); }
                catch (EntryPointNotFoundException) { Dispose(); Skip("made-up game memory (kernel32 ReadProcessMemory): Windows only"); }
            }

            public void Put(uint at, byte[] b) { Array.Copy(b, 0, Mem, (int)at, b.Length); }
            public void Put(uint at, string hex) { Put(at, Bytes(hex)); }
            public void PutInt(uint at, int v) { Put(at, BitConverter.GetBytes(v)); }
            public void PutUInt(uint at, uint v) { Put(at, BitConverter.GetBytes(v)); }
            public void PutShort(uint at, short v) { Put(at, BitConverter.GetBytes(v)); }
            public byte[] Get(uint at, int n) { byte[] b = new byte[n]; Array.Copy(Mem, (int)at, b, 0, n); return b; }
            public int GetInt(uint at) { return BitConverter.ToInt32(Mem, (int)at); }
            public uint GetUInt(uint at) { return BitConverter.ToUInt32(Mem, (int)at); }
            public short GetShort(uint at) { return BitConverter.ToInt16(Mem, (int)at); }
            public bool AllZero(uint at, int n) { for (int i = 0; i < n; i++) if (Mem[at + i] != 0) return false; return true; }

            public void Dispose()
            {
                if (pin.IsAllocated) pin.Free();
                hProc = IntPtr.Zero;
                if (fake == this) fake = null;
            }
        }
    }
}
