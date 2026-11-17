// Offscreen render of every QualControl tab, light and dark, with each qualifying type open (the game must be closed:
// the window is built without a message loop so its timers never run, but keep it that way).
// Build: csc /target:exe /platform:x86 /main:RenderTabs (all RaceControl src files, as build.cmd) test\RenderTabs.cs into a TEMP folder
// (the window writes RaceControl.ini next to the exe); run: RenderTabs.exe <outdir>
using System;
using System.Linq;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class RenderTabs
{
    static object Get(object o, string name) { return typeof(QualControl.MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        var f = new QualControl.MainForm();
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-5000, -5000);
        f.ShowInTaskbar = false;
        foreach (string t in new[] { "tick", "slowTimer" })
        {
            var tm = (Timer)Get(f, t);
            tm.Stop(); tm.Enabled = false;
        }
        f.Show();
        Application.DoEvents();
        var tabs = (TabControl)Get(f, "tabs");
        var themeBox = (ComboBox)Get(f, "themeBox");
        var fmt = (ComboBox)Get(f, "qualFormatBox");
        var indy = (ComboBox)Get(f, "qualIndyBox");
        foreach (int theme in ((args.Length > 1 ? args[1] : "21")).ToCharArray().Select(c => c - '0'))
        {
            themeBox.SelectedIndex = theme; Application.DoEvents();
            string tn = theme == 2 ? "dark" : "light";
            foreach (var size in new[] { new Size(660, 900), new Size(1100, 900) })
            {
                f.Size = size; f.CreateControl(); f.PerformLayout(); Application.DoEvents();
                foreach (TabPage p in tabs.TabPages)
                {
                    if (p.Text == "Qualifying")
                    {
                        // 0 off, 1 single, 2 knockout, 3 indy oval, 4 indy road
                        for (int kind = 0; kind <= 4; kind++)
                        {
                            if (kind >= 3) { indy.SelectedIndex = kind - 2; fmt.SelectedIndex = 0; } else { fmt.SelectedIndex = kind; indy.SelectedIndex = 0; }
                            Snap(f, tabs, p, outDir + "\\" + tn + "_" + size.Width + "_qual" + kind + ".png");
                            if (kind == 1)
                            {
                                p.AutoScrollPosition = new Point(0, 5000); p.PerformLayout(); Application.DoEvents(); Save(f, outDir + "\\" + tn + "_" + size.Width + "_qual1_bottom.png");
                                // a type picked while scrolled down, then scrolled back up: no gap above the first box
                                fmt.SelectedIndex = 2; Application.DoEvents(); fmt.SelectedIndex = 1; Application.DoEvents();
                                p.AutoScrollPosition = new Point(0, 0); p.PerformLayout(); Application.DoEvents(); Save(f, outDir + "\\" + tn + "_" + size.Width + "_qual1_rescroll.png");
                            }
                        }
                    }
                    else if (size.Width == 660 || p.Text == "Qualifying") Snap(f, tabs, p, outDir + "\\" + tn + "_" + size.Width + "_" + p.Text + ".png");
                }
            }
        }
        Console.WriteLine("done");
    }

    static void Snap(Form f, TabControl tabs, TabPage p, string path)
    {
        tabs.SelectedTab = p;
        f.PerformLayout(); p.PerformLayout(); Application.DoEvents();
        p.AutoScrollPosition = new Point(0, 0); Application.DoEvents();
        Save(f, path);
    }

    static void Save(Form f, string path)
    {
        using (var bmp = new Bitmap(f.Width, f.Height))
        {
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            bmp.Save(path);
        }
    }
}
