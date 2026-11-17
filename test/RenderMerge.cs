// Offscreen render of the merge window with two made-up sessions (light and dark). Build into a TEMP folder with all
// src files + this (/main:RenderMerge); run: RenderMerge.exe <outdir>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using QualControl;

static class RenderMerge
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        var rnd = new Random(3);
        Func<string, int, int, Qualifying.SavedResults> make = (name, first, n) =>
        {
            var r = new Qualifying.SavedResults { Track = "daytona", Type = "single", Path = name, Series = "cws2015", Saved = DateTime.Now.AddHours(-first) };
            for (int i = 0; i < n; i++)
            {
                int t = 47000 + rnd.Next(1500);
                r.Cars.Add(new Qualifying.SavedCar { Pos = i + 1, Num = (first + i).ToString(), Name = "Driver Number" + (first + i), File = "car" + (first + i) + ".car", Time = i == n - 1 ? (int?)null : t, Laps = new List<int> { t }, Round = 1 });
            }
            r.Cars[3].File = "car" + (first + 3) + ".car"; if (first == 1) r.Cars[30].IsPlayer = true;
            return r;
        };
        foreach (bool dark in new[] { false, true })
        {
            Theme.Apply(new Form(), dark);
            var f = new MergeForm(null) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ShowInTaskbar = false };
            var list = (List<Qualifying.SavedResults>)typeof(MergeForm).GetField("sessions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f);
            list.Add(make("81_R02_Qual-1 results.txt", 1, 40));
            list.Add(make("81_R02_Qual-2 results.txt", 38, 42));
            f.Show(); Application.DoEvents();
            typeof(MergeForm).GetMethod("Refresh2", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, new object[] { 0 });
            Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(Path.Combine(outDir, "merge_" + (dark ? "dark" : "light") + ".png")); }
            f.Close();
        }
    }
}
