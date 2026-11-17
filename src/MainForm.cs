using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace QualControl
{
    // QualControl: QualControl's custom qualifying (single car runs, knockout rounds, Indy oval, Indy road / street), its
    // timing boxes and the player banner, on their own - no cautions. Works with stock NR2003 or any caution mod.
    public class MainForm : Form
    {
        public const string Version = "0.5.0";   // 0.5.0: race field cut + race roster (.lst) in the merge window, player marked in saved results. 0.4.0: merge saved results, .car file + series in saved results. 0.3.0: save / reapply results, tab gap fix. 0.2.0: running order from practice lap times. 0.1.0: first build - RaceControl 3.4.0's qualifying, timing boxes and banner on their own (no state file for RaceVision yet)

        readonly Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>();
        readonly Dictionary<string, string> settings = new Dictionary<string, string>();
        readonly Label status;
        readonly ListBox logBox;
        readonly Timer tick, slowTimer;
        readonly string logPath, iniPath;
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 30000, InitialDelay = 350, ReshowDelay = 120, OwnerDraw = true };

        ProcessMemory mem;
        string attachError, track;
        bool loading;
        bool Paused { get { try { return mem != null && mem.U8(Addr.SimState) == 3; } catch (GameMemoryException) { return false; } } }
        QualReadyForm qualReady;                    // your qualifying turn: game paused until you are ready
        bool qualReadySeenPaused;
        DateTime qualReadyAt;
        readonly ConeArt art = new ConeArt();
        Label artLabel;
        QualBoxForm qualBox, qualPlacing;                  // the qualifying timing box and its placing copy
        PlayerBannerForm banner;                           // calls to the player over the game (qualifying: your turn, come in...)
        static bool QCar(int c) { return c == Addr.PlayerIdx || c >= 2; }   // a car that can be on a qualifying run (not the pace car)
        QualBroadcastForm qualBroad;                       // the broadcast style qualifying overlay
        DateTime broadAt = DateTime.MinValue; string broadKey;
        ComboBox qualStyleBox;
        readonly Dictionary<int, float> rpmTop = new Dictionary<int, float>();   // the highest RPM seen per car (the bar's full scale)
        // AI telemetry (Telemetry.cs): every qualifying car sampled ~30 times a game second while qualifying is live; the
        // broadcast box's brake reads it
        readonly TeleRecorder tele = new TeleRecorder();
        ComboBox qualLapUnitBox;
        int orderCar = -1, orderLapStart = -2; DateTime orderUntil = DateTime.MinValue;
        bool roundSync;
        readonly Label[] roundLabels = new Label[4], roundMidLabels = new Label[4], roundEndLabels = new Label[4];
        readonly NumericUpDown[] roundMins = new NumericUpDown[4], roundCars = new NumericUpDown[4];
        ListBox orderList;
        List<string> orderOrder = new List<string>();                          // car numbers, in running order
        readonly Dictionary<string, string> orderNames = new Dictionary<string, string>();
        BoardLayout qualBoxLayout = new BoardLayout();
        BoardLayout bannerLayout = new BoardLayout();          // the calls to the player (X/Y unset = centred at the top)
        PlayerBannerForm bannerPlacing;
        readonly TabControl tabs;
        Control page;                                           // the tab the next groups go on
        readonly Qualifying qual = new Qualifying();
        readonly ComboBox qualFormatBox, qualIndyBox, qualIndySplitBox;
        readonly NumericUpDown qualIndyLapsBox, qualIndyGroupMin, qualIndyGroupAdv, qualIndyF12Min, qualIndyF12Adv, qualIndyF6Min, qualIndyBreak;
        // the Qualifying tab: one box per type, only the chosen type's box is open (LayoutQualTab restacks them)
        GroupBox qualTopGroup, qualSingleGroup, qualKoGroup, qualOvalGroup, qualRoadGroup, qualDnqGroup, qualPaceGroup, qualListsGroup, qualOrderGroup;
        ComboBox qualOrderFromBox, qualFastestBox; Button qualFromPracticeBtn, qualKeepListBtn;
        GroupBox qualSavedGroup; Button qualSaveBtn, qualReapplyBtn; Label qualSavedLabel;
        Control qualPage; Label qualBreakLabel, qualIndyLapsLabel, qualLapsLabel, qualOrderLabel, qualResultsLabel;
        readonly RadioButton[] qualRadios = new RadioButton[5];     // Off, Single car runs, Knockout rounds, Indy oval, Indy road
        Panel singleRunsPanel;                                      // release / DNQ / max out / laps: in the single car box or the Indy oval box
        readonly List<Control> qualKoBody = new List<Control>();
        List<Control> qualStack;
        bool qualSync;
        readonly List<Control> qualIndyRoadCtls = new List<Control>();
        readonly NumericUpDown qualLapsBox, qualBreakBox, qualDnqBox, qualReleaseVal, qualMaxOutBox;
        NumericUpDown qualBoostBox;
        // Indy oval re-qualifying lanes, the field size (DNQs), the player's lane buttons
        CheckBox qualReQualBox, qualWaveOffBox; NumericUpDown qualReQualMin, qualReQualRuns, qualStartersBox; ComboBox qualStartersUnit; CheckBox qualScratchBox;
        readonly List<Control> qualOvalCtls = new List<Control>();
        Button laneBtn1, laneBtn2, laneOutBtn; Label qualLaneLabel;
        readonly ComboBox qualReleaseBox;
        readonly TextBox qualRoundsBox, qualOrderBox;
        readonly Label qualStatus, qualNeeds;
        readonly Button qualStart, qualStop;
        readonly ListBox qualBoard;
        DateTime lastQualTick;
        ComboBox themeBox;
        Label brandQual, brandControl;                          // the name in the header, in the logo's colours

        public MainForm()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            logPath = Path.Combine(dir, "qual_control.log");
            iniPath = Path.Combine(dir, "QualControl.ini");

            Text = "QualControl";
            // hover text drawn in our own colours (dark mode too) and sized to its text
            var tipFont = new Font("Segoe UI", 9f);
            tips.Popup += (s, e) =>
            {
                string t = e.AssociatedControl != null ? tips.GetToolTip(e.AssociatedControl) : "";
                Size sz = TextRenderer.MeasureText(t, tipFont, new Size(900, 0), TextFormatFlags.NoPadding);
                e.ToolTipSize = new Size(sz.Width + 18, sz.Height + 14);
            };
            tips.Draw += (s, e) =>
            {
                bool d = Theme.Dark;
                using (var b = new SolidBrush(d ? Theme.Input : SystemColors.Info)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var pen = new Pen(d ? Theme.Border : SystemColors.ControlDark)) e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, tipFont, new Rectangle(9, 7, e.Bounds.Width - 12, e.Bounds.Height - 10), d ? Theme.Text : SystemColors.InfoText, TextFormatFlags.NoPadding);
            };
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(620, 700);
            MinimumSize = new Size(560, 480);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            // QualControl's twist: the logo (transparent, with its own drop shadow) and the name in the logo's amber and red
            // across the top, over a red rule; the band follows the light / dark look
            var header = new Panel { Left = 0, Top = 0, Width = ClientSize.Width, Height = 64, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            header.Paint += (s, e) => { using (var p = new Pen(Theme.Red, 3)) e.Graphics.DrawLine(p, 0, header.Height - 2, header.Width, header.Height - 2); };
            Image logo = null;
            try
            {
                using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("QualControl.logo.png"))
                    if (s != null) logo = new Bitmap(Image.FromStream(s));
            }
            catch (Exception) { }
            if (logo != null)
                header.Controls.Add(new PictureBox { Left = 8, Top = 3, Width = 100, Height = 57, SizeMode = PictureBoxSizeMode.Zoom, Image = logo, BackColor = Color.Transparent });
            int nx = logo != null ? 114 : 12;
            brandQual = new Label { Left = nx, Top = 9, AutoSize = true, Text = "QUAL", Font = new Font("Segoe UI", 14f, FontStyle.Bold), BackColor = Color.Transparent, Tag = Theme.Brand };
            brandControl = new Label { Left = nx + 58, Top = 10, AutoSize = true, Text = "Control", Font = new Font("Georgia", 14f, FontStyle.Italic), BackColor = Color.Transparent, Tag = Theme.Brand };
            header.Controls.Add(brandQual); header.Controls.Add(brandControl);
            header.Controls.Add(new Label { Left = nx + 2, Top = 38, AutoSize = true, Text = "Custom qualifying for NASCAR Racing 2003 Season", ForeColor = SystemColors.GrayText });
            Controls.Add(header);

            int y = 72;
            page = this;
            GroupBox g;
            status = new Label { Left = 12, Top = y, Width = 596, Height = 20, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(status);
            tabs = new ThemedTabControl { Left = 12, Top = y + 22, Width = 596, Height = ClientSize.Height - y - 30,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(tabs);

            y = Tab("Qualifying");
            // The type is chosen in the header of each box below (or Off here); only the chosen type's box is open.
            // qualFormatBox / qualIndyBox are kept as the stored choice (they hold what is saved and what the engine reads)
            // but are not shown: the radio buttons set them.
            qualFormatBox = new ThemedComboBox(); qualFormatBox.Items.AddRange(new object[] { "Off", "Single car runs", "Knockout rounds" });
            qualIndyBox = new ThemedComboBox(); qualIndyBox.Items.AddRange(new object[] { "Off", "Oval: 4-lap average (single car)", "Road / street: groups, Fast 12, Fast Six" });
            g = Group("Qualifying", ref y, 126);
            qualTopGroup = g; qualPage = page;
            QualRadio(g, "Off: no custom qualifying", 10, 20, 0,
                "Qualifying is run in the PRACTICE session just before qualifying (make that session long enough), with the AI cars\n" +
                "held in their pit boxes. In the qualifying session after it, the AI's laps are replaced with the times made here, so the\n" +
                "leaderboard and the starting grid follow them. Your own laps are never touched.\n" +
                "Pick a type in one of the boxes below; Off leaves the game's own qualifying alone.");
            qualStatus = new Label { Left = 10, Top = 46, Width = 400, Height = 36 };
            g.Controls.Add(qualStatus);
            qualNeeds = new Label { Left = 10, Top = 86, Width = 400, Height = 34, ForeColor = SystemColors.GrayText };
            g.Controls.Add(qualNeeds);
            qualStart = new Button { Text = "Start qualifying", Left = 420, Top = 18, Width = 146, Height = 28 };
            tips.SetToolTip(qualStart, "Starts the run once every car is sitting in its pit box (the count in the status stops growing).\n" +
                "Stay out of the car and watch on the TV cameras - or take your own turn when the banner calls you.");
            qualStart.Click += (s, e) => { qual.Start(); QualUi(); };
            g.Controls.Add(qualStart);
            qualStop = new Button { Text = "Stop", Left = 420, Top = 52, Width = 146, Height = 28 };
            tips.SetToolTip(qualStop, "Stops qualifying for this session: the cars are let go and no grid is written.");
            qualStop.Click += (s, e) => { qual.Stop(); QualUi(); };
            g.Controls.Add(qualStop);

            // ---- single car runs ----
            g = Group("Single car runs", ref y, 48);
            qualSingleGroup = g;
            QualRadio(g, "Use single car runs: one car at a time, with its own out lap and timed laps", 10, 20, 1,
                "One car at a time rolls off its pit box, runs an out lap and its timed laps, and comes back in. The out lap never\n" +
                "counts: the first time over the line after leaving pit road starts the timed lap. Cars are ranked by their best lap.");
            // these run settings are shared by the single car runs and the Indy oval: the panel moves into whichever is open
            singleRunsPanel = new Panel { Left = 4, Top = 46, Width = 560, Height = 86, BackColor = Color.Transparent };
            Func<string, int, int, int, Label> sl = (txt, x, top, w) => { var l = new Label { Left = x, Top = top + 3, Width = w, Text = txt }; singleRunsPanel.Controls.Add(l); return l; };
            sl("Release the next car:", 6, 4, 118);
            qualReleaseBox = new ThemedComboBox { Left = 126, Top = 4, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 270 };
            qualReleaseBox.Items.AddRange(new object[] { "when the car before is on pit road", "N seconds after the car before left", "when the car before is N% round its last lap", "green N s after the car before finishes" });
            tips.SetToolTip(qualReleaseBox, "When the next car leaves its pit box.\n" +
                "On pit road / N seconds after / N% round: the car before has reached pit road, left N s ago, or is N% of the way round its last lap.\n" +
                "Green N s after: every box is a different distance from pit exit, so each car's release is timed from its own predicted\n" +
                "out lap (learned from the cars already run) to take the green about N s after the car before ends its last timed lap.\n" +
                "The first pair can be late: there is no out lap to learn from until the first car has taken the green.\n" +
                "The log shows each release and the gap each car actually got.");
            singleRunsPanel.Controls.Add(qualReleaseBox);
            qualReleaseVal = new NumericUpDown { Left = 382, Top = 4, Width = 52, Minimum = 0, Maximum = 600, Value = 30 };
            tips.SetToolTip(qualReleaseVal, "N for the release rule: seconds after the car before left its box, % of the way round its final timed lap,\n" +
                "or (green gap) seconds between the car before finishing and this car taking the green.");
            singleRunsPanel.Controls.Add(qualReleaseVal);
            sl("DNQ if still in its box after (s):", 6, 32, 188);
            qualDnqBox = new NumericUpDown { Left = 196, Top = 32, Width = 55, Minimum = 15, Maximum = 600, Value = 90 };
            tips.SetToolTip(qualDnqBox, "A car that is told to go but doesn't pull out of its pit box in this many seconds is a DNQ: it stays in its box,\n" +
                "starts at the back, and the next car goes. Time the game is paused never counts.");
            singleRunsPanel.Controls.Add(qualDnqBox);
            sl("Max cars out at once:", 290, 32, 128);
            qualMaxOutBox = new NumericUpDown { Left = 420, Top = 32, Width = 50, Minimum = 1, Maximum = 20, Value = 3 };
            tips.SetToolTip(qualMaxOutBox, "Never more than this many cars out on their runs at once (a car on its in-lap counts until it reaches pit road).\n" +
                "Raise it for long road courses with a short release interval.");
            singleRunsPanel.Controls.Add(qualMaxOutBox);
            qualLapsLabel = sl("Timed laps per car:", 6, 60, 188);
            qualLapsBox = new NumericUpDown { Left = 196, Top = 60, Width = 55, Minimum = 1, Maximum = 5, Value = 1 };
            tips.SetToolTip(qualLapsBox, "How many timed laps each car runs (its best counts). The out lap never counts.");
            singleRunsPanel.Controls.Add(qualLapsBox);
            g.Controls.Add(singleRunsPanel);
            qualOrderBox = new TextBox { Left = 250, Top = 79, Width = 316, Visible = false };   // the store; the running order list edits it
            tips.SetToolTip(qualOrderBox, "Car numbers in the order they go out. Cars not listed go after them in a random draw.");
            g.Controls.Add(qualOrderBox);

            // ---- knockout rounds ----
            g = Group("Knockout rounds", ref y, 48);
            qualKoGroup = g;
            QualRadio(g, "Use knockout rounds: every car out at once, the fastest go through", 10, 20, 2,
                "Every car in a round is out until the round's time runs out; the fastest go through to the next round. When time\n" +
                "runs out cars finish the lap they are on; out laps, in-laps and pit road never count. Grid: the last round by its times,\n" +
                "then each earlier round's other cars by that round's times.");
            qualRoundsBox = new TextBox { Left = 250, Top = 107, Width = 150, Visible = false };   // the store: "25:24, 10:12, 5:12"
            tips.SetToolTip(qualRoundsBox, "25:24, 10:12, 5:12 = 25 minutes, the top 24 go through; 10 minutes, the top 12 go through; 5 minutes for pole.");
            g.Controls.Add(qualRoundsBox);
            for (int i = 0; i < 4; i++)
            {
                int lx = 10 + (i / 2) * 285, top = 48 + (i % 2) * 28;
                roundLabels[i] = new Label { Left = lx, Top = top + 3, Width = 55, Text = "Round " + (i + 1) };
                roundMins[i] = new NumericUpDown { Left = lx + 58, Top = top, Width = 50, Minimum = 0, Maximum = 180, DecimalPlaces = 0 };
                roundMidLabels[i] = new Label { Left = lx + 112, Top = top + 3, Width = 56, Text = "min, top" };
                roundCars[i] = new NumericUpDown { Left = lx + 170, Top = top, Width = 45, Minimum = 1, Maximum = 60 };
                roundEndLabels[i] = new Label { Left = lx + 220, Top = top + 3, Width = 50, Text = "go on" };
                tips.SetToolTip(roundMins[i], "Round " + (i + 1) + " length in minutes. 0 = no such round.");
                tips.SetToolTip(roundCars[i], "How many cars go through to the next round (not used in the last round).");
                roundMins[i].ValueChanged += (s2, e2) => { if (!roundSync) RoundsTextFromRows(); };
                roundCars[i].ValueChanged += (s2, e2) => { if (!roundSync) RoundsTextFromRows(); };
                foreach (Control c in new Control[] { roundLabels[i], roundMins[i], roundMidLabels[i], roundCars[i], roundEndLabels[i] }) { g.Controls.Add(c); qualKoBody.Add(c); }
            }
            qualBreakLabel = new Label { Left = 10, Top = 109, Width = 170, Text = "Break between rounds (s):" };
            g.Controls.Add(qualBreakLabel); qualKoBody.Add(qualBreakLabel);
            qualBreakBox = new NumericUpDown { Left = 184, Top = 106, Width = 60, Minimum = 0, Maximum = 600, Value = 60 };
            tips.SetToolTip(qualBreakBox, "Seconds between knockout rounds, with every car back in its box.");
            g.Controls.Add(qualBreakBox); qualKoBody.Add(qualBreakBox);

            // ---- Indy oval ----
            g = Group("Indy oval", ref y, 48);
            qualOvalGroup = g;
            QualRadio(g, "Use Indy oval: each car's run ranked by its lap average", 10, 20, 3,
                "Each car makes one run of consecutive timed laps from a flying start (the out lap never counts), ranked by the AVERAGE\n" +
                "of those laps - every lap counts, a slow one included; a run that isn't finished gets no time. In the qualifying session\n" +
                "every lap is written as the average, so the grid follows it. Cars go one at a time: the release, DNQ and max-cars-out\n" +
                "settings and the running order list are the same as for single car runs.");
            qualIndyLapsLabel = new Label { Left = 400, Top = 23, Width = 92, Text = "Laps per run:" };
            g.Controls.Add(qualIndyLapsLabel); qualOvalCtls.Add(qualIndyLapsLabel);
            qualIndyLapsBox = new NumericUpDown { Left = 494, Top = 19, Width = 60, Minimum = 1, Maximum = 10, Value = 4 };
            tips.SetToolTip(qualIndyLapsBox, "Timed laps in one run (the average of all of them is the car's time).");
            g.Controls.Add(qualIndyLapsBox); qualOvalCtls.Add(qualIndyLapsBox);
            qualReQualBox = new CheckBox { Left = 10, Top = 112, Width = 152, Text = "Re-qualifying lanes for" };
            tips.SetToolTip(qualReQualBox, "Once every car has made its first run, two lanes open for this many minutes (game time):\n" +
                "Lane 1 (priority): the car gives up its time and goes before Lane 2; its result is its best Lane 1 run, which can be\n" +
                "slower than the time it gave up (or no time at all).\n" +
                "Lane 2: the car keeps its time and a faster run replaces it, but it only goes when Lane 1 is empty and no Lane 1 car is\n" +
                "on track, so it may never get its chance.\n" +
                "The AI teams pick a lane from where they stand: no time or outside the field = Lane 1, on the bubble = mostly Lane 2.\n" +
                "You pick yours with the lane buttons below while the lanes are open.");
            g.Controls.Add(qualReQualBox); qualOvalCtls.Add(qualReQualBox);
            qualReQualMin = new NumericUpDown { Left = 165, Top = 112, Width = 50, Minimum = 1, Maximum = 120, Value = 20 };
            g.Controls.Add(qualReQualMin); qualOvalCtls.Add(qualReQualMin);
            var rqLab = new Label { Left = 220, Top = 115, AutoSize = true, Text = "min, up to" };
            g.Controls.Add(rqLab); qualOvalCtls.Add(rqLab);
            qualReQualRuns = new NumericUpDown { Left = 290, Top = 112, Width = 45, Minimum = 2, Maximum = 10, Value = 3 };
            tips.SetToolTip(qualReQualRuns, "Runs a car may make in all, its first run included.");
            g.Controls.Add(qualReQualRuns); qualOvalCtls.Add(qualReQualRuns);
            var rqLab2 = new Label { Left = 340, Top = 115, AutoSize = true, Text = "runs a car" };
            g.Controls.Add(rqLab2); qualOvalCtls.Add(rqLab2);
            qualWaveOffBox = new CheckBox { Left = 430, Top = 112, Width = 136, Text = "AI wave-offs", Checked = true };
            tips.SetToolTip(qualWaveOffBox, "An AI team can wave off a run before its last timed lap (the run counts for nothing): a Lane 2 run that can't\n" +
                "beat its time, a first run well off the car's pace (it goes to Lane 1), a Lane 1 run heading outside the field with time\n" +
                "left to go again. Yours: come back onto pit road before your last timed lap.");
            g.Controls.Add(qualWaveOffBox); qualOvalCtls.Add(qualWaveOffBox);
            qualLaneLabel = new Label { Left = 10, Top = 148, Width = 90, Text = "Your lane:" };
            g.Controls.Add(qualLaneLabel); qualOvalCtls.Add(qualLaneLabel);
            laneBtn1 = new Button { Text = "Lane 1 (give up time)", Left = 100, Top = 143, Width = 150, Height = 26 };
            tips.SetToolTip(laneBtn1, "Priority lane: your time is given up NOW and you go before anyone in Lane 2. Your best Lane 1 run is your time.");
            laneBtn1.Click += (s, e) => { Show(Stamp + qual.ChooseLane(1)); QualUi(); };
            g.Controls.Add(laneBtn1); qualOvalCtls.Add(laneBtn1);
            laneBtn2 = new Button { Text = "Lane 2 (keep time)", Left = 256, Top = 143, Width = 150, Height = 26 };
            tips.SetToolTip(laneBtn2, "You keep your time and a faster run replaces it; you only go when Lane 1 is empty and clear of the track.");
            laneBtn2.Click += (s, e) => { Show(Stamp + qual.ChooseLane(2)); QualUi(); };
            g.Controls.Add(laneBtn2); qualOvalCtls.Add(laneBtn2);
            laneOutBtn = new Button { Text = "Done with the lanes", Left = 420, Top = 143, Width = 146, Height = 26 };
            tips.SetToolTip(laneOutBtn, "Out of the lanes (your time, if you have one, stands). The lanes close early once nobody is left in them.");
            laneOutBtn.Click += (s, e) => { Show(Stamp + qual.ChooseLane(0)); QualUi(); };
            g.Controls.Add(laneOutBtn); qualOvalCtls.Add(laneOutBtn);

            // ---- Indy road / street ----
            g = Group("Indy road / street", ref y, 48);
            qualRoadGroup = g;
            QualRadio(g, "Use Indy road / street: groups, Fast 12, Fast Six", 10, 20, 4,
                "Group 1 and Group 2 go on track in turn; the fastest of each go to the Fast 12, the fastest of those to the Fast Six\n" +
                "for pole. Grid from 13th: Group 1 on the odd spots, Group 2 on the even ones.");
            Func<int, int, string, Label> lab = (x, top, txt) => { var l = new Label { Left = x, Top = top + 3, AutoSize = true, Text = txt }; g.Controls.Add(l); qualIndyRoadCtls.Add(l); return l; };
            Func<int, int, int, int, int, NumericUpDown> num = (x, top, min, max, val) =>
            { var n = new NumericUpDown { Left = x, Top = top, Width = 50, Minimum = min, Maximum = max, Value = val }; g.Controls.Add(n); qualIndyRoadCtls.Add(n); return n; };
            lab(10, 52, "Groups:"); qualIndyGroupMin = num(110, 52, 1, 60, 10); lab(165, 52, "min each, top"); qualIndyGroupAdv = num(250, 52, 1, 20, 6); lab(305, 52, "of each group go through");
            lab(10, 80, "Fast 12:"); qualIndyF12Min = num(110, 80, 1, 60, 10); lab(165, 80, "min, top"); qualIndyF12Adv = num(250, 80, 1, 12, 6); lab(305, 80, "go to the Fast Six");
            lab(10, 108, "Fast Six:"); qualIndyF6Min = num(110, 108, 1, 60, 6); lab(165, 108, "min");
            lab(305, 108, "Break (s):"); qualIndyBreak = num(370, 108, 0, 600, 60);
            tips.SetToolTip(qualIndyBreak, "Seconds between the sessions, with every car back in its box.");
            lab(10, 136, "Split the groups by:");
            qualIndySplitBox = new ThemedComboBox { Left = 130, Top = 132, Width = 236, DropDownStyle = ComboBoxStyle.DropDownList };
            qualIndySplitBox.Items.AddRange(new object[] { "Driver rating (fastest alternate)", "Running order list (alternate)", "Random draw" });
            tips.SetToolTip(qualIndySplitBox, "Driver rating: cars ranked by the AI's qualifying rating, then 1st to Group 1, 2nd to Group 2, and so on, so the\n" +
                "groups are evenly matched. Running order list: alternating down the list. Random draw: a random split.");
            g.Controls.Add(qualIndySplitBox); qualIndyRoadCtls.Add(qualIndySplitBox);

            // ---- the field: true DNQs (any type) ----
            g = Group("Field size (DNQs)", ref y, 92);
            qualDnqGroup = g;
            g.Controls.Add(new Label { Left = 10, Top = 22, Width = 150, Text = "Cars that make the field:" });
            qualStartersBox = new NumericUpDown { Left = 165, Top = 19, Width = 55, Minimum = 0, Maximum = 100, Value = 0 };
            tips.SetToolTip(qualStartersBox, "0 = every car makes the field. Otherwise only the top cars after qualifying do; the rest did not qualify (DNQ).");
            g.Controls.Add(qualStartersBox);
            qualStartersUnit = new ThemedComboBox { Left = 226, Top = 18, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            qualStartersUnit.Items.AddRange(new object[] { "cars", "% of the field" });
            qualStartersUnit.SelectedIndex = 0;
            g.Controls.Add(qualStartersUnit);
            qualScratchBox = new CheckBox { Left = 370, Top = 20, Width = 200, Text = "Take them out before the green", Checked = true };
            tips.SetToolTip(qualScratchBox, "At the start of the race the AI cars that didn't qualify get engine damage past NR2003's must-pit line and are called\n" +
                "in, so they go to their stalls and retire there (like an engine failure). One still out when the pace car pulls off gets a\n" +
                "tow. A car still running at the green starts from the back. Your own car is never touched.");
            g.Controls.Add(qualScratchBox);
            g.Controls.Add(new Label { Left = 10, Top = 46, Width = 556, Height = 40, ForeColor = SystemColors.GrayText,
                Text = "The rest are DNQs: marked on the results and held in their boxes in a practice after qualifying (Happy Hour). " +
                    "With \"Take them out\" ticked they are sent in and retired during the pace laps." });

            // ---- AI pace and your car ----
            g = Group("AI pace and your car", ref y, 104);
            qualPaceGroup = g;
            Check(g, "QualPace", "Qualifying pace for the AI", 10, 22, true,
                "In its own qualifying session NR2003 gives every AI car a small speed bonus from its qualifying rating\n" +
                "(papy_ai.ini driver_qualifying_to_*). Practice doesn't, so with this on the cars get that bonus while they are out on\n" +
                "a qualifying run and their laps match what they'd do in qualifying.");
            Check(g, "QualMatchPace", "Match the stock qualifying pace", 300, 22, true,
                "In the qualifying session the practice laps are scaled by the median of (the game's own qualifying lap / that car's\n" +
                "practice lap), so the times look like stock qualifying times. The order stays as qualified.");
            Check(g, "QualPlayer", "I drive my own turn", 10, 48, true,
                "Your car gets a turn in the running order (or runs in the knockout / Indy rounds) like an AI car. QualControl never\n" +
                "touches your car: it tells you on screen when to go, your timed laps and when to come in. Your practice time becomes\n" +
                "your qualifying time: in the qualifying session drive one lap (any speed) and QualControl writes your practice time over it.");
            Check(g, "QualReadyPause", "Pause the game at my turn", 300, 48, true,
                "When your turn is called the game pauses and a card says so, so you can set your camera (the game's\n" +
                "keys still work). Resume with your pause key or the card's RESUME button. The time to\n" +
                "pull out of your box (the DNQ setting) only starts then. Paused time never counts towards a DNQ, for any car.");
            g.Controls.Add(new Label { Left = 10, Top = 79, Width = 330, Text = "AI boost strength (% of the game's qualifying bonus):" });
            qualBoostBox = new NumericUpDown { Left = 345, Top = 75, Width = 55, Minimum = 0, Maximum = 1000, Value = 100 };
            tips.SetToolTip(qualBoostBox, "How much of the game's qualifying speed bonus the AI cars get while out on a run: 1 + (rating - 1) x coefficient x this %.\n" +
                "100 = the stock formula. Raise it if the laps are still slower than stock qualifying; the log line 'rating ..., multipliers ...'\n" +
                "shows what each car got.");
            qualBoostBox.ValueChanged += (s2, e2) => { if (loading) return; settings["QualBoost"] = qualBoostBox.Value.ToString(); SaveSettings(); qual.BoostPercent = (double)qualBoostBox.Value; };
            g.Controls.Add(qualBoostBox);

            // ---- QualControl: where the running order comes from ----
            g = Group("Running order", ref y, 86);
            qualOrderGroup = g;
            g.Controls.Add(new Label { Left = 10, Top = 23, Width = 70, Text = "Order from:" });
            qualOrderFromBox = new ThemedComboBox { Left = 82, Top = 20, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            qualOrderFromBox.Items.AddRange(new object[] { "The list below (draw now, drag to set)", "Practice lap times (best lap)" });
            tips.SetToolTip(qualOrderFromBox, "The list below: the running order list as it stands (Random draw, or drag the drivers; cars not listed are drawn).\n" +
                "Practice lap times: in the practice session before qualifying the cars first run free while QualControl times their laps.\n" +
                "Then click 'Set the order from practice': the order goes into the list, the cars are called into their boxes, and you\n" +
                "start qualifying as usual - in the same session. To keep that order for a new session instead, load it and click 'Keep my list'.\n" +
                "Single car runs and the Indy oval go out in this order; the Indy road groups use it when they are split by the running order list.\n" +
                "Knockout rounds have no running order (everyone goes out at once).");
            g.Controls.Add(qualOrderFromBox);
            g.Controls.Add(new Label { Left = 344, Top = 23, Width = 86, Text = "Fastest goes:" });
            qualFastestBox = new ThemedComboBox { Left = 432, Top = 20, Width = 134, DropDownStyle = ComboBoxStyle.DropDownList };
            qualFastestBox.Items.AddRange(new object[] { "out first", "out last" });
            tips.SetToolTip(qualFastestBox, "Out first: the fastest in practice goes out first, the slowest last.\nOut last: the other way round (the fastest goes out last).\n" +
                "Cars without a practice lap go to the end away from the fastest, in a random draw.");
            g.Controls.Add(qualFastestBox);
            qualFromPracticeBtn = new Button { Text = "Set the order from practice", Left = 10, Top = 50, Width = 200, Height = 28 };
            tips.SetToolTip(qualFromPracticeBtn, "Puts the cars in the running order list by their best practice lap so far, then calls every car into its pit box\n" +
                "and holds it there for qualifying (the Start button above). You can still drag drivers about in the list before you start.");
            qualFromPracticeBtn.Click += (s2, e2) =>
            {
                var nums = qual.SetOrderFromPractice();
                if (nums == null) { Show(Stamp + "running order: no practice laps yet"); return; }
                try { foreach (var r in Qualifying.Roster(mem)) orderNames[r.Item2] = r.Item3; } catch (Exception) { }
                orderOrder = nums;
                OrderChanged();
                Show(Stamp + "running order set from practice: " + nums.Count + " cars");
                QualUi();
            };
            g.Controls.Add(qualFromPracticeBtn);
            qualKeepListBtn = new Button { Text = "Keep my list", Left = 216, Top = 50, Width = 120, Height = 28 };
            tips.SetToolTip(qualKeepListBtn, "Ends the practice timing without using it: the running order list stands as it is (for example the order\n" +
                "set from practice in an earlier session). The cars are called into their boxes and held for qualifying.");
            qualKeepListBtn.Click += (s2, e2) => { qual.KeepList(); QualUi(); };
            g.Controls.Add(qualKeepListBtn);

            // ---- QualControl: saved results (save now, reapply exactly in a later weekend) ----
            g = Group("Saved results", ref y, 80);
            qualSavedGroup = g;
            qualSaveBtn = new Button { Text = "Save results...", Left = 10, Top = 20, Width = 130, Height = 28 };
            tips.SetToolTip(qualSaveBtn, "Saves the qualifying results (finishing order, every car's times, the DNQs) to a file of your naming.\n" +
                "Save them any time after qualifying is done. Saved before the qualifying session with 'Match the stock qualifying pace'\n" +
                "on, the file is updated when that session ends, with the times exactly as the game showed them.");
            qualSaveBtn.Click += (s2, e2) => SaveResultsClick();
            g.Controls.Add(qualSaveBtn);
            qualReapplyBtn = new Button { Text = "Reapply saved results...", Left = 146, Top = 20, Width = 170, Height = 28 };
            tips.SetToolTip(qualReapplyBtn, "Picks a saved results file and makes its times this weekend's qualifying times: they are written EXACTLY as saved\n" +
                "in the qualifying session (no pace matching), so the grid and the times are the same as when they were saved.\n" +
                "Load the weekend first (the practice before qualifying, or the qualifying session itself). No custom qualifying runs\n" +
                "in that practice then. Cars are matched by car number; cars without a saved time go to the back.");
            qualReapplyBtn.Click += (s2, e2) => ReapplyResultsClick();
            g.Controls.Add(qualReapplyBtn);
            var folderBtn = new Button { Text = "Open folder", Left = 322, Top = 20, Width = 100, Height = 28 };
            tips.SetToolTip(folderBtn, "Opens the folder the results are saved in.");
            folderBtn.Click += (s2, e2) =>
            {
                try { Directory.CreateDirectory(Qualifying.ResultsDir); System.Diagnostics.Process.Start("explorer.exe", "\"" + Qualifying.ResultsDir + "\""); }
                catch (Exception ex) { Show(Stamp + "saved results: " + ex.Message); }
            };
            g.Controls.Add(folderBtn);
            var mergeBtn = new Button { Text = "Merge saved results...", Left = 428, Top = 20, Width = 150, Height = 28 };
            tips.SetToolTip(mergeBtn, "Merges saved results of several qualifying sessions (for example two groups with two different rosters) into one\n" +
                "finishing order: by round reached, then time; a driver in more than one session keeps the best; ties go to the\n" +
                "earlier session. The merged order is saved as a results file you reapply in the race weekend.");
            mergeBtn.Click += (s2, e2) =>
            {
                string v; int fc;
                int cars = settings.TryGetValue("MergeField", out v) && int.TryParse(v, out fc) ? fc : 43;
                bool meIn = !settings.TryGetValue("MergePlayerIn", out v) || v != "0";
                using (var mf = new MergeForm(Show, Nr2003Dir, cars, meIn))
                {
                    mf.ShowDialog(this);
                    settings["MergeField"] = mf.FieldCars.ToString(); settings["MergePlayerIn"] = mf.PlayerAlwaysIn ? "1" : "0"; SaveSettings();
                }
            };
            g.Controls.Add(mergeBtn);
            qualSavedLabel = new Label { Left = 10, Top = 54, Width = 556, Height = 20, ForeColor = SystemColors.GrayText, Text = "Nothing saved or reapplied yet." };
            g.Controls.Add(qualSavedLabel);

            // ---- running order and results, side by side ----
            g = Group("Running order and results", ref y, 360);
            qualListsGroup = g;
            Func<string, int, int, Button> ob = (txt, w, x) => { var b = new Button { Text = txt, Left = x, Top = 20, Width = w, Height = 28 }; g.Controls.Add(b); return b; };
            var getField = ob("Get the field", 110, 10);
            tips.SetToolTip(getField, "Fills the running order list with the AI cars in this session (NR2003 running, a session loaded). Keeps the order you set.");
            getField.Click += (s2, e2) => OrderGetField();
            var drawBtn = ob("Random draw", 100, 126);
            tips.SetToolTip(drawBtn, "Shuffles the list into a random order.");
            drawBtn.Click += (s2, e2) => { var r = new Random(); orderOrder = orderOrder.OrderBy(x => r.Next()).ToList(); OrderChanged(); };
            var upBtn = ob("Up", 56, 232);
            tips.SetToolTip(upBtn, "Moves the selected driver one place earlier in the running order.");
            upBtn.Click += (s2, e2) => OrderMove(-1);
            var downBtn = ob("Down", 56, 294);
            tips.SetToolTip(downBtn, "Moves the selected driver one place later in the running order.");
            downBtn.Click += (s2, e2) => OrderMove(1);
            var clearBtn = ob("Clear (random draw)", 140, 356);
            tips.SetToolTip(clearBtn, "Empties the list: a random draw for everyone when qualifying starts.");
            clearBtn.Click += (s2, e2) => { orderOrder.Clear(); OrderChanged(); };
            qualOrderLabel = new Label { Left = 10, Top = 56, Width = 200, Text = "Running order (drag to move)" };
            g.Controls.Add(qualOrderLabel);
            qualResultsLabel = new Label { Left = 300, Top = 56, Width = 200, Text = "Finished times" };
            g.Controls.Add(qualResultsLabel);
            orderList = new ListBox { Left = 10, Top = 76, Width = 270, Height = 274, IntegralHeight = false, Font = new Font("Consolas", 9.5f), AllowDrop = true };
            tips.SetToolTip(orderList, "Single car runs and the Indy oval: the order the cars go out in. Drag a driver to the place they go out.\n" +
                "Cars not in the list go after them in a random draw. Also splits the Indy road groups when 'Running order list' is chosen.");
            orderList.MouseDown += (s2, e2) =>
            {
                int i = orderList.IndexFromPoint(e2.Location);
                if (i >= 0 && e2.Button == MouseButtons.Left) orderList.DoDragDrop(i, DragDropEffects.Move);
            };
            orderList.DragOver += (s2, e2) => { e2.Effect = DragDropEffects.Move; };
            orderList.DragDrop += (s2, e2) =>
            {
                if (!e2.Data.GetDataPresent(typeof(int))) return;
                int from = (int)e2.Data.GetData(typeof(int));
                int to = orderList.IndexFromPoint(orderList.PointToClient(new Point(e2.X, e2.Y)));
                if (to < 0) to = orderOrder.Count - 1;
                if (from < 0 || from >= orderOrder.Count || to == from) return;
                string n = orderOrder[from]; orderOrder.RemoveAt(from); orderOrder.Insert(to, n);
                OrderChanged(); orderList.SelectedIndex = to;
            };
            g.Controls.Add(orderList);
            qualBoard = new ListBox { Left = 290, Top = 76, Width = 276, Height = 274, IntegralHeight = false, Font = new Font("Consolas", 9f), HorizontalScrollbar = true };
            tips.SetToolTip(qualBoard, "The times as they are posted, fastest first, with the gap to pole.");
            g.Controls.Add(qualBoard);
            // the two lists share the width of the window: half each, whatever size it is
            g.SizeChanged += (s2, e2) => PlaceQualLists();
            PlaceQualLists();
            page.Layout += (s2, e2) => PlaceQualLists();
            page.SizeChanged += (s2, e2) => PlaceQualLists();
            qualStack = new List<Control> { qualTopGroup, qualSingleGroup, qualKoGroup, qualOvalGroup, qualRoadGroup, qualDnqGroup, qualPaceGroup, qualOrderGroup, qualSavedGroup, qualListsGroup };
            ((ScrollableControl)page).AutoScrollMargin = new Size(0, 8);

            // ---- Overlays tab: what QualControl draws over the game ----
            y = Tab("Overlays");
            g = Group("Timing box", ref y, 84);
            Check(g, "ShowQualBox", "Show the timing box over the game", 10, 22, true,
                "A TV style box while qualifying runs: pole on top, below it the car on its run with its live gap to pole (green =\n" +
                "faster) on every timed lap, and its lap time when it crosses the line. Single car runs: after the last timed lap, its\n" +
                "best lap, gap and place for 5 s, then the next car with -.-- until it takes the green. Knockout rounds: the car on TV.\n" +
                "NR2003 must run windowed or borderless.");
            var qualPlaceBtn = new Button { Text = "Place the box", Left = 250, Top = 18, Width = 110, Height = 26 };
            tips.SetToolTip(qualPlaceBtn, "Shows the box over the game: drag it where you want it, the mouse wheel sizes it,\nEnter or double-click saves, Esc cancels. The broadcast panel uses the same spot and size.");
            qualPlaceBtn.Click += (s, e) => PlaceQualBox();
            g.Controls.Add(qualPlaceBtn);
            g.Controls.Add(new Label { Left = 10, Top = 56, Width = 40, Text = "Style:" });
            qualStyleBox = new ThemedComboBox { Left = 52, Top = 52, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
            qualStyleBox.Items.AddRange(new object[] { "Classic timing box", "Broadcast (driver, telemetry, laps, order)" });
            tips.SetToolTip(qualStyleBox, "Classic: the small TV box (pole on top, the car on its run below with its gap).\n" +
                "Broadcast: the full panel from the Figma design - the driver, live telemetry (speed, gear, RPM, throttle, brake), the lap\n" +
                "block (lap n of N, current, last and best lap) and the running order, which slides in between runs and every 15 s during\n" +
                "one. Lime while the car is on pole pace, red when slower. Works with any qualifying type.");
            qualStyleBox.SelectedIndexChanged += (s, e) => { if (loading) return; settings["QualBoxStyle"] = qualStyleBox.SelectedIndex == 1 ? "broadcast" : "classic"; SaveSettings(); };
            g.Controls.Add(qualStyleBox);
            g.Controls.Add(new Label { Left = 262, Top = 56, Width = 84, Text = "Lap times as:" });
            qualLapUnitBox = new ThemedComboBox { Left = 348, Top = 52, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            qualLapUnitBox.Items.AddRange(new object[] { "Time", "Speed (mph)" });
            tips.SetToolTip(qualLapUnitBox, "Lap times on the timing box, the broadcast panel and the finished times list: as times (m:ss.fff from a minute up),\n" +
                "or as speeds in mph like NR2003's own option (the track length over the lap time). Gaps stay in seconds.");
            qualLapUnitBox.SelectedIndexChanged += (s, e) => { LapFmt.Speed = qualLapUnitBox.SelectedIndex == 1; if (loading) return; settings["QualLapUnit"] = LapFmt.Speed ? "speed" : "time"; SaveSettings(); };
            g.Controls.Add(qualLapUnitBox);
            var artBtn = new Button { Text = "Art folder...", Left = 476, Top = 50, Width = 92, Height = 26 };
            tips.SetToolTip(artBtn, "Number art and headshots for the boxes, laid out as series/<series>/numbers and headshots (AIMOD's art folder\n" +
                "works). QualControl looks in its own art folder, then this one. Cancel = no extra folder.");
            artBtn.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Folder with series/<series>/numbers and headshots (for example AIMOD's art folder). Cancel clears it.", SelectedPath = art.ExtraFolder ?? "" })
                    settings["ArtFolder"] = dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : "";
                SaveSettings();
                ApplyOptions();
            };
            g.Controls.Add(artBtn);
            g.Height = 108;
            artLabel = new Label { Left = 10, Top = 84, Width = 556, Height = 18, AutoEllipsis = true, ForeColor = SystemColors.GrayText };
            g.Controls.Add(artLabel);
            y += 24;

            g = Group("Banner", ref y, 54);
            Check(g, "ShowPlayerCalls", "Show the calls to me over the game", 10, 22, true,
                "A banner across the top of the game: YOUR RUN: GO NOW, TIMED LAP 1 OF 2, RUN COMPLETE - COME IN, CHECKERED, THROUGH TO\n" +
                "ROUND 2, DNQ... On the right a countdown: when QualControl expects you at the green (single car runs), the time left to\n" +
                "pull out of your box before a DNQ, the time left in a round.\n" +
                "NR2003 must run windowed or borderless.");
            var bannerPlaceBtn = new Button { Text = "Place the banner", Left = 300, Top = 18, Width = 120, Height = 26 };
            tips.SetToolTip(bannerPlaceBtn, "Shows the banner over the game: drag it where you want it, the mouse wheel sizes it,\nEnter or double-click saves, Esc cancels.");
            bannerPlaceBtn.Click += (s, e) => PlaceBanner();
            g.Controls.Add(bannerPlaceBtn);
            qual.Logger = Show;
            EventHandler qualChanged = (s, e) => { if (loading) return; SaveQualSettings(); };
            // one format at a time: an Indy type turns the NASCAR format off and the other way round
            qualFormatBox.SelectedIndexChanged += (s, e) => { if (!loading && qualFormatBox.SelectedIndex > 0 && qualIndyBox.SelectedIndex > 0) qualIndyBox.SelectedIndex = 0; qualChanged(s, e); };
            qualIndyBox.SelectedIndexChanged += (s, e) => { if (!loading && qualIndyBox.SelectedIndex > 0 && qualFormatBox.SelectedIndex > 0) qualFormatBox.SelectedIndex = 0; qualChanged(s, e); };
            qualIndyLapsBox.ValueChanged += qualChanged;
            qualReQualBox.CheckedChanged += qualChanged; qualWaveOffBox.CheckedChanged += qualChanged;
            qualReQualMin.ValueChanged += qualChanged; qualReQualRuns.ValueChanged += qualChanged;
            qualStartersBox.ValueChanged += qualChanged; qualStartersUnit.SelectedIndexChanged += qualChanged; qualScratchBox.CheckedChanged += qualChanged;
            foreach (var n in new[] { qualIndyGroupMin, qualIndyGroupAdv, qualIndyF12Min, qualIndyF12Adv, qualIndyF6Min, qualIndyBreak }) n.ValueChanged += qualChanged;
            qualIndySplitBox.SelectedIndexChanged += qualChanged;
            qualLapsBox.ValueChanged += qualChanged;
            qualDnqBox.ValueChanged += qualChanged;
            qualReleaseBox.SelectedIndexChanged += qualChanged;
            qualReleaseVal.ValueChanged += qualChanged;
            qualMaxOutBox.ValueChanged += qualChanged;
            qualOrderBox.TextChanged += qualChanged;
            qualRoundsBox.TextChanged += qualChanged;
            qualBreakBox.ValueChanged += qualChanged;
            qualOrderFromBox.SelectedIndexChanged += qualChanged; qualFastestBox.SelectedIndexChanged += qualChanged;

            y = Tab("Log");
            logBox = new ListBox { Left = 4, Top = y, Width = 576, Height = 300, HorizontalScrollbar = true, IntegralHeight = false, Font = new Font("Consolas", 8.5f) };
            page.Controls.Add(logBox);
            // fill the rest of the tab whatever its size (the tab's size isn't known yet while the window is built)
            var logPage = page; int logTop = y;
            logPage.Layout += (s2, e2) => logBox.SetBounds(4, logTop, Math.Max(100, logPage.ClientSize.Width - 8), Math.Max(80, logPage.ClientSize.Height - logTop - 4));

            LoadSettings();
            ApplyOptions();

            // light / dark look (top right, on the black band): follow Windows, or force one
            header.Controls.Add(new Label { Text = "Theme:", AutoSize = true, Top = 22, Left = header.Width - 178, Anchor = AnchorStyles.Top | AnchorStyles.Right });
            themeBox = new ThemedComboBox { Left = header.Width - 130, Top = 18, Width = 118, DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            themeBox.Items.AddRange(new object[] { "Follow Windows", "Light", "Dark" });
            string th;
            themeBox.SelectedIndex = !settings.TryGetValue("Theme", out th) ? 0 : th == "dark" ? 2 : th == "light" ? 1 : 0;
            themeBox.SelectedIndexChanged += (s, e) =>
            {
                settings["Theme"] = themeBox.SelectedIndex == 2 ? "dark" : themeBox.SelectedIndex == 1 ? "light" : "system";
                SaveSettings();
                ApplyTheme();
            };
            header.Controls.Add(themeBox);
            ApplyTheme();

            tick = new Timer { Interval = 10 };
            tick.Tick += (s, e) =>
            {
                if (mem == null) return;
                // AI telemetry for the broadcast box: samples itself at most ~30 times a game second (Telemetry.cs)
                if (qual.LiveOn)
                    try { tele.Sample(mem, qual.Timing, qual.Field.ToList()); }
                    catch (GameMemoryException) { }
                if ((DateTime.Now - lastQualTick).TotalMilliseconds >= 100)
                {
                    lastQualTick = DateTime.Now;
                    HubFeed.Current.Tick();
                    try { qual.Tick(mem); QualBoxTick(); }
                    catch (GameMemoryException) { }
                    try { if (qual.TakeReadyRequest()) ShowQualReady(); QualReadyTick(); }
                    catch (GameMemoryException) { }
                    QualUi();
                }
            };
            slowTimer = new Timer { Interval = 1000 };
            slowTimer.Tick += (s, e) => SlowTick();
            slowTimer.Start();

            FormClosing += (s, e) => { try { qual.Shutdown(); } catch (Exception) { } };
            Show("QualControl build " + Version + " started, log: " + logPath);
            SlowTick();
        }

        // A new tab; the groups that follow go on it. Returns where the first one goes.
        int Tab(string caption)
        {
            var p = new TabPage(caption) { AutoScroll = true, UseVisualStyleBackColor = true };
            tabs.TabPages.Add(p);
            page = p;
            return 6;
        }

        GroupBox Group(string caption, ref int y, int height)
        {
            bool top = page == this;
            var g = new ThemedGroupBox { Text = caption, Left = top ? 12 : 4, Top = y, Width = top ? 596 : 576, Height = height, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            page.Controls.Add(g);
            y += height + 6;
            return g;
        }

        void Check(GroupBox g, string key, string caption, int left, int top, bool def, string tip)
        {
            var cb = new CheckBox { Text = caption, Left = left, Top = top, AutoSize = true, Checked = def };
            tips.SetToolTip(cb, tip);
            cb.CheckedChanged += (s, e) => { ApplyOptions(); SaveSettings(); };
            g.Controls.Add(cb);
            checks[key] = cb;
        }

        // ---- settings: QualControl.ini, key=value (the qualifying keys are RaceControl's, so its ini lines can be copied over) ----

        void LoadSettings()
        {
            try
            {
                if (File.Exists(iniPath))
                    foreach (string line in File.ReadAllLines(iniPath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) settings[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
            }
            catch { }
            loading = true;
            foreach (var kv in checks)
            {
                string v;
                if (settings.TryGetValue(kv.Key, out v)) kv.Value.Checked = v == "1";
            }
            string sv, bl;
            int sc;
            qualBoxLayout = BoardLayout.Parse(settings.TryGetValue("QualBox", out bl) ? bl : null);
            bannerLayout = BoardLayout.Parse(settings.TryGetValue("PlayerBanner", out bl) ? bl : null);
            qualFormatBox.SelectedIndex = !settings.TryGetValue("QualFormat", out sv) ? 0 : sv == "knockout" ? 2 : sv == "single" ? 1 : 0;
            qualIndyBox.SelectedIndex = sv == "indy_oval" ? 1 : sv == "indy_road" ? 2 : 0;
            {   // "group min, through, Fast 12 min, through, Fast Six min, split (rating / list / random), break s"
                string[] ir = (settings.TryGetValue("QualIndyRoad", out sv) ? sv : "").Split(',');
                Func<int, decimal, decimal> at = (i, def) => { decimal v; return i < ir.Length && decimal.TryParse(ir[i], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def; };
                Action<NumericUpDown, decimal> set = (nb, v) => nb.Value = Math.Max(nb.Minimum, Math.Min(nb.Maximum, v));
                set(qualIndyGroupMin, at(0, 10)); set(qualIndyGroupAdv, at(1, 6)); set(qualIndyF12Min, at(2, 10)); set(qualIndyF12Adv, at(3, 6)); set(qualIndyF6Min, at(4, 6));
                qualIndySplitBox.SelectedIndex = ir.Length > 5 && ir[5] == "list" ? 1 : ir.Length > 5 && ir[5] == "random" ? 2 : 0;
                set(qualIndyBreak, at(6, 60));
            }
            qualIndyLapsBox.Value = settings.TryGetValue("QualIndyLaps", out sv) && int.TryParse(sv, out sc) ? Math.Max(1, Math.Min(10, sc)) : 4;
            {   // "on (1/0), minutes, runs per car, AI wave-offs (1/0)"
                string[] rq = (settings.TryGetValue("QualReQual", out sv) ? sv : "").Split(',');
                int a;
                qualReQualBox.Checked = rq.Length > 0 && rq[0] == "1";
                qualReQualMin.Value = rq.Length > 1 && int.TryParse(rq[1], out a) ? Math.Max(1, Math.Min(120, a)) : 20;
                qualReQualRuns.Value = rq.Length > 2 && int.TryParse(rq[2], out a) ? Math.Max(2, Math.Min(10, a)) : 3;
                qualWaveOffBox.Checked = !(rq.Length > 3 && rq[3] == "0");
                // "N" cars or "N%" of the field; 0 = everyone
                string st = settings.TryGetValue("QualStarters", out sv) ? sv.Trim() : "0";
                bool pct = st.EndsWith("%");
                qualStartersBox.Value = int.TryParse(st.TrimEnd('%'), out a) ? Math.Max(0, Math.Min(100, a)) : 0;
                qualStartersUnit.SelectedIndex = pct ? 1 : 0;
                qualScratchBox.Checked = !settings.TryGetValue("QualDnqScratch", out sv) || sv.Trim() != "0";
            }
            qualStyleBox.SelectedIndex = settings.TryGetValue("QualBoxStyle", out sv) && sv == "broadcast" ? 1 : 0;
            qualLapUnitBox.SelectedIndex = settings.TryGetValue("QualLapUnit", out sv) && sv == "speed" ? 1 : 0;
            LapFmt.Speed = qualLapUnitBox.SelectedIndex == 1;
            qualLapsBox.Value = settings.TryGetValue("QualLaps", out sv) && int.TryParse(sv, out sc) ? Math.Max(1, Math.Min(5, sc)) : 1;
            qualOrderBox.Text = settings.TryGetValue("QualOrder", out sv) ? sv : "";
            qualOrderFromBox.SelectedIndex = settings.TryGetValue("QualOrderFrom", out sv) && sv == "practice" ? 1 : 0;
            qualFastestBox.SelectedIndex = settings.TryGetValue("QualFastest", out sv) && sv == "last" ? 1 : 0;
            qualRoundsBox.Text = settings.TryGetValue("QualRounds", out sv) && sv != "" ? sv : "25:24, 10:12, 5:12";
            RoundRowsFromText();
            qualBoostBox.Value = settings.TryGetValue("QualBoost", out sv) && int.TryParse(sv, out sc) ? Math.Max(0, Math.Min(1000, sc)) : 100;
            orderOrder = qualOrderBox.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimStart('#')).ToList();
            ShowOrderList();
            qualBreakBox.Value = settings.TryGetValue("QualBreak", out sv) && int.TryParse(sv, out sc) ? Math.Max(0, Math.Min(600, sc)) : 60;
            qualDnqBox.Value = settings.TryGetValue("QualDnq", out sv) && int.TryParse(sv, out sc) ? Math.Max(15, Math.Min(600, sc)) : 90;
            settings.TryGetValue("QualRelease", out sv);
            qualReleaseBox.SelectedIndex = sv == "seconds" ? 1 : sv == "percent" || sv == "schedule" ? 2 : sv == "gap" || sv == "pitexit" ? 3 : 0;
            qualMaxOutBox.Value = settings.TryGetValue("QualMaxOut", out sv) && int.TryParse(sv, out sc) ? Math.Max(1, Math.Min(20, sc)) : 3;
            qualReleaseVal.Value = settings.TryGetValue("QualReleaseValue", out sv) && int.TryParse(sv, out sc) ? Math.Max(0, Math.Min(600, sc)) : 30;
            loading = false;
            ApplyQualSettings();
        }

        void SaveSettings()
        {
            if (loading) return;
            foreach (var kv in checks) settings[kv.Key] = kv.Value.Checked ? "1" : "0";
            try { File.WriteAllLines(iniPath, settings.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value).ToArray()); } catch { }
        }

        void SaveQualSettings()
        {
            settings["QualFormat"] = qualIndyBox.SelectedIndex == 2 ? "indy_road" : qualIndyBox.SelectedIndex == 1 ? "indy_oval" : qualFormatBox.SelectedIndex == 2 ? "knockout" : qualFormatBox.SelectedIndex == 1 ? "single" : "off";
            settings["QualIndyLaps"] = qualIndyLapsBox.Value.ToString();
            settings["QualReQual"] = (qualReQualBox.Checked ? "1" : "0") + "," + qualReQualMin.Value + "," + qualReQualRuns.Value + "," + (qualWaveOffBox.Checked ? "1" : "0");
            settings["QualStarters"] = qualStartersBox.Value + (qualStartersUnit.SelectedIndex == 1 ? "%" : "");
            settings["QualDnqScratch"] = qualScratchBox.Checked ? "1" : "0";
            settings["QualIndyRoad"] = string.Join(",", new[] { qualIndyGroupMin.Value.ToString(), qualIndyGroupAdv.Value.ToString(), qualIndyF12Min.Value.ToString(),
                qualIndyF12Adv.Value.ToString(), qualIndyF6Min.Value.ToString(), qualIndySplitBox.SelectedIndex == 1 ? "list" : qualIndySplitBox.SelectedIndex == 2 ? "random" : "rating",
                qualIndyBreak.Value.ToString() });
            settings["QualLaps"] = qualLapsBox.Value.ToString();
            settings["QualOrder"] = qualOrderBox.Text.Trim();
            settings["QualOrderFrom"] = qualOrderFromBox.SelectedIndex == 1 ? "practice" : "list";
            settings["QualFastest"] = qualFastestBox.SelectedIndex == 1 ? "last" : "first";
            settings["QualRounds"] = qualRoundsBox.Text.Trim();
            settings["QualBreak"] = qualBreakBox.Value.ToString();
            settings["QualDnq"] = qualDnqBox.Value.ToString();
            settings["QualRelease"] = qualReleaseBox.SelectedIndex == 1 ? "seconds" : qualReleaseBox.SelectedIndex == 2 ? "percent" : qualReleaseBox.SelectedIndex == 3 ? "gap" : "pitroad";
            settings["QualMaxOut"] = qualMaxOutBox.Value.ToString();
            settings["QualReleaseValue"] = qualReleaseVal.Value.ToString();
            SaveSettings();
            ApplyQualSettings();
        }

        // Qualifying type, from the two stored choices: 0 off, 1 single car, 2 knockout, 3 Indy oval, 4 Indy road
        int QualKind { get { return qualIndyBox.SelectedIndex == 2 ? 4 : qualIndyBox.SelectedIndex == 1 ? 3 : qualFormatBox.SelectedIndex == 2 ? 2 : qualFormatBox.SelectedIndex == 1 ? 1 : 0; } }

        RadioButton QualRadio(GroupBox g, string text, int left, int top, int kind, string tip)
        {
            var r = new RadioButton { Text = text, Left = left, Top = top, AutoSize = true };
            tips.SetToolTip(r, tip);
            r.CheckedChanged += (s, e) => { if (!qualSync && !loading && r.Checked) QualPick(kind); };
            g.Controls.Add(r);
            qualRadios[kind] = r;
            return r;
        }

        // a type chosen with its radio button: set the two stored choices (the Indy type is set first so the engine never sees "off" in between)
        void QualPick(int kind)
        {
            if (kind >= 3) { qualIndyBox.SelectedIndex = kind == 3 ? 1 : 2; qualFormatBox.SelectedIndex = 0; }
            else { qualFormatBox.SelectedIndex = kind; qualIndyBox.SelectedIndex = 0; }
            LayoutQualTab();
        }

        // the running order and the finished times share the width of the window, half each
        void PlaceQualLists()
        {
            var g = qualListsGroup;
            if (g == null || qualBoard == null || orderList == null || qualPage == null) return;
            // the room is what the tab shows (its width less its scroll bar and the box's margins), not what the box was stretched to
            int w = Math.Min(g.ClientSize.Width, qualPage.ClientSize.Width - 8 - 8);
            int gap = 10, half = Math.Max(100, (w - 20 - gap) / 2), h = Math.Max(60, g.ClientSize.Height - 86);
            orderList.SetBounds(10, 76, half, h);
            qualBoard.SetBounds(10 + half + gap, 76, half, h);
            qualResultsLabel.Left = 10 + half + gap;
        }

        // Only the chosen type's box is open (the others fold to their header); the boxes restack under each other.
        void LayoutQualTab()
        {
            if (qualStack == null) return;
            int kind = QualKind;
            qualSync = true;
            for (int i = 0; i < qualRadios.Length; i++) qualRadios[i].Checked = i == kind;
            qualSync = false;
            bool single = kind == 1, ko = kind == 2, oval = kind == 3, road = kind == 4;
            foreach (Control c in qualKoBody) c.Visible = ko;
            foreach (Control c in qualOvalCtls) c.Visible = oval;
            foreach (Control c in qualIndyRoadCtls) c.Visible = road;
            // the shared run settings sit in the single car box, or in the Indy oval box
            GroupBox host = single ? qualSingleGroup : oval ? qualOvalGroup : null;
            singleRunsPanel.Visible = host != null;
            if (host != null && singleRunsPanel.Parent != host) { singleRunsPanel.Parent = host; singleRunsPanel.Left = 4; singleRunsPanel.Top = 46; }
            qualLapsLabel.Visible = qualLapsBox.Visible = single;
            singleRunsPanel.Height = single ? 86 : 58;
            qualSingleGroup.Height = single ? 140 : 48;
            qualKoGroup.Height = ko ? 140 : 48;
            qualOvalGroup.Height = oval ? 178 : 48;
            qualRoadGroup.Height = road ? 172 : 48;
            // the page scrolls: a box's Top is where it shows now, so the stack starts at the top of the page as scrolled
            // (AutoScrollPosition.Y is minus the scroll), not at the top of the view - else a gap opens above the first box
            int y = 6 + ((ScrollableControl)qualPage).AutoScrollPosition.Y;
            foreach (Control c in qualStack) { if (c.Top != y) c.Top = y; y += c.Height + 6; }
        }

        void ApplyQualSettings()
        {
            qual.Mode = qualIndyBox.SelectedIndex == 2 ? Qualifying.Format.IndyRoad : qualIndyBox.SelectedIndex == 1 ? Qualifying.Format.IndyOval : qualFormatBox.SelectedIndex == 2 ? Qualifying.Format.Knockout : qualFormatBox.SelectedIndex == 1 ? Qualifying.Format.Single : Qualifying.Format.Off;
            qual.Laps = qual.Mode == Qualifying.Format.IndyOval ? (int)qualIndyLapsBox.Value : (int)qualLapsBox.Value;
            qual.Order = qualOrderBox.Text;
            qual.PracticeOrder = qualOrderFromBox.SelectedIndex == 1 && qual.Mode != Qualifying.Format.Off;
            qual.FastestLast = qualFastestBox.SelectedIndex == 1;
            qualFastestBox.Enabled = qualOrderFromBox.SelectedIndex == 1;
            qual.Rounds = qualRoundsBox.Text;
            qual.BreakSeconds = qual.Mode == Qualifying.Format.IndyRoad ? (double)qualIndyBreak.Value : (double)qualBreakBox.Value;
            qual.IndyGroupMin = (double)qualIndyGroupMin.Value; qual.IndyGroupAdv = (int)qualIndyGroupAdv.Value;
            qual.IndyF12Min = (double)qualIndyF12Min.Value; qual.IndyF12Adv = (int)qualIndyF12Adv.Value; qual.IndyF6Min = (double)qualIndyF6Min.Value;
            qual.IndySplit = qualIndySplitBox.SelectedIndex == 1 ? Qualifying.Split.List : qualIndySplitBox.SelectedIndex == 2 ? Qualifying.Split.Random : Qualifying.Split.Rating;
            qual.DnqSeconds = (double)qualDnqBox.Value;
            qual.ReQual = qualReQualBox.Checked; qual.ReQualMinutes = (double)qualReQualMin.Value; qual.ReQualMaxRuns = (int)qualReQualRuns.Value;
            qual.WaveOffs = qualWaveOffBox.Checked;
            qual.Starters = (int)qualStartersBox.Value; qual.StartersPercent = qualStartersUnit.SelectedIndex == 1; qual.ScratchDnqs = qualScratchBox.Checked;
            qualReQualMin.Enabled = qualReQualRuns.Enabled = qualWaveOffBox.Enabled = qualReQualBox.Checked;
            qualStartersUnit.Enabled = qualScratchBox.Enabled = qualStartersBox.Value > 0;
            qual.FuelPercent = 0;                                   // the fuel load option was removed in 3.4.0: cars keep their own fuel
            qual.BoostPercent = (double)qualBoostBox.Value;
            qual.ReleaseMode = qualReleaseBox.SelectedIndex == 1 ? Qualifying.Release.Seconds : qualReleaseBox.SelectedIndex == 2 ? Qualifying.Release.LapPercent : qualReleaseBox.SelectedIndex == 3 ? Qualifying.Release.Gap : Qualifying.Release.PitRoad;
            qual.MaxOut = (int)qualMaxOutBox.Value;
            qual.ReleaseValue = (double)qualReleaseVal.Value;
            bool single = qual.SingleRuns, ko = qual.Mode == Qualifying.Format.Knockout;
            qualLapsBox.Enabled = qualOrderBox.Enabled = qualDnqBox.Enabled = qualReleaseBox.Enabled = single;
            qualLapsBox.Enabled = qual.Mode == Qualifying.Format.Single;      // the Indy oval has its own laps per run
            qualIndyLapsBox.Enabled = qual.Mode == Qualifying.Format.IndyOval;
            qualMaxOutBox.Enabled = single;
            qualReleaseVal.Enabled = single && qualReleaseBox.SelectedIndex >= 1;
            qualRoundsBox.Enabled = qualBreakBox.Enabled = ko;
            var rounds = Qualifying.ParseRounds(qualRoundsBox.Text);
            qualNeeds.Text = single ? "Practice needs about 1 to 1.5 minutes per car, plus time for the cars to reach their boxes."
                : ko ? (rounds.Count == 0 ? "Rounds: minutes : cars through, for example 25:24, 10:12, 5:12"
                    : string.Format("Practice needs about {0:0} minutes.", rounds.Sum(r => r.Item1) + (double)qualBreakBox.Value / 60 * (rounds.Count - 1) + 3))
                : qual.Mode == Qualifying.Format.IndyRoad ? string.Format("Practice needs about {0:0} minutes.", (double)(2 * qualIndyGroupMin.Value + qualIndyF12Min.Value + qualIndyF6Min.Value) + (double)qualIndyBreak.Value / 60 * 3 + 3)
                : "Off: practice and qualifying are left to the game.";
            LayoutQualTab();
            QualUi();
        }

        void QualUi()
        {
            if (qualStatus == null) return;
            qualStatus.Text = qual.Status;
            qualStart.Enabled = qual.CanStart;
            qualStop.Enabled = qual.Active;
            qualFromPracticeBtn.Enabled = qualKeepListBtn.Enabled = qual.PracticeTiming;
            qualResultsLabel.Text = qual.PracticeTiming ? "Practice best laps" : "Finished times";
            qualSaveBtn.Enabled = qual.HasResults;
            if (laneBtn1 != null)
            {
                bool lanes = qual.LanesOpen;
                int pl = qual.PlayerLane;
                laneBtn1.Enabled = lanes && qual.PlayerCanPickLane && pl != 1;
                laneBtn2.Enabled = lanes && qual.PlayerCanPickLane && pl != 2;
                laneOutBtn.Enabled = lanes && (qual.PlayerCanPickLane || pl > 0);
                qualLaneLabel.Text = !lanes ? "Your lane:" : pl == 1 ? "Lane 1" : pl == 2 ? "Lane 2" : "No lane";
            }
            var lines = qual.Board;
            bool same = qualBoard.Items.Count == lines.Count;
            for (int i = 0; same && i < lines.Count; i++) same = (string)qualBoard.Items[i] == lines[i];
            if (same) return;
            qualBoard.BeginUpdate();
            qualBoard.Items.Clear();
            foreach (string l in lines) qualBoard.Items.Add(l);
            qualBoard.EndUpdate();
        }

        void ApplyOptions()
        {
            if (checks.ContainsKey("QualPace")) qual.QualPace = checks["QualPace"].Checked;
            if (checks.ContainsKey("QualMatchPace")) qual.MatchStockPace = checks["QualMatchPace"].Checked;
            if (checks.ContainsKey("QualPlayer")) qual.IncludePlayer = checks["QualPlayer"].Checked;
            if (checks.ContainsKey("QualReadyPause")) qual.PlayerReadyPause = checks["QualReadyPause"].Checked;
            string v;
            HubFeed.Current.Enabled = !settings.TryGetValue("UseHub", out v) || v != "0";   // NR2003 Companions Hub (MODS\Hub)
            HubFeed.Current.Logger = Show;
            GamePause.KeyName = settings.TryGetValue("PauseKey", out v) && v != "" ? v : "Pause";
            art.ExtraFolder = settings.TryGetValue("ArtFolder", out v) && v != "" ? v : null;
            art.SeriesChoice = settings.TryGetValue("ArtSeries", out v) && v != "" ? v : null;
            art.UseCompanion = !settings.TryGetValue("ArtOverlayApp", out v) || v != "0";
            art.CompanionFolder = settings.TryGetValue("StageCompanionFolder", out v) && v != "" ? v : null;
            if (artLabel != null)
            {
                List<string> roots = art.Roots();
                artLabel.Text = roots.Count == 0 ? "Art: none found, numbers drawn" : "Art: " + string.Join(", ", roots.Select(r => art.IsCompanionAppFolder(r) ? "companion app art" : Path.GetFileName(Path.GetDirectoryName(Path.Combine(r, "x")).TrimEnd(Path.DirectorySeparatorChar))).ToArray());
                tips.SetToolTip(artLabel, string.Join("\n", roots.ToArray()));
            }
        }

        // ---- the player's qualifying turn: paused until ready ---------------------------------------------

        void ShowQualReady()
        {
            if (mem == null) return;
            CloseQualReady();
            if (!Paused) Show(Stamp + "your qualifying turn: NR2003 paused " + GamePause.Pause(mem) + " - set your camera, then resume");
            qualReadySeenPaused = Paused;
            qualReadyAt = DateTime.Now;
            qualReady = new QualReadyForm(OverlayForm.GameRect(mem.Process.MainWindowHandle), qual.PlayerCall, qual.DnqSeconds, () =>
            {
                if (Paused && mem != null) GamePause.Resume(mem);
                qual.PlayerResumed();
                CloseQualReady();
            });
            qualReady.Show();
        }

        void QualReadyTick()
        {
            if (qualReady == null) return;
            if (!qual.PlayerHolding) { CloseQualReady(); return; }             // DNQ / garage / session over
            bool paused = Paused;
            if (paused) qualReadySeenPaused = true;
            else if (qualReadySeenPaused)                                    // you resumed the game yourself
            {
                Show(Stamp + "your qualifying turn: game resumed - pull out within " + qual.DnqSeconds.ToString("0") + " s");
                qual.PlayerResumed();
                CloseQualReady();
            }
            else if ((DateTime.Now - qualReadyAt).TotalSeconds > 3 && !qualReadyWarned)
            {
                qualReadyWarned = true;                                      // never paused: the card waits for a click
                Show(Stamp + "the game did not pause for your turn (fallback pause key '" + GamePause.KeyName + "'; set PauseKey= in QualControl.ini) - click RESUME when ready");
            }
        }
        bool qualReadyWarned;

        void CloseQualReady()
        {
            if (qualReady != null && !qualReady.IsDisposed) qualReady.Close();
            qualReady = null;
            qualReadyWarned = false;
        }

        // ---- knockout rounds: one row per round, stored as "25:24, 10:12, 5:12" ----
        void RoundRowsFromText()
        {
            roundSync = true;
            var r = Qualifying.ParseRounds(qualRoundsBox.Text);
            for (int i = 0; i < 4; i++)
            {
                roundMins[i].Value = i < r.Count ? (decimal)Math.Max(0, Math.Min(180, r[i].Item1)) : 0;
                roundCars[i].Value = i < r.Count ? Math.Max(1, Math.Min(60, r[i].Item2)) : 12;
            }
            roundSync = false;
        }

        void RoundsTextFromRows()
        {
            var parts = new List<string>();
            for (int i = 0; i < 4; i++)
                if (roundMins[i].Value > 0) parts.Add(roundMins[i].Value + ":" + roundCars[i].Value);
            qualRoundsBox.Text = string.Join(", ", parts.ToArray());
        }

        // ---- saved results ----
        // the NR2003 folder (from the game's memory when attached, else the usual install folder)
        string Nr2003Dir()
        {
            try { if (mem != null) { string d = mem.Str(Addr.Nr2003Dir, 260).Trim(); if (d.Length > 3 && Directory.Exists(d)) return d; } }
            catch (Exception) { }
            const string def = @"C:\Papyrus\NASCAR Racing 2003 Season";
            return Directory.Exists(def) ? def : "";
        }

        void SaveResultsClick()
        {
            if (!qual.HasResults) { Show(Stamp + "saved results: no qualifying results to save yet"); return; }
            string track = qual.TrackName, type = qual.TypeKey;
            string name = string.Format("{0} {1} {2:yyyy-MM-dd HHmm}", track.Length > 0 ? track : "results", type, DateTime.Now);
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            Directory.CreateDirectory(Qualifying.ResultsDir);
            using (var d = new SaveFileDialog { InitialDirectory = Qualifying.ResultsDir, FileName = name + ".txt", Filter = "QualControl results (*.txt)|*.txt", Title = "Save the qualifying results" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string msg = qual.SaveResults(d.FileName);
                Show(Stamp + "saved results: " + msg);
                qualSavedLabel.Text = msg.StartsWith("saved") ? "Saved: " + Path.GetFileName(d.FileName) : msg;
            }
        }

        void ReapplyResultsClick()
        {
            if (mem == null) { Show(Stamp + "saved results: start NR2003 and load the weekend first"); return; }
            Directory.CreateDirectory(Qualifying.ResultsDir);
            using (var d = new OpenFileDialog { InitialDirectory = Qualifying.ResultsDir, Filter = "QualControl results (*.txt)|*.txt|All files (*.*)|*.*", Title = "Reapply saved qualifying results" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Qualifying.SavedResults f;
                try { f = Qualifying.ReadResults(d.FileName); }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't read that file: " + ex.Message, "QualControl", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                string here = qual.TrackName;
                if (here.Length > 0 && !string.Equals(here, f.Track, StringComparison.OrdinalIgnoreCase) &&
                    MessageBox.Show(this, "These results were saved at " + f.Track + ", but the weekend loaded is at " + here + ".\n\nReapply them anyway?", "QualControl",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                if (qual.Active && !qual.Reapplied &&
                    MessageBox.Show(this, "QualControl has qualifying going or results waiting for this weekend. Reapplying the saved results replaces them (any held cars are let go).\n\nReapply them?", "QualControl",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                string msg = qual.ReapplyResults(f);
                Show(Stamp + "saved results: " + msg);
                qualSavedLabel.Text = "Reapplied: " + Path.GetFileName(d.FileName) + " (" + f.Track + ", " + f.Cars.Count + " cars)";
                QualUi();
            }
        }

        // ---- running order: a drag-and-drop list, stored as car numbers ----
        void ShowOrderList()
        {
            if (orderList == null) return;
            int sel = orderList.SelectedIndex;
            orderList.BeginUpdate();
            orderList.Items.Clear();
            for (int i = 0; i < orderOrder.Count; i++)
            {
                string nm;
                orderList.Items.Add(string.Format("{0,2}.  #{1,-4} {2}", i + 1, orderOrder[i], orderNames.TryGetValue(orderOrder[i], out nm) ? nm : ""));
            }
            orderList.EndUpdate();
            if (sel >= 0 && sel < orderList.Items.Count) orderList.SelectedIndex = sel;
        }

        void OrderChanged()
        {
            qualOrderBox.Text = string.Join(" ", orderOrder.ToArray());     // saved with the other qualifying settings
            ShowOrderList();
        }

        void OrderMove(int dir)
        {
            int i = orderList.SelectedIndex, j = i + dir;
            if (i < 0 || j < 0 || j >= orderOrder.Count) return;
            string n = orderOrder[i]; orderOrder.RemoveAt(i); orderOrder.Insert(j, n);
            OrderChanged(); orderList.SelectedIndex = j;
        }

        void OrderGetField()
        {
            if (mem == null) { Show(Stamp + "running order: start NR2003 and load a session first"); return; }
            List<Tuple<int, string, string>> roster;
            try { roster = Qualifying.Roster(mem); }
            catch (Exception ex) { Show(Stamp + "running order: " + ex.Message); return; }
            if (roster.Count == 0) { Show(Stamp + "running order: no AI cars found - load a session (practice) first"); return; }
            foreach (var r in roster) orderNames[r.Item2] = r.Item3;
            var nums = roster.Select(r => r.Item2).ToList();
            var keep = orderOrder.Where(nums.Contains).ToList();                   // your order for cars still here
            keep.AddRange(nums.Where(n => !keep.Contains(n)).OrderBy(n => n.Length).ThenBy(n => n));
            orderOrder = keep;
            OrderChanged();
            Show(Stamp + "running order: " + orderOrder.Count + " cars");
        }

        // ---- qualifying timing box ----
        void PlaceQualBox()
        {
            if (qualStyleBox != null && qualStyleBox.SelectedIndex == 1) { PlaceBroadcast(); return; }
            if (qualPlacing != null && !qualPlacing.IsDisposed) { qualPlacing.Activate(); return; }
            if (qualBox != null) { qualBox.Finish(); qualBox = null; }               // only the copy you're moving is shown
            qualPlacing = new QualBoxForm(art, GameArea, qualBoxLayout, true);
            if (qual.LiveOn) qualPlacing.Data = QualBoxNow();
            qualPlacing.Saved += l =>
            {
                qualBoxLayout = l; settings["QualBox"] = l.ToString(); SaveSettings();
                Show(Stamp + "timing box placed, size " + Math.Round(l.Scale * 100) + "%");
            };
            qualPlacing.FormClosed += (s, e) => { qualPlacing = null; };            // the live box comes back in the saved spot
            qualPlacing.Show();
            qualPlacing.Activate();
        }

        // The broadcast style is placed with a copy of itself (same layout and size setting as the timing box).
        QualBroadcastForm broadPlacing;
        void PlaceBroadcast()
        {
            if (broadPlacing != null && !broadPlacing.IsDisposed) { broadPlacing.Activate(); return; }
            if (qualBroad != null) { qualBroad.Finish(); qualBroad = null; }
            broadPlacing = new QualBroadcastForm(art, GameArea, qualBoxLayout, true);
            if (qual.LiveOn && mem != null) broadPlacing.Data = BroadcastNow();
            broadPlacing.Saved += l =>
            {
                qualBoxLayout = l; settings["QualBox"] = l.ToString(); SaveSettings();
                Show(Stamp + "broadcast overlay placed, size " + Math.Round(l.Scale * 100) + "%");
            };
            broadPlacing.FormClosed += (s, e) => { broadPlacing = null; };
            broadPlacing.Show();
            broadPlacing.Activate();
        }

        void PlaceBanner()
        {
            if (bannerPlacing != null && !bannerPlacing.IsDisposed) { bannerPlacing.Activate(); return; }
            if (banner != null) { banner.Close(); banner = null; }                   // only the copy you're moving is shown
            bannerPlacing = new PlayerBannerForm(GameArea, bannerLayout, true);
            bannerPlacing.Saved += l =>
            {
                bannerLayout = l; settings["PlayerBanner"] = l.ToString(); SaveSettings();
                Show(Stamp + "player banner placed, size " + Math.Round(l.Scale * 100) + "%");
            };
            bannerPlacing.FormClosed += (s, e) => { bannerPlacing = null; };        // the live banner comes back in the saved spot
            bannerPlacing.Show();
            bannerPlacing.Activate();
        }

        void BannerTick()
        {
            string text = mem != null && checks.ContainsKey("ShowPlayerCalls") && checks["ShowPlayerCalls"].Checked ? qual.PlayerCall : "";
            int kind = qual.PlayerCallKind;
            string timer = qual.PlayerTimer; bool urgent = qual.PlayerTimerUrgent;
            if (banner != null && banner.IsDisposed) banner = null;
            if (bannerPlacing != null && !bannerPlacing.IsDisposed) text = "";
            if (string.IsNullOrEmpty(text)) { if (banner != null) { banner.Close(); banner = null; } return; }
            if (banner == null)
            {
                try { banner = new PlayerBannerForm(GameArea, bannerLayout); banner.Show(); }
                catch (Exception ex) { Show(Stamp + "player banner: " + ex.Message); banner = null; return; }
            }
            banner.Set(text, kind, timer, urgent);
        }

        void QualBoxTick()
        {
            BannerTick();
            if (qual.LiveOn && mem != null) TrackHeld(); else if (!qual.LiveOn) heldCar = -1;
            if (mem != null) try { LapFmt.TrackM = officialLen > 0 ? officialLen : mem.F64(Addr.TrackLength); } catch (GameMemoryException) { }
            bool placingNow = (qualPlacing != null && !qualPlacing.IsDisposed) || (broadPlacing != null && !broadPlacing.IsDisposed);
            if (qualPlacing != null && !qualPlacing.IsDisposed && qual.LiveOn) { qualPlacing.Data = QualBoxNow(); qualPlacing.Invalidate(); }
            // the broadcast copy being placed shows the live run too (it kept the sample driver - beta tester 2026-10-27)
            if (broadPlacing != null && !broadPlacing.IsDisposed && qual.LiveOn && mem != null && (DateTime.Now - broadAt).TotalMilliseconds >= 40)
            {
                broadAt = DateTime.Now;
                var pd = BroadcastNow();
                broadPlacing.Animate(pd.ShowOrder);
                broadPlacing.Data = pd; broadPlacing.Invalidate();
            }
            bool want = checks["ShowQualBox"].Checked && qual.LiveOn && mem != null && !placingNow;
            bool broadcast = qualStyleBox != null && qualStyleBox.SelectedIndex == 1;
            if (qualBox != null && (qualBox.IsDisposed || qualBox.Finished)) qualBox = null;
            if (qualBroad != null && (qualBroad.IsDisposed || qualBroad.Finished)) qualBroad = null;
            if (!want || !broadcast) { if (qualBroad != null) { qualBroad.Finish(); qualBroad = null; } }
            if (want && broadcast)
            {
                if (qualBox != null) { qualBox.Finish(); qualBox = null; }
                if (qualBroad == null)
                {
                    try { qualBroad = new QualBroadcastForm(art, GameArea, qualBoxLayout); qualBroad.Show(); }
                    catch (Exception ex) { Show(Stamp + "broadcast overlay: " + ex.Message); qualBroad = null; return; }
                }
                // about 25 frames a second, and a repaint only when something on it changed (a big layered window
                // redrawn 100 times a second was needless load on the game's graphics)
                if ((DateTime.Now - broadAt).TotalMilliseconds < 40) return;
                broadAt = DateTime.Now;
                var data = BroadcastNow();
                bool moving = qualBroad.Animate(data.ShowOrder);
                string key = data.Key();
                if (moving || key != broadKey) { qualBroad.Data = data; qualBroad.Invalidate(); broadKey = key; }
                return;
            }
            if (!want) { if (qualBox != null) { qualBox.Finish(); qualBox = null; } return; }
            if (qualBox == null)
            {
                try { qualBox = new QualBoxForm(art, GameArea, qualBoxLayout); qualBox.Show(); }
                catch (Exception ex) { Show(Stamp + "timing box: " + ex.Message); qualBox = null; return; }
            }
            qualBox.Data = QualBoxNow();
            qualBox.Invalidate();
        }

        // the car the broadcast overlay and the telemetry strip follow: the car on its run (single car runs; the car on
        // TV if that one is on a timed lap) or the car on TV (knockout)
        int BoxCar()
        {
            int tv = QualTiming.Focus(mem);
            int car = qual.SingleRuns ? qual.BoxCar : tv;
            if (qual.SingleRuns && qual.BoxKind != Qualifying.BoxShow.Result && QCar(tv) && tv != car && qual.OnTimedLap(tv)) car = tv;
            return car;
        }

        // The broadcast overlay's data: the same car the classic box follows (single car runs) or the car on TV (knockout).
        // Telemetry: speed from the car block's velocity, rpm from the car record (0xC12030 + slot * 5560, +0x1508), the
        // throttle pedal and gear from the block's driver inputs (+6398, +6390 minus 1), and the brake worked out from the
        // car's slowing (the telemetry recorder, Telemetry.cs - the AI have no brake value of their own).
        QualBroadcastData BroadcastNow()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Func<int, string> time = LapFmt.Lap;                       // lap times: m:ss.fff, or mph (Lap times as)
            var d = new QualBroadcastData { LapsTotal = qual.Laps };
            string tn = mem.Str(Addr.TrackName, 64).Trim();
            d.Track = (tn.Contains('_') ? tn.Substring(0, tn.IndexOf('_')) : tn).ToUpperInvariant();
            int car = BoxCar();
            var b = qual.LiveBoard;
            int pole = b.Count > 0 ? b[0].Item1 : -1, poleMs = b.Count > 0 ? b[0].Item2 : 0;
            // the running order, as QualControl has it: ranked as cars finish (pole first), the car on its run (colour
            // coded), then the cars still to go in the qualifying order. Ten rows: if the live car would be below them,
            // the top seven, the live car and the next two.
            var view = qual.SingleRuns ? qual.OrderView() : b.Select(e => Tuple.Create(e.Item1, e.Item2, 'f')).ToList();
            int liveAt = view.FindIndex(v => v.Item3 == 'l');
            var rows = view.Take(10).ToList();
            if (liveAt >= 10) rows = view.Take(7).Concat(view.Skip(liveAt).Take(3)).ToList();
            int rank = 0;
            foreach (var v in view)
            {
                if (v.Item3 == 'f') rank++;
                if (!rows.Contains(v)) continue;
                int el = v.Item3 == 'l' ? qual.Timing.Elapsed(v.Item1) : -1;
                d.Order.Add(new[] { v.Item3 == 'f' ? rank.ToString() : v.Item3 == 'l' ? "" : "", qual.FirstName(v.Item1), qual.LastName(v.Item1),
                    v.Item3 == 'f' ? (v.Item2 > 0 ? time(v.Item2) : "NO TIME") : v.Item3 == 'l' ? (el >= 0 ? LapFmt.Time(el) : "OUT LAP") : "", v.Item3.ToString(),
                    Qualifying.Label(qual.CarNumber(v.Item1)) });          // [5] the painted number (the box doesn't draw it; the overlay feed does)
            }
            // the running order slides in once a lap (6 s after the car on its run crosses the line) and stays up
            // between runs
            bool live = qual.SingleRuns ? qual.BoxKind == Qualifying.BoxShow.Live : QCar(car);
            var lt = QCar(car) ? qual.Timing.Of(car) : null;
            if (lt != null && (car != orderCar || lt.LapStart != orderLapStart))
            {
                if (car == orderCar && lt.LapStart >= 0) orderUntil = DateTime.Now.AddSeconds(6);
                orderCar = car; orderLapStart = lt.LapStart;
            }
            d.ShowOrder = !live || DateTime.Now < orderUntil;
            if (!QCar(car)) return d;
            d.Num = qual.CarNumber(car); d.First = qual.FirstName(car); d.Last = qual.LastName(car);
            // telemetry: speed from the car's own velocity (the record's speed field read ~30 % high)
            long rec = 0xC12030 + car * 5560L, blk = 0xACCA30 + car * (long)Addr.Ocd;
            double vx = mem.F32(blk + 6634), vy = mem.F32(blk + 6638);
            d.Mph = (float)(Math.Sqrt(vx * vx + vy * vy) * 2.2369363);
            d.Rpm = mem.F32(rec + 0x1508);
            // throttle: the pedal in the car block's driver inputs (+6390 gear, +6394 brake slot, +6398 throttle, +6402
            // rpm). Mapped live 2026-11-13 at Indy and Coronado: 1.0 flat out, 0.41 on the pit road limiter, 0 under
            // braking. (The old source, record +0x604, is the top byte of a bit-packed state word with 3 bits of a frame
            // counter in it; +0x605 after it is steering, not brake.)
            float thr = mem.F32(blk + 6398);
            d.Throttle = float.IsNaN(thr) ? 0 : Math.Max(0, Math.Min(1, thr));
            var tc = tele.Of(car);
            if (tc != null && tc.HaveNow) d.Throttle = tc.Now.Thr;        // the recorder's (lightly smoothed, Telemetry.cs)
            d.Brake = tc != null && tc.HaveNow ? tc.Now.Brk : -1;          // from the telemetry recorder (Telemetry.cs)
            int gb = mem.U8(blk + 6390);
            d.Gear = gb <= 10 ? gb - 1 : 0;
            d.Tele = d.Mph >= 0 && d.Mph < 400 && d.Rpm >= 0 && d.Rpm < 30000;
            float top; rpmTop.TryGetValue(car, out top);
            if (d.Tele && d.Rpm > top) rpmTop[car] = top = d.Rpm;
            d.RpmTop = Math.Max(6000, top);
            // laps: the run's own, else what the timing has
            int lapNo; List<int> laps;
            var t = qual.Timing.Of(car);
            if (qual.RunView(car, out lapNo, out laps))
            {
                d.LapNo = lapNo;
                foreach (int ms in laps) d.LapRows.Add(time(ms));
                if (laps.Count > 0) { d.LastLap = time(laps[laps.Count - 1]); d.BestLap = time(laps.Min()); }
            }
            else if (t != null)
            {
                d.LapNo = 1; d.LapsTotal = 0;
                if (t.LastLapMs > 0) d.LastLap = time(t.LastLapMs);
                if (t.BestMs > 0) d.BestLap = time(t.BestMs);
            }
            int run = t != null ? qual.Timing.Elapsed(car) : -1;
            d.Current = run >= 0 ? LapFmt.Time(run) : "-.---";
            // the accent: lime on pole pace, red when slower
            int delta;
            if (qual.SingleRuns && qual.BoxKind == Qualifying.BoxShow.Result) d.Ahead = !qual.ResultHasDiff || qual.ResultDiffMs <= 0;
            else if (pole >= 0 && pole != car && qual.Timing.Delta(car, pole, poleMs, out delta)) d.Ahead = delta <= 0;
            return d;
        }

        // The last timed lap anyone finished: it stays in the box until the next car starts its first timed lap.
        int heldCar = -1, heldLapMs, heldDiffMs, heldPos;
        DateTime heldStamp = DateTime.MinValue;
        void TrackHeld()
        {
            foreach (var kv in qual.Timing.All)
            {
                var t = kv.Value;
                if (t.LastLapMs <= 0 || t.LastLapAt <= heldStamp) continue;
                heldStamp = t.LastLapAt; heldCar = kv.Key; heldLapMs = t.LastLapMs;
                var others = qual.LiveBoard.Where(x => x.Item1 != kv.Key).Select(x => x.Item2).ToList();
                heldDiffMs = others.Count > 0 ? heldLapMs - others.Min() : 0;         // against the best of the others
                heldPos = 1 + others.Count(ms => ms < heldLapMs);
            }
        }

        // once a second while qualifying is live: what the timing box sees (qual_timing.log next to QualControl.exe)
        DateTime qualDbgAt = DateTime.MinValue;
        void QualTimingDebug(int car, string kind)
        {
            if ((DateTime.Now - qualDbgAt).TotalSeconds < 1) return;
            qualDbgAt = DateTime.Now;
            try
            {
                var t = QCar(car) ? qual.Timing.Of(car) : null;
                string line = DateTime.Now.ToString("HH:mm:ss") + " car " + car + (kind != "" ? " box " + kind : "") + (t == null ? " (not tracked)" :
                    " state 0x" + t.State.ToString("x") + " lapStart " + t.LapStart + " now " + qual.Timing.NowMs + " run " + qual.Timing.Elapsed(car) +
                    " path " + t.Path.ToString("0") + " lastCross " + t.LastCross + " crossings " + t.NextIdx + " trackLen " + qual.Timing.TrackLen.ToString("0") +
                    " board " + qual.LiveBoard.Count) + Environment.NewLine;
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qual_timing.log"), line);
            }
            catch (Exception) { }
        }

        // Single car runs: Qualifying picks the car (the same one it names to broadcast tools in the state file), whatever
        // the camera is on - unless you put the camera on another car that is on a timed lap yourself.
        //   Result:  the run just done, during its in-lap - its best lap, the gap to pole and its place, held a few seconds
        //   Live:    the car on a timed lap - the live gap to pole (each timed lap), the lap just done for 4 s between laps
        //   Waiting: the next car on its way to the green - "-.--" until it crosses the line
        QualBoxData SingleBox(QualBoxData d, int tv, int pole, int poleMs)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Func<int, string> time = LapFmt.Lap;
            Func<int, string> gap = ms => (ms / 1000.0).ToString("+0.000;-0.000;0.000", inv);
            int car = qual.BoxCar;
            var kind = qual.BoxKind;
            if (kind != Qualifying.BoxShow.Result && QCar(tv) && tv != car && qual.OnTimedLap(tv)) { car = tv; kind = Qualifying.BoxShow.Live; }
            QualTimingDebug(car, kind.ToString());
            if (!QCar(car) || kind == Qualifying.BoxShow.None) return d;
            d.CarNum = qual.CarNumber(car); d.CarName = qual.LastName(car); d.CarPos = "--";
            int delta;
            if (kind == Qualifying.BoxShow.Result)
            {
                d.CarPos = qual.ResultPos + ".";
                if (qual.ResultHasDiff) { d.Projected = time(qual.ResultBestMs); d.Right = gap(qual.ResultDiffMs); d.Mode = 2; d.Faster = qual.ResultDiffMs <= 0; }
                else { d.Right = time(qual.ResultBestMs); d.Mode = 1; }                 // the first time posted: nothing to compare yet
            }
            else if (kind == Qualifying.BoxShow.Live && car == qual.BoxCar && qual.BoxLapMs > 0 && qual.BoxLapAge < 4)
            {
                if (pole >= 0) { d.Projected = time(qual.BoxLapMs); d.Right = gap(qual.BoxLapMs - poleMs); d.Mode = 2; d.Faster = qual.BoxLapMs <= poleMs; }
                else { d.Right = time(qual.BoxLapMs); d.Mode = 1; }
            }
            else if (kind == Qualifying.BoxShow.Live && pole >= 0 && qual.Timing.Delta(car, pole, poleMs, out delta))
            {
                d.Right = gap(delta); d.Mode = 2; d.Faster = delta <= 0;
            }
            else { d.Right = "-.--"; d.Mode = 0; }
            return d;
        }

        QualBoxData QualBoxNow()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var d = new QualBoxData { Header = qual.LiveHeader };
            var b = qual.LiveBoard;
            int pole = b.Count > 0 ? b[0].Item1 : -1, poleMs = b.Count > 0 ? b[0].Item2 : 0;
            if (pole >= 0) { d.PoleNum = qual.CarNumber(pole); d.PoleName = qual.LastName(pole); d.PoleTime = LapFmt.Lap(poleMs); }
            int tv = QualTiming.Focus(mem);
            if (qual.SingleRuns) return SingleBox(d, tv, pole, poleMs);
            // knockout: the car on TV
            int car = tv;
            QualTimingDebug(car, "");
            var t = QCar(car) ? qual.Timing.Of(car) : null;
            int delta, run = t != null ? qual.Timing.Elapsed(car) : -1;
            bool justCrossed = heldCar == car && (DateTime.Now - heldStamp).TotalSeconds < 4;   // the exact lap, for a moment
            double resultAge = (DateTime.Now - heldStamp).TotalSeconds;
            bool completedRun = QCar(heldCar) && !qual.ActiveRunners.Contains(heldCar);
            bool nextFocusedBeforeGreen = qual.SingleRuns && QCar(car) && car != heldCar && tv == car &&
                qual.ActiveRunners.Contains(car) && run < 0;
            // Keep the completed result up for four seconds, and keep it until RaceVision
            // has focused the next runner near the stripe before that runner's green lap.
            if (completedRun && (resultAge < 4 || (run < 0 && !nextFocusedBeforeGreen)))
            {
                d.CarNum = qual.CarNumber(heldCar); d.CarName = qual.LastName(heldCar); d.CarPos = heldPos + ".";
                d.Projected = LapFmt.Lap(heldLapMs);
                d.Right = (heldDiffMs / 1000.0).ToString("+0.000;-0.000;0.000", inv); d.Mode = 2; d.Faster = heldDiffMs <= 0;
                return d;
            }
            if (run >= 0 && !justCrossed)
            {
                // on a timed lap: the running lap time, live, and the gap to pole at this point of the lap
                d.CarNum = qual.CarNumber(car); d.CarName = qual.LastName(car); d.CarPos = "--";
                d.Projected = "";                                                 // no count-up: just the live gap
                if (pole >= 0 && qual.Timing.Delta(car, pole, poleMs, out delta))
                {
                    d.Right = (delta / 1000.0).ToString("+0.000;-0.000;0.000", inv); d.Mode = 2; d.Faster = delta <= 0;
                }
                else { d.Right = ""; d.Mode = 0; }
                return d;
            }
            if (nextFocusedBeforeGreen)
            {
                d.CarNum = qual.CarNumber(car); d.CarName = qual.LastName(car); d.CarPos = "--";
                d.Projected = ""; d.Right = ""; d.Mode = 0;
                return d;
            }
            bool hold = QCar(heldCar) && (qual.SingleRuns || heldCar == car);
            if (hold)
            {
                // the last lap done stays up through the next car's out lap
                d.CarNum = qual.CarNumber(heldCar); d.CarName = qual.LastName(heldCar); d.CarPos = heldPos + ".";
                d.Projected = LapFmt.Lap(heldLapMs);
                d.Right = (heldDiffMs / 1000.0).ToString("+0.000;-0.000;0.000", inv); d.Mode = 2; d.Faster = heldDiffMs <= 0;
                return d;
            }
            if (t != null)
            {
                d.CarNum = qual.CarNumber(car); d.CarName = qual.LastName(car);
                d.Right = QualTiming.InLap.Contains(t.State) ? "IN LAP" : "OUT LAP"; d.Mode = 0;
            }
            return d;
        }

        Rectangle GameArea { get { return mem != null ? OverlayForm.GameRect(mem.Process.MainWindowHandle) : Screen.PrimaryScreen.Bounds; } }

        void ApplyTheme()
        {
            var mode = themeBox.SelectedIndex == 2 ? Theme.Mode.Dark : themeBox.SelectedIndex == 1 ? Theme.Mode.Light : Theme.Mode.System;
            Theme.Apply(this, Theme.IsDark(mode));
            // the name: the logo's amber and red, a shade deeper on the light look so it reads on grey
            brandQual.ForeColor = Theme.Dark ? Theme.Amber : Color.FromArgb(214, 128, 0);
            brandControl.ForeColor = Theme.Dark ? Color.FromArgb(214, 30, 24) : Theme.Red;
        }

        // ---- log and attaching ----------------------------------------------------------------------

        void Show(string line)
        {
            try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
            logBox.Items.Add(line);
            while (logBox.Items.Count > 1000) logBox.Items.RemoveAt(0);
            logBox.TopIndex = Math.Max(0, logBox.Items.Count - 1);
        }

        static string Stamp { get { return DateTime.Now.ToString("HH:mm:ss") + " "; } }

        void SlowTick()
        {
            if (mem != null && !mem.Alive)
            {
                tick.Stop();
                uint code = 0; bool known = false;
                try { code = unchecked((uint)mem.Process.ExitCode); known = true; } catch (Exception) { }
                mem.Dispose();
                mem = null;
                track = null;
                Show(Stamp + (known && code >= 0xC0000000 ? string.Format("NR2003 CRASHED (exception 0x{0:X8})", code)
                    : "NR2003 closed" + (known ? " (exit code " + code + ")" : "")));
            }
            if (mem == null)
            {
                try { mem = ProcessMemory.Attach(); attachError = null; }
                catch (GameMemoryException ex)
                {
                    if (attachError != ex.Message) Show(Stamp + ex.Message);
                    attachError = ex.Message;
                }
                if (mem != null)
                {
                    ApplyOptions();
                    Show(Stamp + "attached to NR2003 (process " + mem.Process.Id + ")");
                    tick.Start();
                }
            }
            string tn = null;
            if (mem != null) try { tn = mem.Str(Addr.TrackName, 64).Trim(); } catch (GameMemoryException) { }
            if (!string.IsNullOrEmpty(tn) && tn != track)
            {
                track = tn;
                officialLen = TrackOfficialLength(track);
                Show(Stamp + "track: " + track);
            }
            status.Text = mem == null ? (attachError != null ? "Cannot attach to NR2003 - run as administrator" : "Waiting for NR2003...")
                : "Attached to NR2003" + (string.IsNullOrEmpty(track) ? "" : " - " + track);
        }

        double officialLen;      // the track's official length (track.ini), for lap speeds; 0 = not known

        double TrackOfficialLength(string name)
        {
            try
            {
                string exe = mem != null ? mem.Process.MainModule.FileName : null;
                return exe == null ? 0 : LapFmt.Official(Path.Combine(Path.Combine(Path.Combine(Path.GetDirectoryName(exe), "tracks"), name), "track.ini"));
            }
            catch (Exception) { return 0; }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
