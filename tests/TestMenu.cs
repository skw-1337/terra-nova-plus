// Terra Nova Plus tests - true 16:9: the routine that centres the 640x480 screens (menus, briefings, their videos) in
// DOSBox's 848x480 mode. What the routine itself does in the game is checked by tools/menu_wide.py (python
// menu_wide.py test, in an emulator); here: when and where the tool writes it.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

static partial class TNPlus
{
    internal static partial class Tests
    {
        const int MENU_EN = 0, MENU_FR = 1;

        // the game as the tool looks for it: the call site in the 2D library, the driver table with its free first slot
        static void MenuGame(FakeGame g, int lang)
        {
            g.Put(MenuWide.Site[lang], MenuWide.SiteSig);
            g.PutUInt(MenuWide.Slot[lang] + 4, 0x308FCC);
            g.PutUInt(MenuWide.Slot[lang] + 8, MenuWide.SetMode[lang]);
        }

        static void MenuTrueWide()
        {
            Process me = Process.GetCurrentProcess();   // stands for the DOSBox started from the menu
            OptHD = true; SetWideMode(3);
            launched = me; gamePid = me.Id; launchedTrue = true;
        }

        [Test] static void Menu_Code_BothExecutables()
        {
            Eq(2, MenuWide.CodeHex.Length, "English and French");
            byte[] en = Bytes(MenuWide.CodeHex[MENU_EN]), fr = Bytes(MenuWide.CodeHex[MENU_FR]);
            Eq(en.Length, fr.Length, "same routine, other addresses");
            // after the code of both executables (it ends at 0x354940 in the English one, 0x354BC0 in the French one),
            // inside its last page
            Ok(MenuWide.Code >= 0x354BC0 && MenuWide.Code + fr.Length <= 0x355000, "in the unused end of the last code page");
            for (int l = 0; l < 2; l++)
            {
                byte[] c = l == MENU_EN ? en : fr;
                EqBytes(Bytes("6683FB14"), new[] { c[0], c[1], c[2], c[3] }, "starts with cmp bx, 20 (the driver's 640x480 mode)");
                Eq((byte)0xC3, c[c.Length - 1], "ends with ret");
                string hex = MenuWide.CodeHex[l];
                Has(hex, "FF15" + HexOf(BitConverter.GetBytes(MenuWide.Slot[l] + 8)), "sets the mode again through the driver's own entry");
                Has(hex, HexOf(BitConverter.GetBytes(MenuWide.Vbe[l])), "the VBE number of the driver's mode 20");
                Has(hex, "CD31", "the scan line length through DPMI, like the driver");
                Has(hex, HexOf(BitConverter.GetBytes(MenuWide.Slot[l])), "takes itself out of its slot when DOSBox refuses the mode");
            }
            Eq(0x364E98u + 12 * 20 + 2, MenuWide.Vbe[MENU_FR], "French: entry 20 of the driver's mode table");
            Eq(0x364DF0u + 12 * 20 + 2, MenuWide.Vbe[MENU_EN], "English");
            Eq(20, MenuWide.Menus, "mode"); Eq(0x101, MenuWide.VbeGame, "640x480"); Eq(0x222, MenuWide.VbeWide, "848x480");
        }

        [Test] static void Menu_Place_FrenchGame()
        {
            using (FakeGame g = new FakeGame())
            {
                MenuGame(g, MENU_FR);
                Eq(1, PlaceMenuWide(), "placed");
                EqBytes(Bytes(MenuWide.CodeHex[MENU_FR]), g.Get(MenuWide.Code, MenuWide.CodeHex[MENU_FR].Length / 2), "the French routine");
                Eq(MenuWide.Code, g.GetUInt(MenuWide.Slot[MENU_FR]), "the driver's free slot points to it");
                Eq(MenuWide.SetMode[MENU_FR], g.GetUInt(MenuWide.Slot[MENU_FR] + 8), "the driver's entries untouched");
                Eq(0u, g.GetUInt(MenuWide.Slot[MENU_EN]), "nothing at the English addresses");
                Eq(MENU_FR, menuLang, "language");
                Eq(1, PlaceMenuWide(), "already there: nothing to do");
            }
        }

        [Test] static void Menu_Place_EnglishGame()
        {
            using (FakeGame g = new FakeGame())
            {
                MenuGame(g, MENU_EN);
                Eq(1, PlaceMenuWide(), "placed");
                EqBytes(Bytes(MenuWide.CodeHex[MENU_EN]), g.Get(MenuWide.Code, MenuWide.CodeHex[MENU_EN].Length / 2), "the English routine");
                Eq(MenuWide.Code, g.GetUInt(MenuWide.Slot[MENU_EN]), "slot");
                Eq(0u, g.GetUInt(MenuWide.Slot[MENU_FR]), "nothing at the French addresses");
                Eq(MENU_EN, menuLang, "language");
            }
        }

