using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QualControl
{
    // Light / dark look for the QualControl window (RaceControl's, in the logo's colours: black, amber, deep red). Every control is recoloured by its type; the tab headers are drawn
    // by us (WinForms tabs ignore colours) and the title bar goes dark through DWM (Windows 10 1809+ / 11).
    public static class Theme
    {
        public enum Mode { System, Light, Dark }

        public static readonly Color Back = Color.FromArgb(14, 13, 13), Panel = Color.FromArgb(26, 24, 24),
            Input = Color.FromArgb(40, 37, 36), Text = Color.FromArgb(236, 232, 226), Muted = Color.FromArgb(160, 152, 144),
            Border = Color.FromArgb(84, 76, 70), Hover = Color.FromArgb(58, 52, 48);
        // the logo's colours: QUAL amber, Control red
        public static readonly Color Amber = Color.FromArgb(245, 165, 10), Red = Color.FromArgb(176, 0, 0), RedLight = Color.FromArgb(150, 0, 0);

        // Windows' own app setting (Settings > Personalisation > Colours > app mode)
        public static bool WindowsIsDark()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    return v is int && (int)v == 0;
                }
            }
            catch (Exception) { return false; }
        }

        public static bool IsDark(Mode m) { return m == Mode.Dark || (m == Mode.System && WindowsIsDark()); }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        static void TitleBar(Form f, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
                // nudge the frame so the new title bar colour shows at once
                f.Width += 1; f.Width -= 1;
            }
            catch (Exception) { }
        }

        // the light look each control had (so switching back is exact)
        static readonly Dictionary<Control, Tuple<Color, Color>> light = new Dictionary<Control, Tuple<Color, Color>>();
        static readonly HashSet<TabControl> hooked = new HashSet<TabControl>();
        static readonly HashSet<ComboBox> hookedCombos = new HashSet<ComboBox>();
        static readonly HashSet<Button> hookedButtons = new HashSet<Button>();

        static void DrawCombo(object sender, DrawItemEventArgs e)
        {
            var cb = (ComboBox)sender;
            bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(sel ? Hover : Input)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, e.Bounds, cb.Enabled ? Text : Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        static bool dark;
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string ids);
        static readonly HashSet<Control> nativeHooked = new HashSet<Control>();
        // scroll bars and list boxes follow the dark look (Windows 10 1809+; older versions ignore it)
        static void NativeTheme(Control c)
        {
            Action set = () => { try { SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : null, null); } catch (Exception) { } };
            if (c.IsHandleCreated) set();
            if (nativeHooked.Add(c)) c.HandleCreated += (s, e) => set();
        }
        public static bool Dark { get { return dark; } }
        public static readonly Color Accent = Amber, Line = Color.FromArgb(70, 62, 56);

        public static void Apply(Form f, bool on)
        {
            dark = on;
            if (f.IsHandleCreated) TitleBar(f, on); else f.HandleCreated += (s, e) => TitleBar(f, dark);
            Walk(f);
            f.Invalidate(true);
        }

        // controls tagged Brand keep their own colours (the name in the header)
        public static readonly object Brand = new object();

        static void Walk(Control c)
        {
            if (c.Tag == Brand) return;
            if (!light.ContainsKey(c)) light[c] = Tuple.Create(c.BackColor, c.ForeColor);
            var orig = light[c];
            bool muted = orig.Item2 == SystemColors.GrayText;

            if (c is ListBox) NativeTheme(c);
            if (c is TextBox || c is ListBox || c is NumericUpDown || c is ComboBox || c is RichTextBox)
            {
                c.BackColor = dark ? Input : orig.Item1;
                c.ForeColor = dark ? Text : orig.Item2;
                var cb = c as ComboBox;
                if (cb != null)
                {
                    // drop-down lists ignore colours: draw the box and its items ourselves in the dark look
                    if (hookedCombos.Add(cb)) cb.DrawItem += DrawCombo;
                    cb.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                    cb.DrawMode = dark ? DrawMode.OwnerDrawFixed : DrawMode.Normal;
                }
                var lb = c as ListBox;
                if (lb != null) lb.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                var tb = c as TextBox;
                if (tb != null) tb.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                var nu = c as NumericUpDown;
                if (nu != null) nu.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
            }
            else if (c is Button)
            {
                var b = (Button)c;
                if (dark)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderSize = 1;
                    b.FlatAppearance.BorderColor = Border;
                    b.FlatAppearance.MouseOverBackColor = Hover;
                    b.FlatAppearance.MouseDownBackColor = Color.FromArgb(90, 30, 20);
                    b.BackColor = Input; b.ForeColor = b.Enabled ? Text : Muted;
                    if (hookedButtons.Add(b)) b.EnabledChanged += (s, e) => { if (dark) b.ForeColor = b.Enabled ? Text : Muted; };
                }
                else
                {
                    b.FlatStyle = FlatStyle.Standard;
                    b.BackColor = orig.Item1; b.ForeColor = orig.Item2;
                    b.UseVisualStyleBackColor = true;
                }
            }
            else if (c is ThemedTabControl) ((ThemedTabControl)c).SetDark(dark);
            else if (c is TabControl)
            {
                var t = (TabControl)c;
                if (hooked.Add(t)) t.DrawItem += DrawTab;
                t.DrawMode = dark ? TabDrawMode.OwnerDrawFixed : TabDrawMode.Normal;
            }
            else if (c is TabPage)
            {
                var p = (TabPage)c;
                NativeTheme(p);
                p.BackColor = dark ? Panel : orig.Item1;
                p.ForeColor = dark ? Text : orig.Item2;
                p.UseVisualStyleBackColor = !dark;           // after BackColor: setting a colour turns the system's page look off
            }
            else if (c is GroupBox || c is Panel || c is Form || c is UserControl)
            {
                c.BackColor = dark ? (c is Form ? Back : Panel) : orig.Item1;
                c.ForeColor = dark ? Text : orig.Item2;
            }
            else if (c is CheckBox || c is RadioButton || c is Label || c is LinkLabel)
            {
                c.ForeColor = dark ? (muted ? Muted : Text) : orig.Item2;
                if (c.BackColor != Color.Transparent) c.BackColor = dark ? Color.Transparent : orig.Item1;
            }
            else if (!(c is PictureBox))
            {
                c.BackColor = dark ? Panel : orig.Item1;
                c.ForeColor = dark ? Text : orig.Item2;
            }
            foreach (Control k in c.Controls) Walk(k);
        }

        static void DrawTab(object sender, DrawItemEventArgs e)
        {
            var t = (TabControl)sender;
            if (e.Index < 0 || e.Index >= t.TabPages.Count) return;
            bool sel = e.Index == t.SelectedIndex;
            using (var b = new SolidBrush(sel ? Panel : Back)) e.Graphics.FillRectangle(b, e.Bounds);
            if (sel) using (var p = new Pen(Accent, 2)) e.Graphics.DrawLine(p, e.Bounds.Left + 2, e.Bounds.Bottom - 1, e.Bounds.Right - 2, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, t.TabPages[e.Index].Text, t.Font, e.Bounds, sel ? Text : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // A group box whose frame we draw in the dark look (the system draws it light whatever the colours are).
    public class ThemedGroupBox : GroupBox
    {
        public ThemedGroupBox() { SetStyle(ControlStyles.ResizeRedraw, true); }

        protected override void OnPaint(PaintEventArgs e)
        {
            // QualControl's twist on RaceControl's boxes, light and dark: the title in the logo's colours with a short red
            // rule under it (amber on dark, deep red on light)
            bool dark = Theme.Dark;
            var g = e.Graphics;
            Color back = dark ? (BackColor == Color.Transparent ? Theme.Panel : BackColor) : (Parent != null ? Parent.BackColor : SystemColors.Control);
            using (var b = new SolidBrush(back)) g.FillRectangle(b, ClientRectangle);
            using (var bold = new Font(Font, FontStyle.Bold))
            {
                Size ts = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, bold, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                int top = ts.Height / 2;
                using (var pen = new Pen(dark ? Theme.Line : SystemColors.ControlDark))
                {
                    int r = Width - 1, bt = Height - 1;
                    int gapL = 6, gapR = ts.Width > 0 ? 10 + ts.Width : 0;
                    g.DrawLine(pen, 0, top, ts.Width > 0 ? gapL : r, top);
                    if (ts.Width > 0) g.DrawLine(pen, gapR, top, r, top);
                    g.DrawLine(pen, 0, top, 0, bt); g.DrawLine(pen, r, top, r, bt); g.DrawLine(pen, 0, bt, r, bt);
                }
                if (ts.Width > 0)
                {
                    TextRenderer.DrawText(g, Text, bold, new Point(8, 0), dark ? Theme.Amber : Theme.RedLight, TextFormatFlags.NoPadding);
                    using (var red = new Pen(Theme.Red, 2)) g.DrawLine(red, 8, ts.Height + 1, 8 + Math.Min(ts.Width, 28), ts.Height + 1);
                }
            }
        }
    }

    // A tab control that paints its own header strip and frame in the dark look (the system's stays white-edged).
    public class ThemedTabControl : TabControl
    {

        public void SetDark(bool on)
        {
            // our own painting only in the dark look; the light look is the system's, untouched
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, on);
            UpdateStyles();
            Invalidate(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!Theme.Dark) { base.OnPaint(e); return; }
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Back)) g.FillRectangle(b, ClientRectangle);
            int bottom = TabCount > 0 ? GetTabRect(0).Bottom : 0;
            using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, 0, bottom, Width, Height - bottom);
            using (var pen = new Pen(Theme.Line)) g.DrawRectangle(pen, 0, bottom, Width - 1, Height - bottom - 1);
            for (int i = 0; i < TabCount; i++)
            {
                Rectangle r = GetTabRect(i);
                bool sel = i == SelectedIndex;
                using (var b = new SolidBrush(sel ? Theme.Panel : Theme.Back)) g.FillRectangle(b, r.X, r.Y, r.Width, r.Height + (sel ? 1 : 0));
                if (sel)
                {
                    using (var pen = new Pen(Theme.Line)) { g.DrawLine(pen, r.Left, r.Top, r.Left, r.Bottom); g.DrawLine(pen, r.Right - 1, r.Top, r.Right - 1, r.Bottom); g.DrawLine(pen, r.Left, r.Top, r.Right - 1, r.Top); }
                    using (var pen = new Pen(Theme.Accent, 2)) g.DrawLine(pen, r.Left + 2, r.Top + 1, r.Right - 3, r.Top + 1);
                }
                TextRenderer.DrawText(g, TabPages[i].Text, Font, r, sel ? Theme.Text : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // A combo box whose border and drop-down arrow are drawn in the dark look (a flat combo keeps a white frame otherwise).
    public class ThemedComboBox : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0xF || !Theme.Dark || DropDownStyle == ComboBoxStyle.Simple) return;     // WM_PAINT
            try
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    Rectangle r = ClientRectangle;
                    int bw = SystemInformation.VerticalScrollBarWidth;
                    var btn = new Rectangle(r.Right - bw - 1, 1, bw, r.Height - 2);
                    Color edge = Focused || DroppedDown ? Theme.Accent : Theme.Border;
                    using (var b = new SolidBrush(Enabled ? Theme.Hover : Theme.Input)) g.FillRectangle(b, btn);
                    using (var pen = new Pen(Enabled ? Theme.Text : Theme.Muted, 1.6f))
                    {
                        int cx = btn.X + btn.Width / 2, cy = btn.Y + btn.Height / 2;
                        g.DrawLines(pen, new[] { new Point(cx - 3, cy - 1), new Point(cx, cy + 2), new Point(cx + 3, cy - 1) });
                    }
                    using (var pen = new Pen(edge)) g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
                    using (var pen = new Pen(Theme.Input)) g.DrawRectangle(pen, 1, 1, r.Width - 3, r.Height - 3);
                    using (var pen = new Pen(edge)) g.DrawLine(pen, btn.X - 1, 1, btn.X - 1, r.Height - 2);
                }
            }
            catch (Exception) { }
        }
    }
}
