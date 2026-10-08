// Terra Nova Plus - the launcher window (WinForms, .NET Framework 4, nothing to install), in the colours of
// the game's cockpit: dark teal panels with bevelled edges, blue tabs, amber text, LED-style switches and the
// orange buttons of the loadout screens. Same settings and same flow as the console menu in TNPlus.cs:
// GuiMenu() returns true to run the tool (game launched or attach mode) and false to quit. --console keeps
// the old text menu.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static partial class TNPlus
{
    // cockpit palette (from the game's HUD)
    static readonly Color TBack = Color.FromArgb(9, 17, 16), TPanel = Color.FromArgb(22, 41, 38), TPanelHi = Color.FromArgb(78, 134, 120), TPanelLo = Color.FromArgb(4, 10, 9);
    static readonly Color TText = Color.FromArgb(214, 226, 220), TDim = Color.FromArgb(122, 150, 142), TAmber = Color.FromArgb(240, 178, 48);
    static readonly Color TTab = Color.FromArgb(38, 76, 178), TLed = Color.FromArgb(56, 214, 92), TLedOff = Color.FromArgb(40, 58, 54), TRed = Color.FromArgb(236, 72, 60);
    static readonly Color TOrange = Color.FromArgb(214, 122, 28), TField = Color.FromArgb(12, 26, 24);
    static Font TFont, TSmall, TBold, TTitle, TTitle2, TMono, TKey;

    static Label guiHelp, guiWarn, guiPreset, guiKeys, lastBadge;
    static readonly List<KeyValuePair<Label, int>> keyBadges = new List<KeyValuePair<Label, int>>();
    static int rebindKey = -1;                      // KEY_WHAT index waiting for its new key, -1 none
    static GameKey rebindGame = null;               // game command waiting for its new key
    static readonly List<KeyValuePair<Label, GameKey>> gameBadges = new List<KeyValuePair<Label, GameKey>>();
    static readonly List<Control> setPage = new List<Control>();
    static Panel kbPage;
    static Sel cbLayout;
    static Label tabSet, tabKeys;
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);

    // a purple key badge: a click waits for the next key pressed (Esc cancels), stored as its scancode
    static void KeyBadge(Label b, int key)
    {
        keyBadges.Add(new KeyValuePair<Label, int>(b, key));
        b.Cursor = Cursors.Hand;
        b.MouseEnter += delegate { if (rebindKey < 0) guiHelp.Text = "> click to change the " + KEY_WHAT[key] + " key"; };
        b.Click += delegate
        {
            rebindGame = null;
            rebindKey = rebindKey == key ? -1 : key;
            GuiRefresh();
            guiHelp.Text = rebindKey < 0 ? "> unchanged" : "> press the new " + KEY_WHAT[key] + " key (Esc cancels)";
        };
    }

    class KeyCatcher : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            if ((rebindKey < 0 && rebindGame == null) || (m.Msg != 0x100 && m.Msg != 0x104)) return false;   // WM_(SYS)KEYDOWN
            long lp = m.LParam.ToInt64();
            int vk = (int)m.WParam.ToInt64(), scan = (int)((lp >> 16) & 0xFF);
            bool ext = ((lp >> 24) & 1) != 0;
            if (vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5)) return true;   // a modifier: wait for the key
            string msg;
            if (vk == 0x1B) msg = "unchanged";
            else if (rebindGame != null)
            {
                int mods = 0, n = 0;
                if (GetKeyState(0x10) < 0) { mods = 1; n++; }
                if (GetKeyState(0x11) < 0) { mods = 2; n++; }
                if (GetKeyState(0x12) < 0) { mods = 3; n++; }
                msg = n > 1 ? "one modifier at most (Alt, Ctrl or Shift): try again" : AssignGameKey(rebindGame, scan, mods, ext);
            }
            else msg = AssignToolKey(rebindKey, scan, ext);
            rebindKey = -1; rebindGame = null;
            SaveSettings();
            GuiRefresh();
            guiHelp.Text = "> " + msg;
            return true;
        }
    }

    // a key of the game's commands on the KEYBOARD page
    static void GameBadge(Label b, GameKey g)
    {
        gameBadges.Add(new KeyValuePair<Label, GameKey>(b, g));
        b.Cursor = g.Fixed ? Cursors.Default : Cursors.Hand;
        b.MouseEnter += delegate
        {
            if (rebindKey >= 0 || rebindGame != null) return;
            guiHelp.Text = "> " + g.Name.ToLowerInvariant() + ": " + ChordName(g.Sc, g.Mod) +
                (g.Changed ? " (the game: " + ChordName(g.DefSc, g.DefMod) + ")" : "") + (g.Fixed ? ", can't be changed" : ", click to change it");
        };
        b.Click += delegate
        {
            if (g.Fixed) return;
            rebindKey = -1;
            rebindGame = rebindGame == g ? null : g;
            GuiRefresh();
            guiHelp.Text = rebindGame == null ? "> unchanged" : "> press the new key for " + g.Name.ToLowerInvariant() + ", alone or with Alt, Ctrl or Shift (Esc cancels)";
        };
    }

    static Label KeyRow(Panel p, string name, int y)
    {
        Lbl(p, name.ToUpperInvariant(), 10, y + 1, TSmall, TAmber);
        Label b = new Label(); b.AutoSize = false; b.TextAlign = ContentAlignment.MiddleCenter; b.Font = TKey;
        b.Size = new Size(30, 17); b.Location = new Point(p.Width - 40, y); p.Controls.Add(b);
        b.BringToFront();                           // over the end of a long name, never under it
        return b;
    }

    // KEYBOARD page: the game's commands in 4 columns, the tool's keys, reset
    static void BuildKeysPage(Panel page)
    {
        int[] cw = { 150, 175, 190, page.Width - 3 * 8 - 150 - 175 - 190 };   // SQUAD: long names with ALT+
        string[][] cols = { new[] { "MOVEMENT", "WEAPONS" }, new[] { "VIEW", "MAP" }, new[] { "SQUAD" }, new[] { "DRONE", "SYSTEM", "TOOL" } };
        for (int c = 0; c < 4; c++)
        {
            int x = 0, y = 0;
            for (int k = 0; k < c; k++) x += cw[k] + 8;
            foreach (string grp in cols[c])
            {
                bool tool = grp == "TOOL";
                List<GameKey> keys = GameKeys.FindAll(g => g.Group == grp);
                int rows = tool ? KEY_WHAT.Length : keys.Count;
                Panel p = Pane(page, tool ? "TERRA NOVA PLUS" : grp, x, y, cw[c], 36 + rows * 18 + 6);
                int ry = 36;
                if (tool) for (int i = 0; i < KEY_WHAT.Length; i++) { KeyBadge(KeyRow(p, KEY_WHAT[i], ry), i); ry += 18; }
                else foreach (GameKey g in keys) { GameBadge(KeyRow(p, g.Name, ry), g); ry += 18; }
                y += p.Height + 8;
            }
            if (c == 2)                             // under SQUAD, the shortest column
            {
                int ly = page.Height - 64;
                Lbl(page, "LAYOUT", x + 2, ly + 6, TSmall, TAmber);
                cbLayout = new Sel(); cbLayout.Items = LAYOUT_NAMES; cbLayout.CycleMax = 4;
                cbLayout.Location = new Point(x + 64, ly); cbLayout.Size = new Size(cw[c] - 64, CTL_H); page.Controls.Add(cbLayout);
                cbLayout.MouseEnter += delegate
                {
                    guiHelp.Text = "> letter shortcuts (M map, P pause, ALT+A attack...) follow the letters of your keyboard, move, look and number keys keep their place. Yours: " + LAYOUT_NAMES[DetectLayout()];
                };
                cbLayout.Changed += delegate
                {
                    if (guiBusy || cbLayout.Index > 4) return;
                    int l = cbLayout.Index;
                    ApplyLayout(l); rebindKey = -1; rebindGame = null; SaveSettings(); GuiRefresh();
                    List<string> moved = new List<string>();
                    foreach (GameKey g in GameKeys) if (g.Changed) moved.Add(g.Name.ToLowerInvariant() + " " + ChordName(g.Sc, g.Mod));
                    guiHelp.Text = "> " + LAYOUT_NAMES[l] + ": " + (moved.Count == 0 ? "the game's own keys" : string.Join(", ", moved.ToArray()));
                };
                Button r = Btn(page, "RESET ALL KEYS", x, page.Height - 30, cw[c], 28, false);
                r.Click += delegate
                {
                    ResetKeys(); rebindKey = -1; rebindGame = null; SaveSettings(); GuiRefresh();
                    guiHelp.Text = "> every key back to its default (the tool's: H U J F7 F6)";
                };
            }
        }
    }

    static void ShowPage(bool keys)
    {
        foreach (Control c in setPage) c.Visible = !keys;
        kbPage.Visible = keys;
        tabSet.BackColor = keys ? TPanel : TTab; tabSet.ForeColor = keys ? TDim : Color.White;
        tabKeys.BackColor = keys ? TTab : TPanel; tabKeys.ForeColor = keys ? Color.White : TDim;
        rebindKey = -1; rebindGame = null; GuiRefresh();
        guiHelp.Text = keys ? "> the game's keys: click one, then press the new key, alone or with Alt, Ctrl or Shift. Active in missions only."
                            : "> point at a setting to read what it does";
    }
    static readonly string[] SPEED_NAMES = { "500000", "700000", "1000000", "1200000", "1400000", "GAME'S OWN" };
    static Sel cbDisplay, cbDetail, cbDist, cbMusic, cbTarget, cbPreset, cbSpeed, cbObj, cbFov, cbWide;
    const int ROW0 = 46, ROWH = 32, CTL_H = 26, COL_CTL = 156, COL_W = 140, COL_BADGE = 306;
    static int PaneH(int rows) { return ROW0 + rows * ROWH - (ROWH - CTL_H) + 14; }

    // a value selector in the style of the game's option screens: click cycles the choices, right-click goes
    // back; the choice is centred, small arrows at both ends
    class Sel : Button
    {
        public string[] Items = new string[0];
        public int Index;
        public int CycleMax = -1;                   // choices beyond this index are shown but never cycled to
        public event Action Changed;
        public Sel()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = TField; ForeColor = TText; Font = TBold;
            TextAlign = ContentAlignment.MiddleCenter; Cursor = Cursors.Hand; FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 40, 37);
            SetStyle(ControlStyles.Selectable, true);
        }
        public void Set(int i) { Index = Math.Max(0, Math.Min(i, Items.Length - 1)); Text = Items.Length > 0 ? Items[Index] : ""; Invalidate(); }
        void Step(int d)
        {
            int n = CycleMax >= 0 ? Math.Min(CycleMax + 1, Items.Length) : Items.Length;
            if (n == 0) return;
            int i = Index >= n ? (d > 0 ? 0 : n - 1) : (Index + d + n) % n;
            Set(i); if (Changed != null) Changed();
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); Step(e.Button == MouseButtons.Right ? -1 : 1); }
        protected override void OnClick(EventArgs e) { }
        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Left) { Step(-1); return true; }
            if (k == Keys.Right || k == Keys.Space || k == Keys.Return) { Step(1); return true; }
            return base.ProcessCmdKey(ref m, k);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Bevel(e.Graphics, Size, true);
            using (Brush b = new SolidBrush(Focused ? TAmber : TDim))
            {
                int x = Width - 12, y = Height / 2;
                e.Graphics.FillPolygon(b, new[] { new Point(x, y - 4), new Point(x + 5, y), new Point(x, y + 4) });
                e.Graphics.FillPolygon(b, new[] { new Point(12, y - 4), new Point(7, y), new Point(12, y + 4) });
            }
        }
    }
    static CheckBox ckFree, ckClip, ckStereo, ckHit, ckPhys;
    static bool guiBusy;                            // refreshing the controls: ignore their events
    static Action guiRedrawGame;

    static Font Pick(string[] names, float size, FontStyle style)
    {
        foreach (string n in names)
        {
            Font f = new Font(n, size, style);
            if (f.Name == n) return f;
        }
        return new Font(FontFamily.GenericSansSerif, size, style);
    }

    static bool GuiMenu()
    {
        List<string> installs = FindInstalls();
        if (GameDir == "" || !IsInstall(GameDir)) GameDir = installs.Count > 0 ? installs[0] : "";
        Application.EnableVisualStyles();
        TFont = Pick(new[] { "Bahnschrift", "Segoe UI" }, 10.5f, FontStyle.Regular);
        TSmall = Pick(new[] { "Bahnschrift Light", "Bahnschrift", "Segoe UI" }, 9f, FontStyle.Regular);
        TBold = Pick(new[] { "Bahnschrift SemiBold", "Bahnschrift", "Segoe UI" }, 10.5f, FontStyle.Bold);
        TKey = Pick(new[] { "Bahnschrift SemiBold", "Bahnschrift", "Segoe UI" }, 9f, FontStyle.Bold);
        TTitle = Pick(new[] { "Bahnschrift SemiBold Condensed", "Bahnschrift SemiBold", "Impact", "Segoe UI" }, 30f, FontStyle.Bold);
        TTitle2 = Pick(new[] { "Bahnschrift SemiBold Condensed", "Bahnschrift SemiBold", "Segoe UI" }, 12f, FontStyle.Bold);
        TMono = Pick(new[] { "Consolas", "Courier New" }, 9.5f, FontStyle.Regular);
        bool result = false;
        IntPtr console = GetConsoleWindow();
        ShowWindow(console, 0);                     // the window replaces the console until the game runs

        Form f = new Form();
        f.Text = TITLE + " " + VERSION;
        f.BackColor = TBack; f.ForeColor = TText; f.Font = TFont;
        f.FormBorderStyle = FormBorderStyle.FixedSingle; f.MaximizeBox = false;
        f.ClientSize = new Size(780, 596); f.StartPosition = FormStartPosition.CenterScreen;
        try { f.Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
        f.Paint += delegate(object s, PaintEventArgs e) { PaintHeader(e.Graphics, f.ClientSize.Width); };

        // header: game line under the title band
        Panel gameRow = new Panel(); gameRow.Location = new Point(20, 86); gameRow.Size = new Size(740, 28); gameRow.BackColor = TBack; f.Controls.Add(gameRow);
        Action drawGame = delegate
        {
            gameRow.Controls.Clear();
            if (GameDir == "") { Lbl(gameRow, "GAME NOT FOUND: start the game yourself and ATTACH, or set game_dir in TNPlus.ini", 0, 6, TSmall, TAmber); return; }
            string kind = InstallKind(GameDir), lang = ExeLanguage(GameDir);
            Label badge = Tab(gameRow, kind, 0, 2);
            int x = badge.Right + 10;
            if (lang != "") { Label l = Lbl(gameRow, lang.ToUpperInvariant(), x, 6, TBold, TAmber); x = l.Right + 10; }
            Lbl(gameRow, Shorten(GameDir, 78), x, 7, TSmall, TDim);
            if (installs.Count > 1)
            {
                Button sw = Btn(gameRow, "SWITCH", 650, 0, 90, 26, false);
                sw.Click += delegate { GameDir = installs[(installs.IndexOf(GameDir) + 1) % installs.Count]; hdCheckedDir = null; GuiRefresh(); };
            }
        };

        // panels: picture / sound / controls on the left, fixes / cheats / launch on the right
        tabSet = Tab(f, "SETTINGS", 20, 124); tabKeys = Tab(f, "KEYBOARD", tabSet.Right + 6, 124);
        tabSet.Cursor = Cursors.Hand; tabKeys.Cursor = Cursors.Hand;
        tabSet.Click += delegate { ShowPage(false); }; tabKeys.Click += delegate { ShowPage(true); };
        int y0 = 156, lx = 20, rx = 400, pw = 360;
        Panel pPic = Pane(f, "PICTURE", lx, y0, pw, PaneH(6));
        cbDisplay = Combo(pPic, "Display", 0, DisplayLabels(), "BETA", "");
        cbWide = Combo(pPic, "Widescreen 16:9", 1, WIDE_NAMES, "BETA", "STRETCHED: DOSBox stretches the picture to 16:9, the camera is corrected. TRUE (HD): the 3D drawn in real 16:9 (848 columns, sharper), cockpit and HUD stretched to the full width, mouse captured on click. Needs the game launched from here.");
        cbDetail = Combo(pPic, "Terrain detail", 2, DETAIL_NAMES, "BETA", "Smoother ground: polygons instead of stair steps up to 32 (SHARP) or 48 cells (SHARPER, the game: 12), and more detail far away. SHARPER costs about 25 % fps (the default CPU speed makes up for it).");
        cbDist = Combo(pPic, "View distance", 3, DIST_NAMES, KeyName(ScanDistance), ""); KeyBadge(lastBadge, 2);
        cbFov = Combo(pPic, "Field of view", 5, FOV_NAMES, "BETA", "Horizontal field of view of the 3D view in degrees, at every zoom level. GAME: 84.5, or 100.9 in 16:9. The small cockpit cameras keep theirs.");
        cbObj = Combo(pPic, "Object distance", 4, OBJDIST_NAMES, "BETA", "How far bushes, trees, units and buildings are drawn. GAME: scenery 20 cells. FAR: 30, unit ranges x2. MAX: 40, x3. About 4 % fps at MAX.");
        Panel pSnd = Pane(f, "SOUND", lx, pPic.Bottom + 10, pw, PaneH(1));
        cbMusic = Combo(pSnd, "Music", 0, MUSIC_NAMES, "BETA", "");   // AWE32 always offered: found or not is shown below
        Panel pCtl = Pane(f, "CONTROLS", lx, pSnd.Bottom + 10, pw, PaneH(1));
        ckFree = Switch(pCtl, "Mouse freelook", 0, KeyName(ScanFreelook), ""); KeyBadge(lastBadge, 0);
        Panel pFix = Pane(f, "FIXES", rx, y0, pw, PaneH(3));
        ckStereo = Switch(pFix, "Reversed stereo", 0, KeyName(ScanStereo), ""); KeyBadge(lastBadge, 3);
        ckHit = Switch(pFix, "Projectile hits", 1, "BETA", "Above ~30 fps the game cannot hit moving targets (multipulsar, drones): fixed at any speed, for you and the enemies.");
        ckPhys = Switch(pFix, "Physics speed", 2, "BETA", "The game walks faster the higher the frame rate (+65 % at 76 fps), jumps and falls too. Fixed: same speed at any frame rate.");
        Panel pCheat = Pane(f, "CHEATS", rx, pFix.Bottom + 10, pw, PaneH(1));
        ckClip = Switch(pCheat, "Noclip", 0, KeyName(ScanNoclip), ""); KeyBadge(lastBadge, 1);
        Panel pLaunch = Pane(f, "LAUNCH", rx, pCheat.Bottom + 10, pw, PaneH(3) + 24);
        cbTarget = Combo(pLaunch, "Run", 0, new[] { "FULL GAME", "DEMO 1", "DEMO 2" }, null, "The two 1996 demos shipped with GOG and Steam have missions the full game hasn't. No HD there yet.");
        cbPreset = Combo(pLaunch, "Preset", 1, new[] { "ORIGINAL", "CLASSIC+", "BEST", "CUSTOM" }, null, "Presets set picture and controls at once. Fixes, cheats and music are yours to choose.");
        cbSpeed = Combo(pLaunch, "CPU speed", 2, SPEED_NAMES, "BETA",
            "DOSBox CPU cycles: the game runs as fast as they allow. True 16:9 HD: 700000 ~40 fps, 1000000 ~57, 1200000 ~70 (one full CPU core). DOSBox lowers them by itself when your PC cannot keep up.");
        guiPreset = Lbl(pLaunch, "", 14, PaneH(3) - 4, TSmall, TDim); guiPreset.AutoSize = false; guiPreset.Size = new Size(pw - 28, 20);

        // help and warnings: a readout strip like the cockpit's message line
        int yb = Math.Max(pCtl.Bottom, pLaunch.Bottom) + 14;
        setPage.Clear(); setPage.AddRange(new Control[] { pPic, pSnd, pCtl, pFix, pCheat, pLaunch });
        kbPage = new Panel(); kbPage.Location = new Point(20, y0); kbPage.Size = new Size(740, yb - 10 - y0); kbPage.BackColor = TBack;
        kbPage.Visible = false; f.Controls.Add(kbPage);
        Panel strip = new Panel(); strip.Location = new Point(20, yb); strip.Size = new Size(740, 56); strip.BackColor = TField; f.Controls.Add(strip);
        strip.Paint += delegate(object s, PaintEventArgs e) { Bevel(e.Graphics, strip.Size, true); };
        guiHelp = Lbl(strip, "> point at a setting to read what it does", 12, 8, TMono, TLed); guiHelp.AutoSize = false; guiHelp.Size = new Size(716, 30);
        guiWarn = Lbl(strip, "", 12, 36, TMono, TAmber); guiWarn.AutoSize = false; guiWarn.Size = new Size(716, 16);
        guiKeys = Lbl(f, "", 20, yb + 64, TSmall, TDim);

        // buttons, orange like the loadout screen's
        Button bLaunch = Btn(f, "LAUNCH", 20, yb + 88, 170, 32, true);
        Button bAttach = Btn(f, "ATTACH TO A RUNNING GAME", 202, yb + 88, 250, 32, false);
        Button bQuit = Btn(f, "QUIT", 670, yb + 88, 90, 32, false);
        f.ClientSize = new Size(780, yb + 88 + 32 + 20);
        bLaunch.Click += delegate
        {
            ApplyDisplay(); SaveSettings();
            ShowWindow(console, 5);
            if (Launch()) { result = true; f.Close(); }
            else { ShowWindow(console, 0); guiWarn.Text = "COULD NOT LAUNCH: " + (IsInstall(GameDir) ? "see README (demos)" : "game folder not found"); }
        };
        bAttach.Click += delegate { ApplyDisplay(); SaveSettings(); ShowWindow(console, 5); result = true; f.Close(); };
        bQuit.Click += delegate { f.Close(); };
        f.AcceptButton = bLaunch; f.ActiveControl = bLaunch;
        f.FormClosed += delegate { SaveSettings(); };

        // events
        cbDisplay.Changed += delegate { if (!guiBusy) { Display = cbDisplay.Index; GuiRefresh(); } };
        cbWide.Changed += delegate { if (!guiBusy) { SetWideMode(cbWide.Index); GuiRefresh(); } };
        cbDetail.Changed += delegate { if (!guiBusy) { OptDetail = cbDetail.Index; GuiRefresh(); } };
        cbDist.Changed += delegate { if (!guiBusy) { OptDistance = cbDist.Index; GuiRefresh(); } };
        cbObj.Changed += delegate { if (!guiBusy) { OptObjDist = cbObj.Index; GuiRefresh(); } };
        cbFov.Changed += delegate { if (!guiBusy) { OptFov = cbFov.Index; GuiRefresh(); } };
        ckFree.CheckedChanged += delegate { if (!guiBusy) { OptFreelook = ckFree.Checked; GuiRefresh(); } };
        ckClip.CheckedChanged += delegate { if (!guiBusy) { OptNoclip = ckClip.Checked; GuiRefresh(); } };
        ckStereo.CheckedChanged += delegate { if (!guiBusy) { OptStereoFix = ckStereo.Checked; GuiRefresh(); } };
        cbMusic.Changed += delegate { if (!guiBusy) { OptMusic = cbMusic.Index; GuiRefresh(); } };
        ckHit.CheckedChanged += delegate { if (!guiBusy) { OptHitFix = ckHit.Checked; GuiRefresh(); } };
        ckPhys.CheckedChanged += delegate { if (!guiBusy) { OptPhysFix = ckPhys.Checked; GuiRefresh(); } };
        cbSpeed.Changed += delegate { if (!guiBusy) { CpuCycles = CYCLE_CHOICES[cbSpeed.Index]; GuiRefresh(); } };
        cbTarget.Changed += delegate { if (!guiBusy) { LaunchTarget = cbTarget.Index; GuiRefresh(); } };
        cbPreset.CycleMax = 2;
        cbPreset.Changed += delegate { if (!guiBusy && cbPreset.Index < 3) { ApplyPreset(cbPreset.Index); GuiRefresh(); } };
        guiRedrawGame = drawGame;
        BuildKeysPage(kbPage);
        ShowPage(false);
        GuiRefresh();
        KeyCatcher catcher = new KeyCatcher();
        Application.AddMessageFilter(catcher);
        Application.Run(f);
        Application.RemoveMessageFilter(catcher);
        rebindKey = -1; rebindGame = null; keyBadges.Clear(); gameBadges.Clear();
        guiRedrawGame = null;
        return result;
    }

    // the controls follow the settings (and the preset follows the controls)
    static void GuiRefresh()
    {
        guiBusy = true;
        if (guiRedrawGame != null) guiRedrawGame();
        string why; bool hdOk = HdAvailable(out why);
        cbDisplay.Items = DisplayLabels(); cbDisplay.Tag = HdHelp() + ". " + KeyName(ScanSmoothing) + " toggles the smoothing in game.";
        cbDist.Tag = "View distance at mission start. " + KeyName(ScanDistance) + " cycles it in game (click the purple key to change it).";
        ckFree.Tag = "Look around with the mouse, " + KeyName(ScanFreelook) + " in game (click the purple key to change it). Sensitivity and inverted look in TNPlus.ini.";
        ckStereo.Tag = "Only if your sound is mirrored: under DOSBox the game's SB16 driver swaps left and right. " + KeyName(ScanStereo) + " swaps the sound effects in game, to compare.";
        ckClip.Tag = "Fly through everything, " + KeyName(ScanNoclip) + " in game (click the purple key to change it). Speed in TNPlus.ini.";
        guiKeys.Text = "IN GAME   " + KeyName(ScanFreelook) + " freelook   " + KeyName(ScanNoclip) + " noclip   " + KeyName(ScanDistance) + " view distance   " +
            KeyName(ScanStereo) + " stereo swap   " + KeyName(ScanSmoothing) + " HD smoothing";
        foreach (KeyValuePair<Label, int> kb in keyBadges)
        {
            bool wait = kb.Value == rebindKey;
            kb.Key.Text = wait ? "?" : KeyName(GetKey(kb.Value));
            kb.Key.BackColor = wait ? TAmber : Color.FromArgb(118, 48, 160); kb.Key.ForeColor = wait ? Color.FromArgb(40, 30, 0) : Color.White;
            kb.Key.Width = Math.Max(TextRenderer.MeasureText(kb.Key.Text, kb.Key.Font).Width + 12, 26);
            if (kb.Key.Parent != null && kb.Key.Parent.Parent == kbPage) kb.Key.Left = kb.Key.Parent.Width - 10 - kb.Key.Width;
        }
        if (cbLayout != null) cbLayout.Set(CurrentLayout());
        foreach (KeyValuePair<Label, GameKey> gb in gameBadges)
        {
            GameKey g = gb.Value; Label b = gb.Key; bool wait = g == rebindGame;
            b.Text = wait ? "?" : ChordName(g.Sc, g.Mod);
            b.BackColor = wait ? TAmber : g.Fixed ? Color.FromArgb(40, 64, 58) : Color.FromArgb(118, 48, 160);
            b.ForeColor = wait ? Color.FromArgb(40, 30, 0) : g.Fixed ? TDim : g.Changed ? TAmber : Color.White;
            b.Width = Math.Max(TextRenderer.MeasureText(b.Text, b.Font).Width + 10, 26);
            b.Left = b.Parent.Width - 10 - b.Width;
        }
        cbDisplay.Set(Display); cbWide.Set(WideMode()); cbDetail.Set(OptDetail); cbDist.Set(OptDistance); cbObj.Set(OptObjDist); cbFov.Set(OptFov);
        ckFree.Checked = OptFreelook; ckClip.Checked = OptNoclip; ckStereo.Checked = OptStereoFix;
        bool aweRom = AweRom() != null;
        cbMusic.Set(OptMusic);
        cbMusic.Tag = MUSIC_INFO[OptMusic] + (OptMusic == 3
            ? (aweRom ? ". awe32.raw found. Experimental: barely tested, some instruments may sound off."
                      : ". awe32.raw (AWE32 ROM, 1 MB, not included) not found next to TNPlus.exe: ROLAND is played instead, see README.")
            : aweRom ? "." : ". Optional AWE32 music: put awe32.raw (AWE32 ROM, 1 MB, not included) next to TNPlus.exe, see README.");
        ckHit.Checked = OptHitFix; ckPhys.Checked = OptPhysFix; cbTarget.Set(LaunchTarget);
        int sp = Array.IndexOf(CYCLE_CHOICES, CpuCycles);
        if (sp < 0) { var it = new List<string>(SPEED_NAMES); it.Add(CpuCycles.ToString()); cbSpeed.Items = it.ToArray(); sp = SPEED_NAMES.Length; }
        cbSpeed.CycleMax = SPEED_NAMES.Length - 1; cbSpeed.Set(sp);
        foreach (CheckBox c in new[] { ckFree, ckClip, ckStereo, ckHit, ckPhys }) Led(c);
        int p = CurrentPreset(); cbPreset.Set(p < 0 ? 3 : p);
        guiPreset.Text = p < 0 ? "Your own settings. A preset starts again from a known mix." : PRESET_INFO[p];
        string warn = "";
        if (Display == 2 && !hdOk) warn = HdName() + " " + why + ": 320x400 is used.";
        if (LaunchTarget > 0 && !HasDemo(GameDir, LaunchTarget)) warn += (warn == "" ? "" : "  ") + "Demo " + LaunchTarget + " not found in the game folder, see README.";
        if (OptMusic == 3) warn += (warn == "" ? "" : "  ") + (aweRom ? "AWE32 ROM found: AWE32 music on." : "AWE32 ROM not found (awe32.raw next to TNPlus.exe): ROLAND is used.");
        guiWarn.Text = warn.ToUpperInvariant();
        guiBusy = false;
    }

    // ---- drawing
    static void PaintHeader(Graphics g, int width)
    {
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        // title band: a dark plate with a thin amber rule, scanlines like the cockpit screens
        using (Brush b = new SolidBrush(TPanel)) g.FillRectangle(b, 0, 0, width, 76);
        using (Pen p = new Pen(Color.FromArgb(30, 0, 0, 0))) for (int y = 1; y < 76; y += 3) g.DrawLine(p, 0, y, width, y);
        using (Pen p = new Pen(TPanelHi)) g.DrawLine(p, 0, 76, width, 76);
        using (Pen p = new Pen(TAmber, 2)) g.DrawLine(p, 20, 70, 200, 70);
        using (Brush b = new SolidBrush(TText)) g.DrawString("TERRA NOVA", TTitle, b, 16, 10);
        SizeF w = g.MeasureString("TERRA NOVA", TTitle);
        using (Brush b = new SolidBrush(TAmber)) g.DrawString("PLUS", TTitle, b, 16 + w.Width - 6, 10);
        using (Brush b = new SolidBrush(TDim)) g.DrawString("STRIKE FORCE CENTAURI  ·  QUALITY-OF-LIFE PACK  ·  " + VERSION.ToUpperInvariant(), TTitle2, b, 400, 30);
        using (Brush b = new SolidBrush(TDim)) g.DrawString("made by skw-1337  ·  github.com/skw-1337/terra-nova-plus", TSmall, b, 400, 50);
    }

    static void Bevel(Graphics g, Size s, bool sunken)
    {
        Color hi = sunken ? TPanelLo : TPanelHi, lo = sunken ? TPanelHi : TPanelLo;
        using (Pen p = new Pen(hi)) { g.DrawLine(p, 0, 0, s.Width - 1, 0); g.DrawLine(p, 0, 0, 0, s.Height - 1); }
        using (Pen p = new Pen(lo)) { g.DrawLine(p, 0, s.Height - 1, s.Width - 1, s.Height - 1); g.DrawLine(p, s.Width - 1, 0, s.Width - 1, s.Height - 1); }
    }

    // ---- widgets
    static Label Lbl(Control parent, string text, int x, int y, Font font, Color color)
    {
        Label l = new Label(); l.Text = text; l.Location = new Point(x, y); l.Font = font; l.ForeColor = color; l.AutoSize = true; l.BackColor = Color.Transparent;
        parent.Controls.Add(l); return l;
    }

    // blue tab with white text, like COMM / STAT on the cockpit: fixed box, text centred both ways
    static Label Tab(Control parent, string text, int x, int y)
    {
        Label l = new Label(); l.Text = text; l.AutoSize = false; l.TextAlign = ContentAlignment.MiddleCenter;
        l.Font = TBold; l.ForeColor = Color.White; l.BackColor = TTab;
        l.Size = new Size(TextRenderer.MeasureText(text, TBold).Width + 18, 22); l.Location = new Point(x, y);
        parent.Controls.Add(l); return l;
    }

    static Panel Pane(Control f, string name, int x, int y, int w, int h)
    {
        Panel p = new Panel(); p.Location = new Point(x, y); p.Size = new Size(w, h); p.BackColor = TPanel; f.Controls.Add(p);
        Tab(p, name, 10, 6);
        p.Paint += delegate(object s, PaintEventArgs e)
        {
            Bevel(e.Graphics, p.Size, false);
            using (Pen pen = new Pen(TPanelLo)) e.Graphics.DrawLine(pen, 8, 30, w - 9, 30);
            using (Pen pen = new Pen(Color.FromArgb(60, TPanelHi))) e.Graphics.DrawLine(pen, 8, 31, w - 9, 31);
        };
        return p;
    }

    // badge: BETA in amber, or an in-game key in purple; fixed box, text centred both ways
    static void Badge(Control parent, string tag, int x, int y)
    {
        if (tag == null) return;
        bool beta = tag == "BETA";
        Label b = new Label(); b.Text = tag; b.AutoSize = false; b.TextAlign = ContentAlignment.MiddleCenter;
        b.Font = beta ? TSmall : TBold; b.ForeColor = beta ? Color.FromArgb(40, 30, 0) : Color.White;
        b.BackColor = beta ? TAmber : Color.FromArgb(118, 48, 160);
        int w = TextRenderer.MeasureText(tag, b.Font).Width + 12;
        b.Size = new Size(Math.Max(w, 26), 20); b.Location = new Point(x, y + (CTL_H - 20) / 2);
        parent.Controls.Add(b);
        lastBadge = b;
    }

    static Sel Combo(Panel p, string label, int row, string[] items, string tag, string help)
    {
        int yy = ROW0 + row * ROWH;
        Lbl(p, label.ToUpperInvariant(), 14, yy + (CTL_H - 15) / 2, TSmall, TAmber);
        Sel c = new Sel(); c.Items = items; c.Location = new Point(COL_CTL, yy); c.Size = new Size(COL_W, CTL_H); c.Tag = help; c.Set(0);
        p.Controls.Add(c);
        Badge(p, tag, COL_BADGE, yy);
        c.Enter += delegate { guiHelp.Text = "> " + (string)c.Tag; };
        c.MouseEnter += delegate { guiHelp.Text = "> " + (string)c.Tag; };
        return c;
    }

    // ON / OFF switch drawn as a LED button
    static CheckBox Switch(Panel p, string label, int row, string tag, string help)
    {
        int yy = ROW0 + row * ROWH;
        Lbl(p, label.ToUpperInvariant(), 14, yy + (CTL_H - 15) / 2, TSmall, TAmber);
        CheckBox c = new CheckBox(); c.Appearance = Appearance.Button; c.FlatStyle = FlatStyle.Flat; c.Location = new Point(COL_CTL, yy); c.Size = new Size(COL_W, CTL_H);
        c.TextAlign = ContentAlignment.MiddleCenter; c.Font = TBold; c.Tag = help; c.Cursor = Cursors.Hand;
        c.FlatAppearance.BorderColor = TPanelLo; c.FlatAppearance.BorderSize = 1; c.FlatAppearance.CheckedBackColor = TLed; c.FlatAppearance.MouseOverBackColor = Color.Empty;
        p.Controls.Add(c);
        Badge(p, tag, COL_BADGE, yy);
        c.CheckedChanged += delegate { Led(c); };
        c.Enter += delegate { guiHelp.Text = "> " + (string)c.Tag; };
        c.MouseEnter += delegate { guiHelp.Text = "> " + (string)c.Tag; };
        Led(c);
        return c;
    }

    static void Led(CheckBox c)
    {
        c.Text = c.Checked ? "ON" : "OFF";
        c.BackColor = c.Checked ? TLed : TLedOff; c.ForeColor = c.Checked ? Color.FromArgb(6, 40, 14) : TDim;
        c.FlatAppearance.MouseOverBackColor = c.Checked ? Color.FromArgb(90, 230, 120) : Color.FromArgb(58, 82, 76);
    }

    class TBtn : Button
    {
        public bool Accent;
        public TBtn() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        bool over;
        protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color bg = Accent ? (over ? Color.FromArgb(236, 146, 44) : TOrange) : (over ? Color.FromArgb(34, 62, 57) : TPanel);
            using (Brush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, ClientRectangle);
            Bevel(e.Graphics, Size, false);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Accent ? Color.FromArgb(30, 14, 0) : TText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) using (Pen p = new Pen(TAmber)) e.Graphics.DrawRectangle(p, 2, 2, Width - 5, Height - 5);
        }
    }

    static Button Btn(Control parent, string text, int x, int y, int w, int h, bool accent)
    {
        TBtn b = new TBtn(); b.Text = text; b.Location = new Point(x, y); b.Size = new Size(w, h); b.Font = TBold; b.Accent = accent;
        parent.Controls.Add(b); return b;
    }
}