        [Test] static void Menu_Place_WaitsForTheGameToLoad()
        {
            using (FakeGame g = new FakeGame())
            {
                Eq(0, PlaceMenuWide(), "no game code yet");
                g.Put(MenuWide.Site[MENU_FR], MenuWide.SiteSig);
                Eq(0, PlaceMenuWide(), "code loaded, its data not yet");
                g.PutUInt(MenuWide.Slot[MENU_FR] + 8, 0x000C4710);        // as in the file: not relocated yet
                Eq(0, PlaceMenuWide(), "driver table not relocated");
                Ok(g.AllZero(MenuWide.Code, 0x400), "nothing written while waiting");
                Eq(0u, g.GetUInt(MenuWide.Slot[MENU_FR]), "slot left alone");
                g.PutUInt(MenuWide.Slot[MENU_FR] + 8, MenuWide.SetMode[MENU_FR]);
                Eq(1, PlaceMenuWide(), "loaded: placed");
            }
        }

        [Test] static void Menu_Place_NotTheMemoryWeKnow()
        {
            using (FakeGame g = new FakeGame())
            {
                MenuGame(g, MENU_FR);
                g.PutUInt(MenuWide.Slot[MENU_FR], 0x309999);
                Eq(-1, PlaceMenuWide(), "the slot is taken");
                Ok(g.AllZero(MenuWide.Code, 0x400), "nothing written");
                Eq(0x309999u, g.GetUInt(MenuWide.Slot[MENU_FR]), "slot left alone");
            }
            using (FakeGame g = new FakeGame())
            {
                MenuGame(g, MENU_FR);
                g.Put(MenuWide.Code + 100, "90");
                Eq(-1, PlaceMenuWide(), "the end of the code page is not empty");
                Eq(0u, g.GetUInt(MenuWide.Slot[MENU_FR]), "slot left alone");
            }
        }

        [Test] static void Menu_Place_RoutineWipedOrGone()
        {
            using (FakeGame g = new FakeGame())
            {
                // the slot is ours but the code is gone (written while the game was still being loaded)
                MenuGame(g, MENU_FR);
                g.PutUInt(MenuWide.Slot[MENU_FR], MenuWide.Code);
                Eq(1, PlaceMenuWide(), "written again");
                EqBytes(Bytes(MenuWide.CodeHex[MENU_FR]), g.Get(MenuWide.Code, MenuWide.CodeHex[MENU_FR].Length / 2), "routine");
                // the routine took itself out of the slot: DOSBox refused the 848x480 mode. Not put back.
                g.PutUInt(MenuWide.Slot[MENU_FR], 0);
                Eq(-1, PlaceMenuWide(), "took itself out");
                Eq(0u, g.GetUInt(MenuWide.Slot[MENU_FR]), "slot stays empty");
            }
        }

        [Test] static void Menu_MenusFor()
        {
            Process me = Process.GetCurrentProcess();
            No(MenusFor(me.Id), "no game started from the tool");
            MenuTrueWide();
            Ok(MenusFor(me.Id), "true 16:9 in 1280x720, DOSBox started by the tool");
            No(MenusFor(me.Id + 1), "another DOSBox");
            SetWideMode(2); Ok(MenusFor(me.Id), "true 16:9 in 848x480");
            SetWideMode(1); No(MenusFor(me.Id), "stretched 16:9: the whole picture is stretched there");
            SetWideMode(0); No(MenusFor(me.Id), "widescreen off");
            SetWideMode(3); OptHD = false; No(MenusFor(me.Id), "no HD: no true 16:9");
            OptHD = true; launchedTrue = false; No(MenusFor(me.Id), "DOSBox started without the true 16:9 overlay (vesa_modes = all)");
            launchedTrue = true; OptWideMenus = true; No(MenusFor(me.Id), "wide_menus = 1: stretched as before");
            OptWideMenus = false; launched = null; No(MenusFor(me.Id), "game closed");
            OptWideAttach = true; Ok(MenusFor(12345), "wide_attach (tests)");
        }

        [Test] static void Menu_Try_InPlaceBeforeTheFirstScreen()
        {
            using (FakeGame g = new FakeGame())
            {
                MenuGame(g, MENU_FR);
                g.PutInt(MenuWide.Mode[MENU_FR], -1);                     // the driver has not set a mode yet
                StringWriter said = new StringWriter(); Console.SetOut(said);
                TryMenuWide();
                Eq(1, menuState, "in place");
                Eq(MenuWide.Code, g.GetUInt(MenuWide.Slot[MENU_FR]), "slot");
                Has(said.ToString(), "16:9 menus ON: menus and briefings keep their proportions", "said");
                HasNot(said.ToString(), "from the next", "in time: nothing about waiting");
            }
        }

