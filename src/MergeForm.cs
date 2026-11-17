using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace QualControl
{
    // The merge window: pick saved results files (sessions), see the merged order and the race field (the cut), save it
    // as a results file that "Reapply saved results..." writes in the race weekend's qualifying session, and write the
    // race roster (.lst): a copy of a roster with only the + / - marks changed.
    public class MergeForm : Form
    {
        readonly List<Qualifying.SavedResults> sessions = new List<Qualifying.SavedResults>();
        readonly ListBox sessionList, mergedList;
        readonly Label summary, savedLabel, fieldLabel;
        readonly Button saveBtn, rosterBtn;
        readonly NumericUpDown fieldBox;
        readonly CheckBox playerInBox;
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 30000 };
        List<ResultsMerge.Entry> merged = new List<ResultsMerge.Entry>();
        HashSet<ResultsMerge.Entry> field = new HashSet<ResultsMerge.Entry>();
        readonly Action<string> log;
        readonly Func<string> nr2003Dir;
        bool building = true;

        public int FieldCars { get { return (int)fieldBox.Value; } }
        public bool PlayerAlwaysIn { get { return playerInBox.Checked; } }

        public MergeForm(Action<string> log, Func<string> nr2003Dir = null, int fieldCars = 43, bool playerAlwaysIn = true)
        {
            this.log = log;
            this.nr2003Dir = nr2003Dir;
            Text = "QualControl - merge saved results";
            ClientSize = new Size(720, 680);
            MinimumSize = new Size(620, 560);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            ShowIcon = false;

            var g1 = new ThemedGroupBox { Text = "Sessions (in this order: the first wins a tie)", Left = 10, Top = 8, Width = 700, Height = 150, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(g1);
            sessionList = new ListBox { Left = 10, Top = 24, Width = 560, Height = 116, IntegralHeight = false, Font = new Font("Consolas", 9f), HorizontalScrollbar = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            tips.SetToolTip(sessionList, "The saved results to merge, one per qualifying session. The order counts: when two drivers have the same\n" +
                "time (and round), the one from the session higher in this list is ahead.");
            g1.Controls.Add(sessionList);
            Func<string, int, string, Button> sb = (t, top, tip) =>
            {
                var b = new Button { Text = t, Left = 580, Top = top, Width = 110, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right };
                tips.SetToolTip(b, tip); g1.Controls.Add(b); return b;
            };
            sb("Add...", 22, "Adds saved results files (you can pick several at once).").Click += (s, e) => AddFiles();
            sb("Remove", 52, "Takes the selected session out of the merge.").Click += (s, e) => { int i = sessionList.SelectedIndex; if (i >= 0) { sessions.RemoveAt(i); Refresh2(Math.Min(i, sessions.Count - 1)); } };
            sb("Up", 82, "Moves the selected session up (earlier: it wins ties).").Click += (s, e) => MoveSession(-1);
            sb("Down", 112, "Moves the selected session down.").Click += (s, e) => MoveSession(1);

            var g2 = new ThemedGroupBox { Text = "Merged order", Left = 10, Top = 166, Width = 700, Height = 370, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(g2);
            summary = new Label { Left = 10, Top = 22, Width = 680, Height = 34, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = "Add two or more saved results files." };
            g2.Controls.Add(summary);
            mergedList = new ListBox { Left = 10, Top = 58, Width = 680, Height = 302, IntegralHeight = false, Font = new Font("Consolas", 9f), HorizontalScrollbar = true, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            tips.SetToolTip(mergedList, "Ranked by the round reached (knockout / Indy road), then the time. A driver in more than one session keeps\n" +
                "the best (\"also S2\" = it ran in session 2 too). S1, S2 ... = the session the time is from. * = written 1 ms behind\n" +
                "the car ahead so the merged order holds in the game (a later round's car slower than an earlier round's, or a tie).\n" +
                "DNQ = outside the race field.");
            g2.Controls.Add(mergedList);

            var g3 = new ThemedGroupBox { Text = "Race field", Left = 10, Top = 544, Width = 700, Height = 80, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(g3);
            g3.Controls.Add(new Label { Left = 10, Top = 25, Width = 200, Text = "Cars in the race (your car included):" });
            fieldBox = new NumericUpDown { Left = 212, Top = 21, Width = 56, Minimum = 1, Maximum = 46, Value = Math.Max(1, Math.Min(46, fieldCars)) };
            tips.SetToolTip(fieldBox, "How many cars make the race from the merged order. Stock NR2003 races 43 cars; a patched exe can run up to 46.\n" +
                "Your own car counts as one of them when you make it.");
            fieldBox.ValueChanged += (s, e) => { if (!building) Refresh2(sessionList.SelectedIndex); };
            g3.Controls.Add(fieldBox);
            playerInBox = new CheckBox { Left = 290, Top = 23, AutoSize = true, Text = "My car always makes the field", Checked = playerAlwaysIn };
            tips.SetToolTip(playerInBox, "On: your car is in the race from any place (the last AI car inside the cut drops out).\n" +
                "Off: you can fail to qualify like anyone else (you are told; your car stays in the game, so don't race it).");
            playerInBox.CheckedChanged += (s, e) => { if (!building) Refresh2(sessionList.SelectedIndex); };
            g3.Controls.Add(playerInBox);
            fieldLabel = new Label { Left = 10, Top = 52, Width = 680, Height = 20, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            g3.Controls.Add(fieldLabel);

            saveBtn = new Button { Text = "Save merged results...", Left = 10, Top = 636, Width = 170, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Enabled = false };
            tips.SetToolTip(saveBtn, "Saves the merged order (with the field cut) as a results file. Load the race weekend and use 'Reapply saved\n" +
                "results...' on the Qualifying tab: its qualifying session then writes these times exactly. Cars are matched by .car file.");
            saveBtn.Click += (s, e) => SaveMerged();
            Controls.Add(saveBtn);
            rosterBtn = new Button { Text = "Write race roster (.lst)...", Left = 186, Top = 636, Width = 180, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Enabled = false };
            tips.SetToolTip(rosterBtn, "Writes a NEW roster for the race weekend: pick the roster to copy (the series' list your sessions used),\n" +
                "then name the new file. Every line is kept as it is, only the marks change: + for the AI cars in the race field,\n" +
                "- for every other car. A field car the roster doesn't list is added at the end. Your own car is never in a roster.");
            rosterBtn.Click += (s, e) => WriteRoster();
            Controls.Add(rosterBtn);
            savedLabel = new Label { Left = 374, Top = 643, Width = 240, Height = 20, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, AutoEllipsis = true };
            Controls.Add(savedLabel);
            var close = new Button { Text = "Close", Left = 620, Top = 636, Width = 90, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            close.Click += (s, e) => Close();
            Controls.Add(close);
            CancelButton = close;
            Theme.Apply(this, Theme.Dark);
            building = false;
            Refresh2(-1);
        }

        void AddFiles()
        {
            Directory.CreateDirectory(Qualifying.ResultsDir);
            using (var d = new OpenFileDialog { InitialDirectory = Qualifying.ResultsDir, Filter = "QualControl results (*.txt)|*.txt|All files (*.*)|*.*", Multiselect = true, Title = "Add saved results to merge" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var bad = new List<string>();
                var added = new List<Qualifying.SavedResults>();
                foreach (string p in d.FileNames)
                {
                    if (sessions.Any(x => string.Equals(Path.GetFullPath(x.Path), Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase))) continue;
                    try { added.Add(Qualifying.ReadResults(p)); }
                    catch (Exception ex) { bad.Add(Path.GetFileName(p) + ": " + ex.Message); }
                }
                sessions.AddRange(added.OrderBy(x => x.Saved));          // several at once: oldest first
                if (bad.Count > 0) MessageBox.Show(this, "Not added:\n" + string.Join("\n", bad.ToArray()), "QualControl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Refresh2(sessions.Count - 1);
            }
        }

        void MoveSession(int dir)
        {
            int i = sessionList.SelectedIndex, j = i + dir;
            if (i < 0 || j < 0 || j >= sessions.Count) return;
            var x = sessions[i]; sessions.RemoveAt(i); sessions.Insert(j, x);
            Refresh2(j);
        }

        void Refresh2(int select)
        {
            sessionList.BeginUpdate();
            sessionList.Items.Clear();
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                sessionList.Items.Add(string.Format("S{0}  {1}  -  {2}, {3}, {4} cars{5}", i + 1, Path.GetFileName(s.Path), s.Track, s.Type, s.Cars.Count,
                    s.Saved > DateTime.MinValue ? ", saved " + s.Saved.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : ""));
            }
            sessionList.EndUpdate();
            if (select >= 0 && select < sessionList.Items.Count) sessionList.SelectedIndex = select;

            merged = sessions.Count > 0 ? ResultsMerge.Merge(sessions) : new List<ResultsMerge.Entry>();
            field = ResultsMerge.Field(merged, FieldCars, PlayerAlwaysIn);
            bool rounds = merged.Any(e => e.Car.Round > 1);
            mergedList.BeginUpdate();
            mergedList.Items.Clear();
            for (int i = 0; i < merged.Count; i++)
            {
                if (i == FieldCars && merged.Count > FieldCars) mergedList.Items.Add("     ---- the cut: " + FieldCars + " cars ----");
                mergedList.Items.Add(ResultsMerge.Line(merged[i], i + 1, sessions, rounds) + (field.Contains(merged[i]) ? (i >= FieldCars ? "  IN (your car)" : "") : "  DNQ"));
            }
            mergedList.EndUpdate();
            int total = sessions.Sum(s => s.Cars.Count), dups = merged.Count(e => e.Others.Count > 0);
            var types = sessions.Select(s => s.Type).Distinct().ToList();
            var notes = new List<string>();
            if (types.Count > 1) notes.Add("the sessions are of different qualifying types (" + string.Join(", ", types.ToArray()) + ")");
            if (sessions.Select(s => s.Track).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) notes.Add("the sessions are from different tracks");
            if (sessions.Any(s => s.Cars.Any(c => c.File.Length == 0 && !c.IsPlayer))) notes.Add("some cars have no .car file (saved before 0.4.0): matched by number + name, and they can't go in a roster");
            summary.Text = sessions.Count == 0 ? "Add two or more saved results files."
                : string.Format("{0} session(s), {1} times, {2} drivers{3}{4}.", sessions.Count, total, merged.Count,
                    dups > 0 ? " (" + dups + " in more than one session: best kept)" : "", merged.Any(e => e.Raised) ? "; * = written 1 ms behind the car ahead" : "")
                  + (notes.Count > 0 ? "\nNote: " + string.Join("; ", notes.ToArray()) + "." : "");
            var me = merged.FirstOrDefault(e => e.Car.IsPlayer);
            int ai = field.Count(e => !e.Car.IsPlayer);
            fieldLabel.Text = merged.Count == 0 ? "" : string.Format("Race field: {0} AI cars{1}; {2} did not qualify.", ai,
                me == null ? " (your car is in none of the sessions)" : field.Contains(me) ? " + your car (P" + (merged.IndexOf(me) + 1) + ")" : " - YOUR CAR DID NOT QUALIFY (P" + (merged.IndexOf(me) + 1) + ")",
                merged.Count - field.Count);
            saveBtn.Enabled = rosterBtn.Enabled = merged.Count > 0;
        }

        void SaveMerged()
        {
            if (merged.Count == 0) return;
            var r = ResultsMerge.ToResults(sessions, merged, field);
            string name = string.Format("{0} merged {1:yyyy-MM-dd HHmm}", sessions[0].Track.Length > 0 ? sessions[0].Track : "results", DateTime.Now);
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            using (var d = new SaveFileDialog { InitialDirectory = Qualifying.ResultsDir, FileName = name + ".txt", Filter = "QualControl results (*.txt)|*.txt", Title = "Save the merged results" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Qualifying.WriteResultsFile(r, d.FileName);
                    savedLabel.Text = "Saved: " + Path.GetFileName(d.FileName);
                    if (log != null)
                    {
                        log("merged " + sessions.Count + " sessions (" + string.Join(", ", sessions.Select(s => Path.GetFileName(s.Path)).ToArray()) + ") into " + Path.GetFileName(d.FileName) + ": " +
                            merged.Count + " drivers, field " + field.Count + " (" + fieldLabel.Text + ")");
                        bool rounds = merged.Any(e => e.Car.Round > 1);
                        for (int i = 0; i < merged.Count; i++) log("    " + ResultsMerge.Line(merged[i], i + 1, sessions, rounds) + (field.Contains(merged[i]) ? "" : "  DNQ"));
                    }
                }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't save: " + ex.Message, "QualControl", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        static readonly Encoding Ansi = Encoding.GetEncoding(1252);     // roster lines are file names: read and written byte for byte

        void WriteRoster()
        {
            if (merged.Count == 0) return;
            var ai = merged.Where(e => field.Contains(e) && !e.Car.IsPlayer).ToList();
            var noFile = ai.Where(e => e.Car.File.Length == 0).ToList();
            if (noFile.Count > 0 && MessageBox.Show(this, noFile.Count + " car(s) in the field have no .car file in their saved results (saved before 0.4.0), so they can't be marked " +
                    "in a roster:\n" + string.Join(", ", noFile.Select(e => "#" + e.Car.Num + " " + e.Car.Name).ToArray()) + "\n\nWrite the roster without them?", "QualControl",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            string series = sessions.Select(s => s.Series).FirstOrDefault(s => s.Length > 0) ?? "";
            string dir = nr2003Dir != null ? nr2003Dir() : "";
            string cars = dir.Length > 0 && series.Length > 0 ? Path.Combine(Path.Combine(Path.Combine(dir, "series"), series), "cars") : "";
            string basePath;
            using (var d = new OpenFileDialog { Filter = "NR2003 rosters (*.lst)|*.lst", Title = "Pick the roster to copy (the series' list your sessions used)" })
            {
                if (Directory.Exists(cars)) d.InitialDirectory = cars;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                basePath = d.FileName;
            }
            string text;
            try { text = File.ReadAllText(basePath, Ansi); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't read the roster: " + ex.Message, "QualControl", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            List<string> added; int marked;
            string outText = ResultsMerge.Roster(text, ai.Select(e => e.Car.File), out added, out marked);
            string folder = Path.GetDirectoryName(basePath);
            // the cars added at the end must exist next to the roster, or NR2003 can't load them
            var missingFiles = added.Where(a => !File.Exists(Path.Combine(folder, a))).ToList();
            if (missingFiles.Count > 0 && MessageBox.Show(this, missingFiles.Count + " field car(s) are not in this roster's cars folder:\n" + string.Join("\n", missingFiles.Take(12).ToArray()) +
                    (missingFiles.Count > 12 ? "\n..." : "") + "\n\nWrong series folder? Write the roster anyway?", "QualControl", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            string outPath;
            using (var d = new SaveFileDialog { InitialDirectory = folder, Filter = "NR2003 rosters (*.lst)|*.lst", Title = "Name the NEW race roster", OverwritePrompt = true,
                FileName = Path.GetFileNameWithoutExtension(basePath) + " race.lst" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                outPath = d.FileName;
            }
            if (string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(basePath), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "That is the roster being copied. Give the new roster its own name.", "QualControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { File.WriteAllText(outPath, outText, Ansi); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't write the roster: " + ex.Message, "QualControl", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            int aiIn = marked + added.Count;
            savedLabel.Text = "Roster: " + Path.GetFileName(outPath);
            string msg = string.Format("Wrote {0}: a copy of {1} with {2} AI cars marked + (the race field) and every other line -.{3}\n\n" +
                "In NR2003 pick this roster for the race weekend and set the number of opponents to at least {2}. Then reapply the merged results " +
                "in its qualifying session (Save merged results... first if you haven't).", Path.GetFileName(outPath), Path.GetFileName(basePath), aiIn,
                added.Count > 0 ? "\n\nAdded at the end (not in that roster): " + string.Join(", ", added.ToArray()) : "");
            if (log != null) log("race roster written: " + outPath + " (copy of " + Path.GetFileName(basePath) + ", " + aiIn + " AI cars +" + (added.Count > 0 ? ", added: " + string.Join(" ", added.ToArray()) : "") + ")");
            MessageBox.Show(this, msg, "QualControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