        [Test] static void Menu_Try_AlreadyPlacedEarly()
        {
            using (FakeGame g = new FakeGame())
            {
                MenuGame(g, MENU_EN);
                Eq(1, PlaceMenuWide(), "placed before the game was found (EarlyMenus)");
                // the game went on: its 640x480 mode is DOSBox's 848x480 now
                g.PutInt(MenuWide.Mode[MENU_EN], 20); g.PutShort(MenuWide.Vbe[MENU_EN], 0x222);
                StringWriter said = new StringWriter(); Console.SetOut(said);
                TryMenuWide();
                Eq(1, menuState, "in place");
                Has(said.ToString(), "16:9 menus ON", "said once the game is found");
                HasNot(said.ToString(), "from the next", "it is already at work");
            }
        }

        [Test] static void Menu_Try_TooLateForTheFirstMenu()
        {
            using (FakeGame g = new FakeGame())
            {
                MenuGame(g, MENU_FR);
                g.PutInt(MenuWide.Mode[MENU_FR], 20); g.PutShort(MenuWide.Vbe[MENU_FR], 0x101);   // already at its menu, in its own mode
                StringWriter said = new StringWriter(); Console.SetOut(said);
                TryMenuWide();
                Eq(1, menuState, "in place all the same: it acts at the next mode change");
                Has(said.ToString(), "from the next briefing or mission on", "said");
            }
        }

        [Test] static void Menu_Try_Unavailable()
        {
            using (FakeGame g = new FakeGame())
            {
                StringWriter said = new StringWriter(); Console.SetOut(said);
                for (int i = 0; i < 39; i++) TryMenuWide();
                Eq(0, menuState, "code not recognized: still looking (DOS/4GW may be loading it)");
                Eq("", said.ToString(), "nothing said yet");
                TryMenuWide();
                Eq(-1, menuState, "gives up");
                Has(said.ToString(), "16:9 menus unavailable: unsupported game version", "said");
            }
            using (FakeGame g = new FakeGame())
            {
                MenuGame(g, MENU_FR);
                g.PutUInt(MenuWide.Slot[MENU_FR], 0x309999);
                StringWriter said = new StringWriter(); Console.SetOut(said);
                menuState = 0; menuTries = 0;
                TryMenuWide();
                Eq(-1, menuState, "slot taken");
                Has(said.ToString(), "16:9 menus unavailable: unexpected game memory", "said");
            }
            using (FakeGame g = new FakeGame())
            {
                MenuGame(g, MENU_FR);
                g.Put(MenuWide.Code, MenuWide.CodeHex[MENU_FR]);          // the routine is there, out of its slot
                StringWriter said = new StringWriter(); Console.SetOut(said);
                menuState = 0; menuTries = 0;
                TryMenuWide();
                Eq(-1, menuState, "refused by DOSBox");
                Has(said.ToString(), "DOSBox refused its 848x480 mode", "said");
                Eq(0u, g.GetUInt(MenuWide.Slot[MENU_FR]), "not put back");
            }
        }

        [Test] static void Menu_Check_RoutineTookItselfOut()
        {
            using (FakeGame g = new FakeGame())
            {
                MenuGame(g, MENU_EN);
                TryMenuWide();
                Eq(1, menuState, "in place");
                StringWriter said = new StringWriter(); Console.SetOut(said);
                MenuWideCheck();
                Eq(1, menuState, "still in its slot");
                Eq("", said.ToString(), "nothing to say");
                g.PutUInt(MenuWide.Slot[MENU_EN], 0);                     // DOSBox refused 848x480 at the game's first menu
                MenuWideCheck();
                Eq(-1, menuState, "off");
                Has(said.ToString(), "16:9 menus OFF: DOSBox refused its 848x480 mode", "said");
            }
        }

        // ---------------------------------------------------------------- before the game is found
        [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr addr, IntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll")] static extern bool VirtualFree(IntPtr addr, IntPtr size, uint type);

        // A block of this process that looks like DOSBox's guest RAM from outside: a region of its own, the guest's
        // address 0 a little after its start (0x60 in DOSBox Staging), an interrupt table and the BIOS date.
        sealed class FakeDosbox : IDisposable
        {
            public readonly IntPtr Region;
            public readonly long Base;
            public FakeDosbox(int off = 0x60, bool bios = true)
            {
                Region = VirtualAlloc(IntPtr.Zero, new IntPtr(0x500000), 0x3000, 0x04);
                if (Region == IntPtr.Zero) Fail("VirtualAlloc");
                Base = Region.ToInt64() + off;
                if (!bios) return;
                for (int i = 0; i < 64; i++) Marshal.WriteInt16(new IntPtr(Base + i * 4 + 2), unchecked((short)0xF000));
                Put(0xFFFF5, System.Text.Encoding.ASCII.GetBytes("01/01/92"));
            }
            public void Put(uint at, byte[] b) { Marshal.Copy(b, 0, new IntPtr(Base + at), b.Length); }
            public void PutUInt(uint at, uint v) { Marshal.WriteInt32(new IntPtr(Base + at), unchecked((int)v)); }
            public uint GetUInt(uint at) { return unchecked((uint)Marshal.ReadInt32(new IntPtr(Base + at))); }
            public byte[] Get(uint at, int n) { byte[] b = new byte[n]; Marshal.Copy(new IntPtr(Base + at), b, 0, n); return b; }
            public void Game(int lang)
            {
                Put(MenuWide.Site[lang], Bytes(MenuWide.SiteSig));
                PutUInt(MenuWide.Slot[lang] + 8, MenuWide.SetMode[lang]);
            }
            public void Dispose() { VirtualFree(Region, IntPtr.Zero, 0x8000); }
        }

        [Test] static void Menu_FindGuestRam()
        {
            using (FakeDosbox d = new FakeDosbox())
            {
                hProc = new IntPtr(-1);
                try { Eq(d.Base, FindGuestRam(), "found by the BIOS date and the interrupt table"); }
                finally { hProc = IntPtr.Zero; }
            }
            using (FakeDosbox d = new FakeDosbox(0x1000, false))
            {
                hProc = new IntPtr(-1);
                try
                {
                    Eq(0L, FindGuestRam(), "no BIOS yet (DOSBox starting): not found");
                    d.Put(0xFFFF5, System.Text.Encoding.ASCII.GetBytes("01/01/92"));
                    Eq(0L, FindGuestRam(), "a date alone, no interrupt table: not it");
                }
                finally { hProc = IntPtr.Zero; }
            }
        }

        [Test] static void Menu_Early_PlacedAsSoonAsTheGameIsLoaded()
        {
            using (FakeDosbox d = new FakeDosbox())
            {
                MenuTrueWide();
                gamePid = 0;                                              // the game is not found yet (no TryAttach)
                Ok(EarlyMenus(0.0), "DOSBox is up, the game not loaded: waiting (TryAttach held back)");
                Ok(EarlyMenus(0.5), "still waiting");
                Eq(0u, d.GetUInt(MenuWide.Slot[MENU_FR]), "nothing written");
                d.Game(MENU_FR);
                No(EarlyMenus(0.6), "the game is loaded: placed, TryAttach can go");
                EqBytes(Bytes(MenuWide.CodeHex[MENU_FR]), d.Get(MenuWide.Code, MenuWide.CodeHex[MENU_FR].Length / 2), "the routine, in DOSBox's RAM");
                Eq(MenuWide.Code, d.GetUInt(MenuWide.Slot[MENU_FR]), "slot");
                Eq(IntPtr.Zero, hProc, "the tool is still not attached");
                No(EarlyMenus(0.7), "nothing more to do");
                EarlyClose();
                Eq(0, earlyPid, "closed");
            }
        }

        [Test] static void Menu_Early_OnlyForTrueWideMenus()
        {
            using (FakeDosbox d = new FakeDosbox())
            {
                d.Game(MENU_FR);
                No(EarlyMenus(0.0), "no game started from the tool");
                MenuTrueWide(); gamePid = 0;
                OptWideMenus = true;
                No(EarlyMenus(0.0), "wide_menus = 1: nothing");
                SetWideMode(1); OptWideMenus = false;
                No(EarlyMenus(0.0), "stretched 16:9: nothing");
                Eq(0u, d.GetUInt(MenuWide.Slot[MENU_FR]), "nothing written");
                Eq(0, earlyPid, "DOSBox not even opened");
            }
        }

        [Test] static void Menu_Early_GivesUp()
        {
            using (FakeDosbox d = new FakeDosbox())
            {
                MenuTrueWide(); gamePid = 0;
                Ok(EarlyMenus(100.0), "waiting");
                Ok(EarlyMenus(119.0), "still, 19 s later");
                No(EarlyMenus(120.5), "after 20 s TryAttach gets its turn (it keeps looking in between)");
                d.Game(MENU_FR);
                No(EarlyMenus(121.0), "the game shows up at last");
                Eq(MenuWide.Code, d.GetUInt(MenuWide.Slot[MENU_FR]), "placed all the same");
                EarlyClose();
            }
            using (FakeDosbox d = new FakeDosbox())
            {
                d.Game(MENU_FR);
                d.PutUInt(MenuWide.Slot[MENU_FR], 0x309999);              // not the memory we know
                MenuTrueWide(); gamePid = 0;
                No(EarlyMenus(0.0), "unavailable: no waiting");
                No(EarlyMenus(0.1), "and no second look");
                Eq(0x309999u, d.GetUInt(MenuWide.Slot[MENU_FR]), "left alone");
                EarlyClose();
            }
        }
    }
}
