using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DshUsage
{
    // ---------------- 极简 JSON 解析 ----------------
    static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            return Value(text, ref i);
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static object Value(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            return Num(s, ref i);
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (s[i] != '"') break;
                string k = Str(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                d[k] = Value(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return d;
        }

        static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (i < s.Length)
            {
                l.Add(Value(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return l;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // "
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 'b') sb.Append('\b');
                    else if (e == 'f') sb.Append('\f');
                    else if (e == 'u')
                    {
                        if (i + 4 <= s.Length)
                        {
                            int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber);
                            sb.Append((char)code);
                            i += 4;
                        }
                    }
                    else sb.Append(e);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static object Num(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && ("+-0123456789.eE".IndexOf(s[i]) >= 0)) i++;
            double d;
            double.TryParse(s.Substring(start, i - start), NumberStyles.Any, CultureInfo.InvariantCulture, out d);
            return d;
        }

        // 便捷取值
        public static Dictionary<string, object> AsMap(object o) { return o as Dictionary<string, object>; }
        public static List<object> AsList(object o) { return o as List<object>; }
        public static string Str2(object o) { if (o == null) return ""; return Convert.ToString(o, CultureInfo.InvariantCulture); }
        public static long Long(object o)
        {
            if (o == null) return 0;
            try { return (long)Math.Round(ToDouble(o)); } catch { return 0; }
        }
        public static double ToDouble(object o)
        {
            if (o == null) return 0;
            if (o is double) return (double)o;
            if (o is bool) return ((bool)o) ? 1 : 0;
            double d; double.TryParse(Str2(o), NumberStyles.Any, CultureInfo.InvariantCulture, out d); return d;
        }
    }

    // ---------------- 配置 ----------------
    class Config
    {
        public string DataDir;
        public bool AutoRefresh = true;
        public int IntervalSec = 10;
        public string HolidaysRaw = "";
        public double Budget = 0;
        public bool Tray = true;
        public bool Hotkey = true;
        public bool NoGuide = false;
        public double PriceOffMiss = 1.0, PriceOffHit = 0.02, PriceOffOut = 4.0;
        public double PricePkMiss = 2.0, PricePkHit = 0.04, PricePkOut = 8.0;

        static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dsh-usage");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "config.ini");
            }
        }

        public static string DefaultDataDir()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, Path.Combine(".dsh", Path.Combine("storages", Path.Combine("session_projcache", "sessions"))));
        }

        public static Config Load()
        {
            var c = new Config();
            c.DataDir = DefaultDataDir();
            try
            {
                if (!File.Exists(FilePath)) return c;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int p = line.IndexOf('=');
                    if (p < 0) continue;
                    string k = line.Substring(0, p).Trim();
                    string v = line.Substring(p + 1).Trim();
                    if (k == "dataDir") c.DataDir = v;
                    else if (k == "autoRefresh") c.AutoRefresh = (v == "1");
                    else if (k == "holidays") c.HolidaysRaw = v;
                    else if (k == "interval") { int n; if (int.TryParse(v, out n) && n >= 3 && n <= 600) c.IntervalSec = n; }
                    else if (k == "budget") { double d; if (double.TryParse(v, out d) && d >= 0) c.Budget = d; }
                    else if (k == "tray") c.Tray = (v == "1");
                    else if (k == "hotkey") c.Hotkey = (v == "1");
                    else if (k == "noGuide") c.NoGuide = (v == "1");
                    else if (k == "priceOff" || k == "pricePeak")
                    {
                        string[] parts = v.Split(',');
                        if (parts.Length == 3)
                        {
                            double a, b2, c3;
                            if (double.TryParse(parts[0], out a) && double.TryParse(parts[1], out b2) && double.TryParse(parts[2], out c3))
                            {
                                if (k == "priceOff") { c.PriceOffMiss = a; c.PriceOffHit = b2; c.PriceOffOut = c3; }
                                else { c.PricePkMiss = a; c.PricePkHit = b2; c.PricePkOut = c3; }
                            }
                        }
                    }
                }
            }
            catch { }
            if (string.IsNullOrEmpty(c.DataDir)) c.DataDir = DefaultDataDir();
            return c;
        }

        static string FmtNum(double d)
        {
            return d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("dataDir=" + DataDir);
                sb.AppendLine("autoRefresh=" + (AutoRefresh ? "1" : "0"));
                sb.AppendLine("interval=" + IntervalSec);
                sb.AppendLine("holidays=" + (HolidaysRaw ?? ""));
                sb.AppendLine("budget=" + Budget.ToString("0.##"));
                sb.AppendLine("tray=" + (Tray ? "1" : "0"));
                sb.AppendLine("hotkey=" + (Hotkey ? "1" : "0"));
                sb.AppendLine("noGuide=" + (NoGuide ? "1" : "0"));
                sb.AppendLine("priceOff=" + FmtNum(PriceOffMiss) + "," + FmtNum(PriceOffHit) + "," + FmtNum(PriceOffOut));
                sb.AppendLine("pricePeak=" + FmtNum(PricePkMiss) + "," + FmtNum(PricePkHit) + "," + FmtNum(PricePkOut));
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }
    }

    class SessionInfo
    {
        public string Id, Title, Cwd;
        public long Total, Uncached, Cache, OutN;
        public int Turns;
        public DateTime Last;
        public Timeline Tl;
        public Timeline RangeTl;
        public bool Approx;
        public string Model = "";

        // 主对话 / 子智能体
        public bool IsSub;
        public string SubLabel = "";
        public string ParentId = "";
        public string ParentTitle = "";
        public List<string> ChildIds = new List<string>();

        public string KindText { get { return IsSub ? "子智能体" : "主对话"; } }
        public string ShowName()
        {
            if (IsSub) return "[子] " + (SubLabel.Length > 0 ? SubLabel : Title);
            return "[主] " + Title;
        }
    }

    // 单价（元/百万 token），可在设置里改
    static class Pricing
    {
        public static double OffMiss = 1.0, OffHit = 0.02, OffOut = 4.0;
        public static double PkMiss = 2.0, PkHit = 0.04, PkOut = 8.0;
    }

    // 按峰谷时段拆分后的 token
    class Slot
    {
        public long OffU, OffC, OffO, PkU, PkC, PkO;

        public void Add(long iN, long cR, long oN, bool peak)
        {
            if (peak) { PkU += iN; PkC += cR; PkO += oN; }
            else { OffU += iN; OffC += cR; OffO += oN; }
        }

        public void Merge(Slot o)
        {
            OffU += o.OffU; OffC += o.OffC; OffO += o.OffO;
            PkU += o.PkU; PkC += o.PkC; PkO += o.PkO;
        }

        public long OffTokens() { return OffU + OffC + OffO; }
        public long PkTokens() { return PkU + PkC + PkO; }
        public long TotalTokens() { return OffTokens() + PkTokens(); }

        public double OffCost()
        {
            return (OffU / 1000000.0) * Pricing.OffMiss + (OffC / 1000000.0) * Pricing.OffHit + (OffO / 1000000.0) * Pricing.OffOut;
        }
        public double PkCost()
        {
            return (PkU / 1000000.0) * Pricing.PkMiss + (PkC / 1000000.0) * Pricing.PkHit + (PkO / 1000000.0) * Pricing.PkOut;
        }
        public double TotalCost() { return OffCost() + PkCost(); }
    }

    class Timeline : Slot
    {
        public long RawSize;
        public long RawTicks;
        public bool FromRaw;
        public Dictionary<string, Slot> Days = new Dictionary<string, Slot>();
        public Dictionary<string, Slot> DateHours = new Dictionary<string, Slot>();
        public Slot[] Hours = new Slot[24];

        public Timeline()
        {
            for (int i = 0; i < 24; i++) Hours[i] = new Slot();
        }
    }

    // 时间切片：按“任意时段”裁剪出新的时间线
    static class Tl
    {
        public static Timeline Slice(Timeline src, DateTime a, DateTime b)
        {
            if (src == null) return null;
            if (src.DateHours == null || src.DateHours.Count == 0)
            {
                var only = new Timeline();
                only.FromRaw = src.FromRaw;
                only.RawSize = src.RawSize;
                only.RawTicks = src.RawTicks;
                foreach (var kv in src.Days)
                {
                    DateTime d;
                    if (!DateTime.TryParse(kv.Key, out d)) continue;
                    if (d.Date < a.Date || d.Date > b.Date) continue;
                    only.Days[kv.Key] = kv.Value;
                    only.Merge(kv.Value);
                }
                return only;
            }
            var t = new Timeline();
            t.FromRaw = src.FromRaw;
            t.RawSize = src.RawSize;
            t.RawTicks = src.RawTicks;
            foreach (var kv in src.DateHours)
            {
                DateTime hh;
                string k2 = kv.Key.Replace(' ', 'T') + ":00:00";
                if (!DateTime.TryParse(k2, out hh)) continue;
                if (hh < a || hh >= b) continue;
                t.Merge(kv.Value);
                string dk = kv.Key.Substring(0, 10);
                Slot ds;
                if (!t.Days.TryGetValue(dk, out ds)) { ds = new Slot(); t.Days[dk] = ds; }
                ds.Merge(kv.Value);
                t.Hours[hh.Hour].Merge(kv.Value);
            }
            return t;
        }
    }

    // ---------------- 主窗口 ----------------
    // 统一的浅色主题与控件工厂
    static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(238, 241, 246);
        public static readonly Color Line = Color.FromArgb(227, 231, 239);
        public static readonly Color Line2 = Color.FromArgb(237, 240, 245);
        public static readonly Color Ink = Color.FromArgb(27, 33, 48);
        public static readonly Color Ink2 = Color.FromArgb(90, 98, 114);
        public static readonly Color Ink3 = Color.FromArgb(139, 147, 163);
        public static readonly Color Accent = Color.FromArgb(47, 107, 224);
        public static readonly Color AccentSoft = Color.FromArgb(234, 241, 254);
        public static readonly Color AccentLine = Color.FromArgb(191, 212, 251);
        public static readonly Color Nice = Color.FromArgb(31, 169, 122);
        public static readonly Color RowSel = Color.FromArgb(241, 246, 254);

        static float k = 0f;
        // 当前屏幕缩放比例（DPI/96）。用于自绘控件里那些不受 WinForms 自动缩放影响的像素值
        public static float K
        {
            get
            {
                if (k <= 0)
                {
                    try
                    {
                        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
                    }
                    catch { k = 1f; }
                    if (k <= 0) k = 1f;
                }
                return k;
            }
        }

        public static int S(int v) { return (int)Math.Round(v * K); }
        public static int S(float v) { return (int)Math.Round(v * K); }

        // 按当前 DPI 等比放大整棵控件树（字体本身是按点渲染的，会跟着 DPI 一起变大）
        public static void ScaleTree(Control c)
        {
            if (K > 1.001f) c.Scale(new SizeF(K, K));
        }

        public static Font F(float size) { return new Font("微软雅黑", size); }
        public static Font FB(float size) { return new Font("微软雅黑", size, FontStyle.Bold); }

        public static GraphicsPath RoundPath(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (r.Width <= 1 || r.Height <= 1) return p;
            int d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) return p;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // 开了 AllPaintingInWmPaint 的自绘控件必须自己擦底，否则旧像素会一直残留（150% DPI 下尤其明显）
        public static void Pre(Control c, Graphics g)
        {
            g.Clear(c.BackColor);
        }

        static System.Reflection.PropertyInfo _dbProp;

        // 给容器开双缓冲，避免子控件重绘时父层闪烁/留残影
        public static void Buf(Control c)
        {
            if (_dbProp == null)
                _dbProp = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (_dbProp != null && c != null)
            {
                try { _dbProp.SetValue(c, true, null); } catch { }
            }
            if (c != null)
                foreach (Control ch in c.Controls) Buf(ch);
        }

        public static void RoundControl(Control c, int rad)
        {
            EventHandler fix = delegate
            {
                using (GraphicsPath path = RoundPath(new Rectangle(0, 0, c.Width, c.Height), (int)Math.Round(rad * K)))
                {
                    Region old = c.Region;
                    c.Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            };
            c.SizeChanged += fix;
            fix(c, EventArgs.Empty);
            if (c.Parent != null) c.Parent.Invalidate();
        }

        public static void DrawCard(Graphics g, Rectangle r, Color fill, Color border, int rad)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundPath(r, rad))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, path);
            }
        }

        public static Button Ghost(string text, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = F(9f);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Line;
            b.BackColor = Color.White;
            b.ForeColor = Ink2;
            b.Size = new Size(w, h);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        public static Button Primary(string text, int w, int h)
        {
            Button b = Ghost(text, w, h);
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Accent;
            return b;
        }
    }

    // ---------------- 自绘圆角下拉框 ----------------
    class FlatCombo : Control
    {
        List<string> items = new List<string>();
        int sel = -1;
        bool hot, dropped;
        ComboPopup pop;
        public event EventHandler SelectedIndexChanged;

        public FlatCombo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Ui.F(9f);
            Height = 30;
            Cursor = Cursors.Hand;
            BackColor = Color.White;
            TabStop = true;
        }

        public List<string> Items { get { return items; } }

        public void Fill(params string[] a)
        {
            items.Clear();
            items.AddRange(a);
            if (sel >= items.Count) sel = items.Count - 1;
            Invalidate();
        }

        public void FitWidth()
        {
            int w = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int t = TextRenderer.MeasureText(items[i], Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
                if (t > w) w = t;
            }
            Width = w + Ui.S(56);
        }

        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                int v = value;
                if (v < -1) v = -1;
                if (v >= items.Count) v = items.Count - 1;
                if (sel == v) return;
                sel = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public string SelectedItem
        {
            get { return (sel >= 0 && sel < items.Count) ? items[sel] : null; }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            if (dropped) ClosePop();
            else OpenPop();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Enter || keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down) { SelectedIndex = sel + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = sel - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { if (!dropped) OpenPop(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { if (dropped) ClosePop(); e.Handled = true; }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        public void OpenPop()
        {
            if (items.Count == 0) return;
            dropped = true;
            Invalidate();
            pop = new ComboPopup(this, items.ToArray(), sel);
            Point p = PointToScreen(new Point(0, Height + 4));
            Screen sc = Screen.FromControl(this);
            if (p.Y + pop.Height > sc.WorkingArea.Bottom) p = PointToScreen(new Point(0, -pop.Height - 4));
            if (p.X + pop.Width > sc.WorkingArea.Right) p.X = sc.WorkingArea.Right - pop.Width - 4;
            pop.Location = p;
            pop.Show();
        }

        public void ClosePop()
        {
            dropped = false;
            Invalidate();
            if (pop != null)
            {
                ComboPopup p2 = pop;
                pop = null;
                p2.Close();
            }
        }

        public ComboPopup CurrentPop { get { return pop; } }

        public void Pick(int idx)
        {
            SelectedIndex = idx;
            ClosePop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Pre(this, g);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.DrawCard(g, r, Color.White, (hot || dropped || Focused) ? Ui.AccentLine : Ui.Line, Ui.S(8));
            string text = SelectedItem;
            if (string.IsNullOrEmpty(text)) text = "";
            TextRenderer.DrawText(g, text, Font, new Rectangle(Ui.S(10), 0, Width - Ui.S(32), Height), Ui.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            int cx = Width - Ui.S(16), cy = Height / 2;
            using (Pen pen = new Pen(Ui.Ink3, 1.6f))
            {
                g.DrawLine(pen, cx - Ui.S(4), cy - Ui.S(2), cx, cy + Ui.S(2));
                g.DrawLine(pen, cx, cy + Ui.S(2), cx + Ui.S(4), cy - Ui.S(2));
            }
        }
    }

    class ComboPopup : Form
    {
        FlatCombo owner;
        string[] items;
        int sel, hotIdx = -1;
        int Pad = Ui.S(8), ItemH = Ui.S(32);

        public ComboPopup(FlatCombo c, string[] its, int idx)
        {
            owner = c;
            items = its;
            sel = idx;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.White;
            DoubleBuffered = true;
            int w = Math.Max(c.Width, Ui.S(140));
            ClientSize = new Size(w, items.Length * ItemH + Pad * 2);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            Deactivate += new EventHandler(delegate { owner.ClosePop(); });
            MouseMove += new MouseEventHandler(OnMove);
            MouseDown += new MouseEventHandler(OnDown);
            Paint += new PaintEventHandler(OnPaint);
            ApplyRegion();
        }

        void ApplyRegion()
        {
            using (System.Drawing.Drawing2D.GraphicsPath path = Ui.RoundPath(new Rectangle(0, 0, Width, Height), Ui.S(10)))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        int HitIndex(int y)
        {
            int i = (y - Pad) / ItemH;
            if (y < Pad || i < 0 || i >= items.Length) return -1;
            return i;
        }

        void OnMove(object s, MouseEventArgs e)
        {
            int i = HitIndex(e.Y);
            if (i != hotIdx) { hotIdx = i; Invalidate(); }
        }

        void OnDown(object s, MouseEventArgs e)
        {
            int i = HitIndex(e.Y);
            if (i >= 0) owner.Pick(i);
            else owner.ClosePop();
        }

        void OnPaint(object s, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillRectangle(b, ClientRectangle);
            using (System.Drawing.Drawing2D.GraphicsPath path = Ui.RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(10)))
            using (Pen pen = new Pen(Ui.Line)) g.DrawPath(pen, path);
            for (int i = 0; i < items.Length; i++)
            {
                Rectangle r = new Rectangle(Ui.S(6), Pad + i * ItemH, Width - Ui.S(12), ItemH - Ui.S(2));
                if (i == hotIdx)
                {
                    using (System.Drawing.Drawing2D.GraphicsPath path = Ui.RoundPath(r, Ui.S(6)))
                    using (SolidBrush b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, path);
                }
                Color c = (i == sel) ? Ui.Accent : Ui.Ink;
                Font f = (i == sel) ? Ui.FB(9f) : Ui.F(9f);
                TextRenderer.DrawText(g, items[i], f, new Rectangle(r.X + Ui.S(8), r.Y, r.Width - Ui.S(16), r.Height), c,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }
    }

    // ---------------- 自绘数字步进框 ----------------
    class SpinBox : Control
    {
        decimal val, lo = 0m, hi = 100m, step = 1m;
        int dec = 0, pad0 = 0;
        bool hot;
        public string Unit = "";
        public event EventHandler ValueChanged;

        public SpinBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Ui.F(9f);
            Height = 30;
            Cursor = Cursors.Hand;
            BackColor = Color.White;
        }

        public int Minimum { get { return (int)lo; } set { lo = value; if (val < lo) Value = (int)lo; Invalidate(); } }
        public int Maximum { get { return (int)hi; } set { hi = value; if (val > hi) Value = (int)hi; Invalidate(); } }
        public decimal MinimumD { get { return lo; } set { lo = value; if (val < lo) ValueD = lo; Invalidate(); } }
        public decimal MaximumD { get { return hi; } set { hi = value; if (val > hi) ValueD = hi; Invalidate(); } }
        public decimal Step { get { return step; } set { if (value > 0m) step = value; } }
        public int Decimals { get { return dec; } set { dec = value; Invalidate(); } }
        public int MinDigits { get { return pad0; } set { pad0 = value; Invalidate(); } }

        public int Value
        {
            get { return (int)val; }
            set
            {
                int v = value;
                if (v < lo) v = (int)lo;
                if (v > hi) v = (int)hi;
                if (v == (int)val) return;
                val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public decimal ValueD
        {
            get { return val; }
            set
            {
                decimal v = value;
                if (v < lo) v = lo;
                if (v > hi) v = hi;
                if (v == val) return;
                val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        string ShowText()
        {
            if (dec > 0) return val.ToString("F" + dec);
            if (pad0 > 0) return ((int)val).ToString().PadLeft(pad0, '0');
            return val.ToString("0");
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (e.X < Width - Ui.S(24)) return;
            if (e.Y < Height / 2) ValueD = val + step;
            else ValueD = val - step;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ValueD = val + (e.Delta > 0 ? step : -step);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Pre(this, g);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Ui.DrawCard(g, new Rectangle(0, 0, Width - 1, Height - 1), Color.White, hot ? Ui.AccentLine : Ui.Line, Ui.S(8));
            string txt = ShowText();
            TextRenderer.DrawText(g, txt, Font, new Rectangle(Ui.S(10), 0, Width - Ui.S(40), Height), Ui.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            if (Unit.Length > 0)
            {
                int vw = TextRenderer.MeasureText(txt, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                TextRenderer.DrawText(g, Unit, Ui.F(8f), new Rectangle(Ui.S(10) + vw + Ui.S(2), 0, Width - Ui.S(30), Height), Ui.Ink3,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }
            using (Pen pen = new Pen(Ui.Line2)) g.DrawLine(pen, Width - Ui.S(24), Ui.S(6), Width - Ui.S(24), Height - Ui.S(7));
            int cx = Width - Ui.S(12);
            using (Pen pen = new Pen(Ui.Ink3, 1.5f))
            {
                g.DrawLine(pen, cx - Ui.S(4), Ui.S(12), cx, Ui.S(8));
                g.DrawLine(pen, cx, Ui.S(8), cx + Ui.S(4), Ui.S(12));
                g.DrawLine(pen, cx - Ui.S(4), Ui.S(21), cx, Ui.S(25));
                g.DrawLine(pen, cx, Ui.S(25), cx + Ui.S(4), Ui.S(21));
            }
        }
    }

    // ---------------- 自绘圆角勾选框 ----------------
    class FlatCheck : CheckBox
    {
        bool hot;

        public FlatCheck()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Ui.F(9f);
            ForeColor = Ui.Ink2;
            BackColor = Color.White;
            AutoSize = false;
            Height = 30;
        }

        public void FitWidth()
        {
            int t = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
            Width = Ui.S(16) + Ui.S(8) + t + Ui.S(20);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Pre(this, g);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int box = Ui.S(16), y = (Height - box) / 2;
            Rectangle r = new Rectangle(Ui.S(1), y, box, box);
            if (Checked)
            {
                Ui.DrawCard(g, r, Ui.Accent, Ui.Accent, Ui.S(4));
                using (Pen pen = new Pen(Color.White, 1.8f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLines(pen, new Point[] {
                        new Point(r.X + Ui.S(4), r.Y + Ui.S(8)), new Point(r.X + Ui.S(7), r.Y + Ui.S(11)), new Point(r.X + Ui.S(12), r.Y + Ui.S(5)) });
                }
            }
            else
            {
                Ui.DrawCard(g, r, Color.White, hot ? Ui.AccentLine : Color.FromArgb(205, 212, 225), Ui.S(4));
            }
            if (!string.IsNullOrEmpty(Text))
                TextRenderer.DrawText(g, Text, Font, new Rectangle(box + Ui.S(8), 0, Width - box - Ui.S(8), Height), ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }

    // ---------------- 自绘圆角日期时间框 ----------------
    class FlatDate : Control
    {
        DateTime val = DateTime.Now;
        bool hot;
        CalendarPopup pop;
        DateTime lastClose = DateTime.MinValue;
        public event EventHandler ValueChanged;

        public FlatDate()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Ui.F(9f);
            Height = 30;
            Cursor = Cursors.Hand;
            BackColor = Color.White;
            TabStop = true;
        }

        public DateTime Value
        {
            get { return val; }
            set
            {
                DateTime v = value;
                if (v == val) return;
                val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public bool Dropped { get { return pop != null; } }
        public Form CurrentPop { get { return pop; } }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            if (pop != null) { ClosePop(); return; }
            if ((DateTime.Now - lastClose).TotalMilliseconds < 250) return;
            OpenPop();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && pop != null) { ClosePop(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OpenPop(); e.Handled = true; }
        }

        public void OpenPop()
        {
            if (pop != null) return;
            pop = new CalendarPopup(this, val);
            Form f = FindForm();
            Point topLeft = (f != null) ? f.PointToScreen(new Point(Left, Top)) : PointToScreen(new Point(0, 0));
            Point below = new Point(topLeft.X, topLeft.Y + Height + Ui.S(4));
            Rectangle wa = Screen.FromPoint(below).WorkingArea;
            if (below.Y + pop.Height > wa.Bottom - Ui.S(4))
                below = new Point(topLeft.X, topLeft.Y - pop.Height - Ui.S(4));
            if (below.X + pop.Width > wa.Right - Ui.S(4)) below.X = wa.Right - Ui.S(4) - pop.Width;
            if (below.X < wa.Left) below.X = wa.Left;
            if (below.Y < wa.Top) below.Y = wa.Top;
            pop.StartPosition = FormStartPosition.Manual;
            pop.Location = below;
            pop.Show(f);
        }

        public void ClosePop()
        {
            if (pop == null) return;
            lastClose = DateTime.Now;
            CalendarPopup p = pop;
            pop = null;
            p.Close();
            Invalidate();
        }

        public void Pick(DateTime v)
        {
            lastClose = DateTime.Now;
            CalendarPopup p = pop;
            pop = null;
            if (p != null) p.Close();
            Value = v;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Pre(this, g);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Ui.DrawCard(g, new Rectangle(0, 0, Width - 1, Height - 1), Color.White,
                (hot || pop != null || Focused) ? Ui.AccentLine : Ui.Line, Ui.S(8));
            string txt = val.ToString("yyyy-MM-dd   HH:mm");
            TextRenderer.DrawText(g, txt, Font, new Rectangle(Ui.S(12), 0, Width - Ui.S(48), Height), Ui.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);

            int icx = Width - Ui.S(26), icy = (Height - Ui.S(15)) / 2;
            Rectangle body = new Rectangle(icx, icy + Ui.S(2), Ui.S(16), Ui.S(13));
            using (Pen pen = new Pen(Ui.Ink3, 1.2f))
            {
                using (System.Drawing.Drawing2D.GraphicsPath path = Ui.RoundPath(body, Ui.S(3)))
                    g.DrawPath(pen, path);
                g.DrawLine(pen, body.X, body.Y + Ui.S(4), body.Right, body.Y + Ui.S(4));
                g.DrawLine(pen, body.X + Ui.S(4), body.Y - Ui.S(2), body.X + Ui.S(4), body.Y + Ui.S(1));
                g.DrawLine(pen, body.Right - Ui.S(4), body.Y - Ui.S(2), body.Right - Ui.S(4), body.Y + Ui.S(1));
            }
        }
    }

    // ---------------- 自绘日历弹层 ----------------
    class CalendarPopup : Form
    {
        FlatDate owner;
        DateTime sel, month;
        int hotCell = -1, hotPrev, hotNext;
        SpinBox sh, sm;
        Button okBtn;
        string[] weeks = new string[] { "日", "一", "二", "三", "四", "五", "六" };
        int pad = Ui.S(10), headH = Ui.S(40), weekH = Ui.S(24), cellH = Ui.S(33), footH = Ui.S(54);
        int W = Ui.S(312), H, gy0, cw, gx0;

        public CalendarPopup(FlatDate o, DateTime v)
        {
            owner = o;
            sel = v;
            month = new DateTime(v.Year, v.Month, 1);
            H = pad + headH + weekH + 6 * cellH + footH + pad;
            gy0 = pad + headH + weekH;
            cw = (W - pad * 2) / 7;
            gx0 = pad + ((W - pad * 2) - cw * 7) / 2;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            ClientSize = new Size(W, H);
            Font = Ui.F(9f);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            Deactivate += new EventHandler(delegate { owner.ClosePop(); });

            int by = gy0 + 6 * cellH;
            var lblT = new Label();
            lblT.Text = "时间";
            lblT.AutoSize = true;
            lblT.ForeColor = Ui.Ink3;
            lblT.Font = Ui.F(8.5f);
            lblT.Location = new Point(pad + Ui.S(2), by + Ui.S(17));

            sh = new SpinBox();
            sh.Height = Ui.S(28);
            sh.Width = Ui.S(76);
            sh.Minimum = 0;
            sh.Maximum = 23;
            sh.MinDigits = 2;
            sh.Value = v.Hour;
            sh.Location = new Point(pad + Ui.S(44), by + Ui.S(12));

            var lblColon = new Label();
            lblColon.Text = ":";
            lblColon.AutoSize = true;
            lblColon.ForeColor = Ui.Ink2;
            lblColon.Location = new Point(sh.Right + Ui.S(3), by + Ui.S(16));

            sm = new SpinBox();
            sm.Height = Ui.S(28);
            sm.Width = Ui.S(76);
            sm.Minimum = 0;
            sm.Maximum = 59;
            sm.MinDigits = 2;
            sm.Value = v.Minute;
            sm.Location = new Point(sh.Right + Ui.S(12), by + Ui.S(12));

            okBtn = Ui.Primary("确定", Ui.S(62), Ui.S(26));
            okBtn.Font = Ui.F(9f);
            okBtn.Location = new Point(W - pad - Ui.S(62), by + Ui.S(12));
            okBtn.Click += new EventHandler(delegate { Confirm(); });

            Controls.Add(lblT);
            Controls.Add(sh);
            Controls.Add(lblColon);
            Controls.Add(sm);
            Controls.Add(okBtn);

            MouseMove += new MouseEventHandler(OnMove);
            MouseDown += new MouseEventHandler(OnDown);
            MouseLeave += new EventHandler(delegate { hotCell = -1; hotPrev = 0; hotNext = 0; Invalidate(); });
            Paint += new PaintEventHandler(delegate(object s, PaintEventArgs pe) { DrawAll(pe.Graphics); });
            ApplyRegion();
        }

        void ApplyRegion()
        {
            using (System.Drawing.Drawing2D.GraphicsPath path = Ui.RoundPath(new Rectangle(0, 0, Width, Height), Ui.S(10)))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { owner.ClosePop(); e.Handled = true; }
            else if (e.KeyCode == Keys.Left) { ShiftMonth(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { ShiftMonth(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { Confirm(); e.Handled = true; }
        }

        void ShiftMonth(int delta)
        {
            month = month.AddMonths(delta);
            hotCell = -1;
            Invalidate();
        }

        void Confirm()
        {
            owner.Pick(buildSel());
        }

        DateTime buildSel()
        {
            return new DateTime(sel.Year, sel.Month, sel.Day, sh.Value, sm.Value, 0);
        }

        int CellIndexAt(Point p)
        {
            if (p.Y < gy0 || p.Y >= gy0 + 6 * cellH) return -1;
            if (p.X < gx0 || p.X >= gx0 + cw * 7) return -1;
            int col = (p.X - gx0) / cw;
            int row = (p.Y - gy0) / cellH;
            return row * 7 + col;
        }

        DateTime DateAt(int idx)
        {
            DateTime first = new DateTime(month.Year, month.Month, 1);
            int lead = (int)first.DayOfWeek;
            return first.AddDays(idx - lead);
        }

        void OnMove(object s, MouseEventArgs e)
        {
            int idx = CellIndexAt(e.Location);
            int hp = (e.Y < pad + headH && e.X < pad + Ui.S(28)) ? 1 : 0;
            int hn = (e.Y < pad + headH && e.X > W - pad - Ui.S(28)) ? 1 : 0;
            if (idx != hotCell || hp != hotPrev || hn != hotNext)
            {
                hotCell = idx; hotPrev = hp; hotNext = hn;
                Invalidate();
            }
        }

        void OnDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.Y < pad + headH)
            {
                if (e.X < pad + Ui.S(28)) { ShiftMonth(-1); return; }
                if (e.X > W - pad - Ui.S(28)) { ShiftMonth(1); return; }
                return;
            }
            int idx = CellIndexAt(e.Location);
            if (idx < 0) return;
            DateTime d = DateAt(idx);
            if (d.Month != month.Month) month = new DateTime(d.Year, d.Month, 1);
            sel = new DateTime(d.Year, d.Month, d.Day, sel.Hour, sel.Minute, 0);
            Invalidate();
            Confirm();
        }

        protected void DrawAll(Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillRectangle(b, ClientRectangle);
            using (System.Drawing.Drawing2D.GraphicsPath path = Ui.RoundPath(new Rectangle(0, 0, W - 1, H - 1), Ui.S(10)))
            using (Pen pen = new Pen(Ui.Line)) g.DrawPath(pen, path);

            // 头部：‹ 2026 年 11 月 ›
            string title = month.ToString("yyyy 年 M 月");
            using (Font f = Ui.FB(10.5f))
            {
                SizeF sz = g.MeasureString(title, f);
                using (SolidBrush b = new SolidBrush(Ui.Ink))
                    g.DrawString(title, f, b, (W - sz.Width) / 2f, pad + (headH - sz.Height) / 2f - Ui.S(1));
            }
            int cx1 = pad + Ui.S(14), cx2 = W - pad - Ui.S(14), cy = pad + headH / 2;
            using (Pen pen = new Pen(hotPrev > 0 ? Ui.Accent : Ui.Ink3, 1.4f))
            {
                g.DrawLine(pen, cx1 + Ui.S(5), cy - Ui.S(6), cx1 - Ui.S(1), cy);
                g.DrawLine(pen, cx1 - Ui.S(1), cy, cx1 + Ui.S(5), cy + Ui.S(6));
            }
            using (Pen pen = new Pen(hotNext > 0 ? Ui.Accent : Ui.Ink3, 1.4f))
            {
                g.DrawLine(pen, cx2 - Ui.S(5), cy - Ui.S(6), cx2 + Ui.S(1), cy);
                g.DrawLine(pen, cx2 + Ui.S(1), cy, cx2 - Ui.S(5), cy + Ui.S(6));
            }

            // 星期行
            for (int i = 0; i < 7; i++)
            {
                Rectangle cell = new Rectangle(gx0 + i * cw, pad + headH, cw, weekH);
                TextRenderer.DrawText(g, weeks[i], Ui.F(8.5f), cell, Ui.Ink3,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            // 日期格
            DateTime today = DateTime.Today;
            for (int idx = 0; idx < 42; idx++)
            {
                DateTime d = DateAt(idx);
                int col = idx % 7, row = idx / 7;
                Rectangle cell = new Rectangle(gx0 + col * cw, gy0 + row * cellH, cw, cellH);
                bool inMonth = d.Month == month.Month;
                bool isSel = d.Date == sel.Date;
                bool isToday = d.Date == today;
                int rad = Math.Min(cw, cellH) / 2 - Ui.S(2);
                Rectangle circle = new Rectangle(cell.X + (cell.Width - rad * 2) / 2, cell.Y + (cell.Height - rad * 2) / 2, rad * 2, rad * 2);

                if (isSel)
                {
                    using (System.Drawing.Drawing2D.GraphicsPath path = Ui.RoundPath(circle, rad))
                    {
                        using (SolidBrush b = new SolidBrush(Ui.Accent)) g.FillPath(b, path);
                    }
                }
                else if (isToday)
                {
                    using (System.Drawing.Drawing2D.GraphicsPath path = Ui.RoundPath(circle, rad))
                    using (Pen pen = new Pen(Ui.AccentLine)) g.DrawPath(pen, path);
                }
                else if (hotCell == idx)
                {
                    using (System.Drawing.Drawing2D.GraphicsPath path = Ui.RoundPath(circle, rad))
                    using (SolidBrush b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, path);
                }

                Color tc = isSel ? Color.White : (inMonth ? Ui.Ink : Color.FromArgb(183, 190, 203));
                TextRenderer.DrawText(g, d.Day.ToString(), Ui.F(9.5f), cell, tc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            // 底部分隔线
            int by = gy0 + 6 * cellH;
            using (Pen pen = new Pen(Ui.Line2)) g.DrawLine(pen, pad, by + Ui.S(1), W - pad, by + Ui.S(1));
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ApplyRegion(); }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }
    }

    class MainForm : Form
    {
        Config cfg;
        double estOff, estOn;
        int selTurns;

        List<SessionInfo> all = new List<SessionInfo>();
        List<SessionInfo> shown = new List<SessionInfo>();
        List<SessionInfo> sel = new List<SessionInfo>();

        // 子会话折叠：默认收起，只列主对话，点左侧三角展开
        bool foldSubs = true;
        HashSet<string> expanded = new HashSet<string>();
        Dictionary<string, int> subCount = new Dictionary<string, int>();

        Panel head, header, kpiBar, filterBar, gridHost, bottom, footer;
        DataGridView grid;
        TextBox txt;
        FlatCombo cmbKind;
        CheckBox chkAuto, chkLow, chkChain;
        Panel colHead;
        SpinBox numInterval;
        Label lblUpdated, lblSel, lblTotal, lblDetail, lblEst, lblBudget;
        Label lblKind;
        Label kpi1v, kpi1n, kpi2v, kpi2n, kpi3v, kpi3n, kpi4v, kpi4n;
        Label lblTotalBig, lblFooterNote;
        ToolStripStatusLabel lblStatus;
        Button btnAll, btnClear, btnRefresh, btnCopy, btnExport, btnSettings, btnDist, btnFold, btnCompare, btnHelp;
        List<Button> chips = new List<Button>();
        Hashtable checkedIds = new Hashtable();
        int topRow = 0;
        int hoverRow = -1;
        int sortCol = 8;
        bool sortAsc = false;
        double maxRowCost = 1;
        int timeIdx = 0;
        string scopeText = "";
        System.Windows.Forms.Timer timer;
        Font listFont;
        NotifyIcon tray;
        bool exiting = false;
        bool trayTipShown = false;
        double monthCost = 0;
        DateTime? rangeA = null, rangeB = null;
        int lastTimeIdx = 0;
        bool timeGuard = false;
        static bool SelfCheck = false;
        public static bool IsSelfCheck { set { SelfCheck = value; } }
        public static int CmpN = 2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public void CompareShot(string path)
        {
            IsSelfCheck = true;
            Show();
            Application.DoEvents();
            WaitBuild(60);
            List<SessionInfo> pick = new List<SessionInfo>();
            foreach (SessionInfo s in all)
            {
                if (s.IsSub || s.Total <= 0) continue;
                pick.Add(s);
                if (pick.Count >= CmpN) break;
            }
            if (pick.Count < 2) { exiting = true; Close(); return; }
            using (CompareForm f = new CompareForm(pick.ToArray()))
            {
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();
                var bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                f.Close();
            }
            exiting = true;
            Close();
        }

        public void FoldSim(string outPath)
        {
            Show();
            Application.DoEvents();
            WaitBuild(60);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int target = -1;
            for (int i = 0; i < shown.Count; i++)
            {
                SessionInfo s = shown[i];
                if (!s.IsSub && subCount.ContainsKey(s.Id) && subCount[s.Id] > 0) { target = i; break; }
            }
            sb.AppendLine("rows0=" + shown.Count + " targetRow=" + target + " groups=" + subCount.Count);
            if (target >= 0)
            {
                IntPtr h = grid.Handle;
                Rectangle cr = grid.GetCellDisplayRectangle(1, target, true);
                int gx = cr.X + cr.Width / 2, gy = cr.Y + cr.Height / 2;
                IntPtr lp = (IntPtr)((gy << 16) | gx);
                for (int k = 0; k < 2; k++)
                {
                    SendMessage(h, 0x0201, (IntPtr)1, lp);
                    SendMessage(h, 0x0202, (IntPtr)0, lp);
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(300);
                    Application.DoEvents();
                    sb.AppendLine("click" + (k + 1) + " rows=" + shown.Count + " expanded=" + expanded.Count);
                }
            }
            System.IO.File.WriteAllText(outPath, sb.ToString(), new System.Text.UTF8Encoding(true));
            exiting = true;
            Close();
        }

        public void SelSim(string outPath)
        {
            Show();
            Application.DoEvents();
            WaitBuild(60);
            var sb = new StringBuilder();
            string pid = null;
            foreach (var kv in subCount) if (kv.Value > 0) { pid = kv.Key; break; }
            if (chkChain != null) { chkChain.Checked = true; Application.DoEvents(); }
            int idx = -1;
            for (int i = 0; i < shown.Count; i++) if (shown[i].Id == pid) { idx = i; break; }
            sb.AppendLine("parent=" + pid + " row=" + idx + " chain=" + (chkChain != null && chkChain.Checked));
            if (idx >= 0) { ToggleCheck(idx); Application.DoEvents(); }
            for (int i = 0; i < shown.Count; i++)
            {
                if (i == idx || shown[i].IsSub) continue;
                ToggleCheck(i);
                break;
            }
            Application.DoEvents();
            sb.AppendLine("afterCheck sel=" + sel.Count + " checked=" + checkedIds.Count + " btn=" + btnCompare.Text);
            sb.AppendLine("lblSel=" + lblSel.Text);
            sb.AppendLine("lblTotalBig=" + lblTotalBig.Text);
            foldSubs = true;
            expanded.Clear();
            RefreshView(false);
            Application.DoEvents();
            sb.AppendLine("folded shown=" + shown.Count + " sel=" + sel.Count);
            sb.AppendLine("lblSel2=" + lblSel.Text);
            sb.AppendLine("lblTotalBig2=" + lblTotalBig.Text);
            System.IO.File.WriteAllText(outPath, sb.ToString(), new System.Text.UTF8Encoding(true));
            exiting = true;
            Close();
        }

        public void HdrSim(string outPath)
        {
            Show();
            Application.DoEvents();
            WaitBuild(60);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            IntPtr h = grid.Handle;
            int[] cols = new int[] { 4, 4, 7, 2 };
            foreach (int col in cols)
            {
                Rectangle r = grid.GetCellDisplayRectangle(col, -1, true);
                int gx = r.X + r.Width / 2, gy = r.Y + r.Height / 2;
                IntPtr lp = (IntPtr)((gy << 16) | gx);
                SendMessage(h, 0x0201, (IntPtr)1, lp);
                SendMessage(h, 0x0202, (IntPtr)0, lp);
                Application.DoEvents();
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();
                sb.AppendLine("click col=" + col + " rect=" + r + " -> sortCol=" + sortCol + " asc=" + sortAsc
                    + " head=" + grid.Columns[col].HeaderText);
                for (int i = 0; i < 3 && i < shown.Count; i++)
                    sb.AppendLine("   " + i + ") " + shown[i].Title + "  tok=" + TokOf(shown[i]) + "  cost=" + SessCost(shown[i]).ToString("F2"));
            }
            System.IO.File.WriteAllText(outPath, sb.ToString(), new System.Text.UTF8Encoding(true));
            exiting = true;
            Close();
        }

        public void ClickSim(string outPath)
        {
            Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(400);
            Application.DoEvents();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Rectangle cr = grid.GetCellDisplayRectangle(0, 0, false);
            int gx = cr.X + cr.Width / 2, gy = cr.Y + cr.Height / 2;
            sb.AppendLine("cell0=" + cr + " click=" + gx + "," + gy + " before=" + checkedIds.Count);
            IntPtr h = grid.Handle;
            grid.MouseDown += new MouseEventHandler(delegate { dbgDown++; });
            IntPtr lp = (IntPtr)((gy << 16) | gx);
            SendMessage(h, 0x0201, (IntPtr)1, lp);
            SendMessage(h, 0x0202, (IntPtr)0, lp);
            Application.DoEvents();
            System.Threading.Thread.Sleep(200);
            Application.DoEvents();
            sb.AppendLine("after1 checked=" + checkedIds.Count + " clickEvt=" + dbgClick + " downEvt=" + dbgDown + " | " + lblSel.Text + " | " + lblTotal.Text);
            SendMessage(h, 0x0201, (IntPtr)1, lp);
            SendMessage(h, 0x0202, (IntPtr)0, lp);
            Application.DoEvents();
            System.Threading.Thread.Sleep(200);
            Application.DoEvents();
            sb.AppendLine("after2 checked=" + checkedIds.Count);
            System.IO.File.WriteAllText(outPath, sb.ToString(), System.Text.Encoding.UTF8);
            Close();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        void SetupTray()
        {
            if (!cfg.Tray || tray != null) return;
            tray = new NotifyIcon();
            tray.Icon = System.Drawing.SystemIcons.Application;
            tray.Text = Program.AppTitle;
            var menu = new ContextMenu();
            menu.MenuItems.Add("打开", new EventHandler(delegate { ShowFromTray(); }));
            menu.MenuItems.Add("退出", new EventHandler(delegate { exiting = true; Close(); }));
            tray.ContextMenu = menu;
            tray.DoubleClick += new EventHandler(delegate { ShowFromTray(); });
            tray.Visible = true;
        }

        bool hotkeyOk = false;

        void SetupHotkey()
        {
            if (!cfg.Hotkey || SelfCheck) return;
            try { hotkeyOk = RegisterHotKey(Handle, 1, 0x0001 | 0x0002, (int)Keys.D); }
            catch { hotkeyOk = false; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (tray != null && !exiting && e.CloseReason != CloseReason.WindowsShutDown)
            {
                e.Cancel = true;
                Hide();
                if (!trayTipShown)
                {
                    trayTipShown = true;
                    try { tray.ShowBalloonTip(1500, Program.AppTitle, "已收进托盘，双击图标或按 Ctrl+Alt+D 打开。", ToolTipIcon.Info); }
                    catch { }
                }
                return;
            }
            if (tray != null) { try { tray.Visible = false; tray.Dispose(); } catch { } tray = null; }
            try { UnregisterHotKey(Handle, 1); } catch { }
            base.OnFormClosing(e);
        }

        public MainForm()
        {
            cfg = Config.Load();
            Peak.LoadBuiltin();
            foreach (string h in (cfg.HolidaysRaw == null ? "" : cfg.HolidaysRaw).Split(','))
            {
                string hs = h.Trim();
                if (hs.Length == 10) Peak.Holidays.Add(hs);
            }
            Pricing.OffMiss = cfg.PriceOffMiss; Pricing.OffHit = cfg.PriceOffHit; Pricing.OffOut = cfg.PriceOffOut;
            Pricing.PkMiss = cfg.PricePkMiss; Pricing.PkHit = cfg.PricePkHit; Pricing.PkOut = cfg.PricePkOut;
            listFont = new Font("微软雅黑", 9f);

            AutoScaleMode = AutoScaleMode.None;
            Text = Program.AppTitle;
            ClientSize = new Size(1200, 812);
            MinimumSize = new Size(1120, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ui.Bg;
            Font = new Font("微软雅黑", 9f);

            BuildGrid();

            BuildHead();
            BuildKpi();
            BuildFilter();
            BuildBottom();

            var status = new StatusStrip();
            status.BackColor = Color.FromArgb(245, 247, 250);
            status.SizingGrip = false;
            lblStatus = new ToolStripStatusLabel("");
            lblStatus.ForeColor = Color.FromArgb(124, 132, 148);
            status.Items.Add(lblStatus);

            HeadLine.Height = 1;
            HeadLine.Dock = DockStyle.Top;
            HeadLine.BackColor = Color.FromArgb(227, 231, 239);

            gridHost = new Panel();
            gridHost.Dock = DockStyle.Fill;
            gridHost.BackColor = Ui.Bg;
            gridHost.Padding = new Padding(16, 2, 16, 8);

            var gridCard = new Panel();
            gridCard.Dock = DockStyle.Fill;
            gridCard.BackColor = Color.White;
            gridCard.Paint += new PaintEventHandler(DrawCardBorder);
            gridCard.Controls.Add(grid);
            gridHost.Controls.Add(gridCard);

            Controls.Add(gridHost);
            Controls.Add(filterBar);
            Controls.Add(kpiBar);
            Controls.Add(HeadLine);
            Controls.Add(header);
            Controls.Add(footer);
            Controls.Add(status);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = cfg.IntervalSec * 1000;
            timer.Tick += new EventHandler(OnTick);

            Ui.ScaleTree(this);
            Ui.Buf(this);
            LayoutFilterRow();
            FitToScreen();
            if (DumpPath != null) Dump(DumpPath);
            Resize += new EventHandler(delegate
            {
                if (chips.Count > 0 && cmbKind != null && chkAuto != null && chkAuto.Parent != null)
                {
                    LayoutFilterRow();
                    SyncChips();
                }
            });
            Load += new EventHandler(OnFirstLoad);
        }

        // 窗口默认高度在 150% 缩放下会超过 1067 高的屏，先按工作区收一收
        void FitToScreen()
        {
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int frameW = Ui.S(16), frameH = Ui.S(39);
            int maxW = wa.Width - frameW - Ui.S(10);
            int maxH = wa.Height - frameH - Ui.S(10);
            if (maxW < MinimumSize.Width) maxW = wa.Width - frameW;
            if (maxH < MinimumSize.Height) maxH = wa.Height - frameH;
            if (ClientSize.Width > maxW || ClientSize.Height > maxH)
                ClientSize = new Size(Math.Min(ClientSize.Width, maxW), Math.Min(ClientSize.Height, maxH));
            Location = new Point(wa.Left + (wa.Width - (ClientSize.Width + frameW)) / 2,
                                 wa.Top + Math.Max(0, (wa.Height - (ClientSize.Height + frameH)) / 2));
        }

        public static string DumpPath = null;

        void Dump(string path)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("form=" + Width + "x" + Height + " client=" + ClientSize.Width + "x" + ClientSize.Height + " k=" + Ui.K);
            sb.AppendLine("filterBar=" + filterBar.Bounds + " h=" + filterBar.Height);
            foreach (Button b in chips) sb.AppendLine("chip[" + b.Text + "] " + b.Bounds + " reg=" + (b.Region != null) + " vis=" + b.Visible);
            var arr = new Control[] { lblKind, cmbKind, chkLow, chkChain, btnDist, chkAuto, chkAuto.Parent, txt };
            foreach (Control c in arr)
                if (c != null) sb.AppendLine("ctl[" + (c.Text == null ? "" : c.Text) + "] " + c.Bounds + " vis=" + c.Visible + " fs=" + (c.Font == null ? 0 : c.Font.Size));
            Control card = filterBar.Controls.Count > 0 ? filterBar.Controls[0] : null;
            if (card != null) sb.AppendLine("card=" + card.Bounds);
            sb.AppendLine("grid=" + grid.Bounds + " host=" + (grid.Parent == null ? "?" : grid.Parent.Bounds.ToString()));
            System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
        }

        void LayoutFilterRow()
        {
            cmbKind.FitWidth();
            ((FlatCheck)chkLow).FitWidth();
            ((FlatCheck)chkChain).FitWidth();

            int cardW = ClientSize.Width - Ui.S(32);
            int y = Ui.S(44);
            int x = Ui.S(14);
            lblKind.Location = new Point(x, y + Ui.S(5));
            lblKind.BackColor = Color.White;
            x += lblKind.Width + Ui.S(8);

            cmbKind.Location = new Point(x, y);
            x += cmbKind.Width + Ui.S(14);


            chkLow.Location = new Point(x, y);
            x += chkLow.Width + Ui.S(10);

            chkChain.Location = new Point(x, y);
            x += chkChain.Width + Ui.S(12);

            btnDist.Location = new Point(x, y + Ui.S(1));

            // 右侧：row1 放自动刷新 + 间隔，row2 放全选 + 清空（自己排，不依赖 FlowLayoutPanel）
            int rx = cardW - Ui.S(12);
            int y1 = Ui.S(9);
            chkAuto.Location = new Point(rx - chkAuto.Width, y1);
            rx = chkAuto.Left - Ui.S(10);
            numInterval.Location = new Point(rx - numInterval.Width, y1);
            rx = numInterval.Left - Ui.S(10);

            int rx2 = cardW - Ui.S(12);
            int y2 = Ui.S(44);
            btnClear.Location = new Point(rx2 - btnClear.Width, y2);
            rx2 = btnClear.Left - Ui.S(8);
            btnAll.Location = new Point(rx2 - btnAll.Width, y2);
            rx2 = btnAll.Left - Ui.S(8);
            if (btnFold != null) btnFold.Location = new Point(rx2 - btnFold.Width, y2);
        }

        Panel HeadLine = new Panel();

        void BuildHead()
        {
            header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 62;
            header.BackColor = Ui.Bg;

            Label t1 = new Label();
            t1.Text = "会话用量";
            t1.AutoSize = true;
            t1.Font = Ui.FB(15f);
            t1.ForeColor = Ui.Ink;
            t1.BackColor = Ui.Bg;
            t1.Location = new Point(18, 9);

            Label t2 = new Label();
            t2.Text = "读取 ~/.dsh 会话缓存与原始记录 · 峰谷分时计价 · 主对话与子智能体分开计算";
            t2.AutoSize = true;
            t2.Font = Ui.F(8.5f);
            t2.ForeColor = Ui.Ink3;
            t2.BackColor = Ui.Bg;
            t2.Location = new Point(20, 36);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Right;
            flow.Width = 352;
            flow.FlowDirection = FlowDirection.RightToLeft;
            flow.WrapContents = false;
            flow.BackColor = Ui.Bg;
            flow.Padding = new Padding(0, 17, 18, 0);

            btnRefresh = Ui.Primary("刷新", 88, 30);
            btnRefresh.Click += new EventHandler(delegate { Reload(true); });

            btnSettings = Ui.Ghost("设置", 76, 30);
            btnSettings.Click += new EventHandler(delegate { ShowSettings(); });

            btnHelp = Ui.Ghost("使用教程", 100, 30);
            btnHelp.Click += new EventHandler(delegate { ShowGuide(false); });

            flow.Controls.Add(btnRefresh);
            flow.Controls.Add(btnSettings);
            flow.Controls.Add(btnHelp);

            header.Controls.Add(t1);
            header.Controls.Add(t2);
            header.Controls.Add(flow);
        }

        DataGridViewTextBoxColumn MakeCol(string head, int width, DataGridViewAutoSizeColumnMode mode, float weight, DataGridViewContentAlignment align)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = head;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.ReadOnly = true;
            c.AutoSizeMode = mode;
            if (mode == DataGridViewAutoSizeColumnMode.None) c.Width = width;
            else c.FillWeight = weight;
            c.DefaultCellStyle.Alignment = align;
            c.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            c.HeaderCell.Style.Alignment = align;
            return c;
        }

        void BuildGrid()
        {
            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Color.White;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToOrderColumns = false;
            grid.ReadOnly = true;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.ColumnHeadersHeight = Ui.S(34);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.RowTemplate.Height = Ui.S(36);
            grid.ScrollBars = ScrollBars.Vertical;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Ui.Line2;
            grid.EnableHeadersVisualStyles = false;

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 246, 250);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(108, 117, 135);
            grid.ColumnHeadersDefaultCellStyle.Font = Ui.F(9f);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(244, 246, 250);

            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Ui.Ink;
            grid.DefaultCellStyle.Font = Ui.F(9f);
            grid.DefaultCellStyle.SelectionBackColor = Ui.RowSel;
            grid.DefaultCellStyle.SelectionForeColor = Ui.Ink;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(252, 253, 255);

            grid.Columns.Add(MakeCol("", Ui.S(34), DataGridViewAutoSizeColumnMode.None, 0, DataGridViewContentAlignment.MiddleLeft));
            grid.Columns.Add(MakeCol("主/子", Ui.S(64), DataGridViewAutoSizeColumnMode.None, 0, DataGridViewContentAlignment.MiddleCenter));
            grid.Columns.Add(MakeCol("会话", 0, DataGridViewAutoSizeColumnMode.Fill, 100, DataGridViewContentAlignment.MiddleLeft));
            grid.Columns.Add(MakeCol("归属 / 项目", 0, DataGridViewAutoSizeColumnMode.Fill, 62, DataGridViewContentAlignment.MiddleLeft));
            grid.Columns.Add(MakeCol("总 token", Ui.S(92), DataGridViewAutoSizeColumnMode.None, 0, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(MakeCol("闲时", Ui.S(78), DataGridViewAutoSizeColumnMode.None, 0, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(MakeCol("高峰", Ui.S(78), DataGridViewAutoSizeColumnMode.None, 0, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(MakeCol("费用", Ui.S(104), DataGridViewAutoSizeColumnMode.None, 0, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(MakeCol("最后活动", Ui.S(100), DataGridViewAutoSizeColumnMode.None, 0, DataGridViewContentAlignment.MiddleRight));

            grid.CellPainting += new DataGridViewCellPaintingEventHandler(GridCellPaint);
            grid.MouseDown += new MouseEventHandler(GridMouseDown);
            grid.CellMouseClick += new DataGridViewCellMouseEventHandler(GridCellMouseClick);
            grid.CellDoubleClick += new DataGridViewCellEventHandler(delegate(object s2, DataGridViewCellEventArgs e2) { ShowTurns(); });
            grid.ColumnHeaderMouseClick += new DataGridViewCellMouseEventHandler(GridHeaderClick);
            grid.CellMouseEnter += new DataGridViewCellEventHandler(GridHover);
            grid.MouseLeave += new EventHandler(delegate { hoverRow = -1; grid.Cursor = Cursors.Default; grid.Invalidate(); lblStatus.Text = scopeText; });

            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("查看逐轮明细", new EventHandler(delegate { ShowTurns(); }));
            menu.MenuItems.Add("打开项目目录", new EventHandler(delegate { OpenProjectDir(); }));
            menu.MenuItems.Add("只看这个项目", new EventHandler(delegate { FilterByProject(); }));
            menu.MenuItems.Add("复制这条会话", new EventHandler(delegate { CopyOneSession(); }));
            grid.ContextMenu = menu;
        }

        void BuildKpi()
        {
            kpiBar = new Panel();
            kpiBar.Dock = DockStyle.Top;
            kpiBar.Height = 98;
            kpiBar.BackColor = Ui.Bg;
            kpiBar.Padding = new Padding(16, 0, 16, 8);

            TableLayoutPanel tbl = new TableLayoutPanel();
            tbl.Dock = DockStyle.Fill;
            tbl.ColumnCount = 4;
            tbl.RowCount = 1;
            tbl.BackColor = Ui.Bg;
            for (int i = 0; i < 4; i++) tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            string[] ks = new string[] { "总 token（全量）", "本月花费", "会话数", "高峰占比" };
            Label[] vs = new Label[4];
            Label[] ns = new Label[4];
            for (int i = 0; i < 4; i++)
            {
                Panel card = new Panel();
                card.Dock = DockStyle.Fill;
                card.Margin = new Padding(0, 0, 8, 0);
                card.BackColor = Ui.Bg;
                card.Paint += new PaintEventHandler(KpiCardPaint);

                Label k = new Label();
                k.Text = ks[i];
                k.AutoSize = true;
                k.Font = Ui.F(9f);
                k.ForeColor = Ui.Ink3;
                k.BackColor = Color.White;
                k.Location = new Point(14, 10);

                Label v = new Label();
                v.Text = "—";
                v.AutoSize = true;
                v.Font = Ui.FB(15f);
                v.ForeColor = Ui.Ink;
                v.BackColor = Color.White;
                v.Location = new Point(12, 27);

                Label n = new Label();
                n.Text = "";
                n.AutoSize = true;
                n.Font = Ui.F(8.5f);
                n.ForeColor = Ui.Ink3;
                n.BackColor = Color.White;
                n.Location = new Point(14, 60);

                card.Controls.Add(k);
                card.Controls.Add(v);
                card.Controls.Add(n);
                tbl.Controls.Add(card, i, 0);
                vs[i] = v;
                ns[i] = n;
            }
            kpi1v = vs[0]; kpi2v = vs[1]; kpi3v = vs[2]; kpi4v = vs[3];
            kpi1n = ns[0]; kpi2n = ns[1]; kpi3n = ns[2]; kpi4n = ns[3];
            kpiBar.Controls.Add(tbl);
        }

        string[] chipTexts = new string[] { "全部", "今天", "近 7 天", "近 30 天", "自定义时段…" };

        void BuildFilter()
        {
            filterBar = new Panel();
            filterBar.Dock = DockStyle.Top;
            filterBar.Height = 96;
            filterBar.BackColor = Ui.Bg;
            filterBar.Padding = new Padding(16, 0, 16, 8);

            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.BackColor = Color.White;
            card.Paint += new PaintEventHandler(FlatCardPaint);

            Panel box = new Panel();
            box.Location = new Point(12, 8);
            box.Size = new Size(252, 30);
            box.BackColor = Color.White;
            box.Paint += new PaintEventHandler(SearchBoxPaint);
            box.Cursor = Cursors.IBeam;

            Label ph = new Label();
            ph.Text = "搜索会话 / 项目路径";
            ph.AutoSize = true;
            ph.Font = Ui.F(9f);
            ph.ForeColor = Ui.Ink3;
            ph.BackColor = Color.White;
            ph.Location = new Point(32, 7);
            ph.Cursor = Cursors.IBeam;

            txt = new TextBox();
            txt.BorderStyle = BorderStyle.None;
            txt.Font = Ui.F(9f);
            txt.ForeColor = Ui.Ink;
            txt.BackColor = Color.White;
            txt.Location = new Point(32, 6);
            txt.Width = 208;
            txt.TextChanged += new EventHandler(delegate
            {
                ph.Visible = txt.Text.Length == 0;
                RefreshView(true);
            });

            box.Controls.Add(txt);
            box.Controls.Add(ph);
            box.Click += new EventHandler(delegate { txt.Focus(); });
            ph.Click += new EventHandler(delegate { txt.Focus(); });
            card.Controls.Add(box);

            int x = 276;
            for (int i = 0; i < chipTexts.Length; i++)
            {
                Button b = Ui.Ghost(chipTexts[i], TextRenderer.MeasureText(chipTexts[i], Ui.F(9f)).Width + 26, 28);
                b.Location = new Point(x, 9);
                Ui.RoundControl(b, 14);
                int idx = i;
                b.Click += new EventHandler(delegate { PickTime(idx); });
                chips.Add(b);
                card.Controls.Add(b);
                x += b.Width + 6;
            }

            btnClear = Ui.Ghost("清空", 58, 28);
            btnClear.Click += new EventHandler(delegate { ClearChecks(); });

            btnAll = Ui.Ghost("全选", 58, 28);
            btnAll.Click += new EventHandler(delegate { CheckAll(); });

            btnFold = Ui.Ghost("展开全部", 76, 28);
            btnFold.Click += new EventHandler(delegate
            {
                bool anyFolded = false;
                foreach (KeyValuePair<string, int> kv in subCount)
                    if (!expanded.Contains(kv.Key)) { anyFolded = true; break; }
                if (anyFolded)
                {
                    foreach (KeyValuePair<string, int> kv in subCount) expanded.Add(kv.Key);
                }
                else expanded.Clear();
                RefreshView(false);
            });

            Label lblSec = new Label();
            lblSec.Text = "秒";
            lblSec.AutoSize = true;
            lblSec.Visible = false;
            lblSec.ForeColor = Ui.Ink3;
            lblSec.Margin = new Padding(2, 7, 2, 0);

            numInterval = new SpinBox();
            numInterval.Width = 68;
            numInterval.Unit = "秒";
            numInterval.Height = 30;
            numInterval.Font = Ui.F(9f);
            numInterval.Minimum = 3;
            numInterval.Maximum = 600;
            numInterval.Value = Math.Max(3, Math.Min(600, cfg.IntervalSec));
            numInterval.Margin = new Padding(2, 4, 2, 0);
            numInterval.ValueChanged += new EventHandler(delegate
            {
                timer.Interval = numInterval.Value * 1000;
                cfg.IntervalSec = numInterval.Value;
            });

            chkAuto = new FlatCheck();
            chkAuto.Text = "自动刷新";
            chkAuto.Size = new Size(90, 30);
            chkAuto.Checked = cfg.AutoRefresh;
            chkAuto.Margin = new Padding(2, 4, 2, 0);
            chkAuto.CheckedChanged += new EventHandler(OnAutoChanged);

            card.Controls.Add(btnClear);
            card.Controls.Add(btnAll);
            card.Controls.Add(btnFold);
            card.Controls.Add(numInterval);
            card.Controls.Add(chkAuto);

            Label lk = new Label();
            lk.Text = "类型";
            lk.AutoSize = true;
            lk.ForeColor = Ui.Ink3;
            lk.BackColor = Color.White;
            lk.Location = new Point(14, 49);
            lblKind = lk;

            cmbKind = new FlatCombo();
            cmbKind.Font = Ui.F(9f);
            cmbKind.Location = new Point(48, 44);
            cmbKind.Size = new Size(116, 30);
            cmbKind.Fill("全部会话", "只看主对话", "只看子智能体");
            cmbKind.SelectedIndex = 0;
            cmbKind.SelectedIndexChanged += new EventHandler(delegate { RefreshView(true); });

            chkLow = new FlatCheck();
            chkLow.Text = "只看低命中";
            chkLow.Size = new Size(100, 30);
            chkLow.Location = new Point(398, 44);
            chkLow.CheckedChanged += new EventHandler(delegate { RefreshView(true); });

            chkChain = new FlatCheck();
            chkChain.Text = "连带子体";
            chkChain.Size = new Size(94, 30);
            chkChain.Location = new Point(508, 44);
            chkChain.CheckedChanged += new EventHandler(delegate { UpdateTotal(); });

            btnDist = Ui.Ghost("时段分布", 84, 28);
            btnDist.Location = new Point(612, 45);
            btnDist.Click += new EventHandler(delegate { ShowDist(); });

            lblUpdated = new Label();
            lblUpdated.Text = "";
            lblUpdated.Dock = DockStyle.Bottom;
            lblUpdated.Height = 22;
            lblUpdated.TextAlign = ContentAlignment.MiddleRight;
            lblUpdated.Padding = new Padding(0, 0, 14, 4);
            lblUpdated.Font = Ui.F(8.5f);
            lblUpdated.ForeColor = Ui.Ink3;
            lblUpdated.BackColor = Color.White;

            card.Controls.Add(lk);
            card.Controls.Add(cmbKind);
            card.Controls.Add(chkLow);
            card.Controls.Add(chkChain);
            card.Controls.Add(btnDist);
            card.Controls.Add(lblUpdated);
            filterBar.Controls.Add(card);
        }

        void DrawCardBorder(object sender, PaintEventArgs e)
        {
            Panel p = (Panel)sender;
            Ui.DrawCard(e.Graphics, new Rectangle(0, 0, p.Width - 1, p.Height - 1), Color.White, Ui.Line, 8);
        }

        void GridCellPaint(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (hoverRow == e.RowIndex && e.ColumnIndex != 0 && e.ColumnIndex != 7 && !grid.Rows[e.RowIndex].Selected)
            {
                e.PaintBackground(e.CellBounds, false);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(250, 252, 255)))
                    e.Graphics.FillRectangle(b, e.CellBounds);
                e.PaintContent(e.CellBounds);
                e.Handled = true;
                return;
            }

            if (e.ColumnIndex == 0)
            {
                e.PaintBackground(e.CellBounds, true);
                if (hoverRow == e.RowIndex && !grid.Rows[e.RowIndex].Selected)
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(250, 252, 255)))
                        e.Graphics.FillRectangle(hb, e.CellBounds);
                SessionInfo s = grid.Rows[e.RowIndex].Tag as SessionInfo;
                bool on = s != null && checkedIds.ContainsKey(s.Id);
                int bx = e.CellBounds.X + (e.CellBounds.Width - Ui.S(14)) / 2;
                int by = e.CellBounds.Y + (e.CellBounds.Height - Ui.S(14)) / 2;
                using (GraphicsPath path = Ui.RoundPath(new Rectangle(bx, by, Ui.S(14), Ui.S(14)), Ui.S(3)))
                {
                    using (SolidBrush b = new SolidBrush(on ? Ui.Accent : Color.White)) e.Graphics.FillPath(b, path);
                    using (Pen pen = new Pen(on ? Ui.Accent : (hoverRow == e.RowIndex ? Ui.AccentLine : Color.FromArgb(206, 212, 224)), 1f)) e.Graphics.DrawPath(pen, path);
                }
                if (on)
                {
                    using (Pen pen = new Pen(Color.White, 1.6f))
                    {
                        e.Graphics.DrawLine(pen, bx + Ui.S(3.5f), by + Ui.S(7f), bx + Ui.S(6f), by + Ui.S(9.5f));
                        e.Graphics.DrawLine(pen, bx + Ui.S(6f), by + Ui.S(9.5f), bx + Ui.S(10.5f), by + Ui.S(4.5f));
                    }
                }
                e.Handled = true;
                return;
            }

            if (e.ColumnIndex == 1)
            {
                e.PaintBackground(e.CellBounds, true);
                if (hoverRow == e.RowIndex && !grid.Rows[e.RowIndex].Selected)
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(250, 252, 255)))
                        e.Graphics.FillRectangle(hb, e.CellBounds);
                e.PaintContent(e.CellBounds);
                SessionInfo s1 = grid.Rows[e.RowIndex].Tag as SessionInfo;
                int kids = (s1 != null && subCount.ContainsKey(s1.Id)) ? subCount[s1.Id] : 0;
                if (kids > 0)
                {
                    int cx = e.CellBounds.X + Ui.S(8);
                    int cy = e.CellBounds.Y + e.CellBounds.Height / 2;
                    using (SolidBrush b = new SolidBrush(Ui.Accent))
                    {
                        if (expanded.Contains(s1.Id))
                            e.Graphics.FillPolygon(b, new Point[] {
                                new Point(cx - Ui.S(5), cy - Ui.S(3)),
                                new Point(cx + Ui.S(5), cy - Ui.S(3)),
                                new Point(cx, cy + Ui.S(4)) });
                        else
                            e.Graphics.FillPolygon(b, new Point[] {
                                new Point(cx - Ui.S(3), cy - Ui.S(5)),
                                new Point(cx - Ui.S(3), cy + Ui.S(5)),
                                new Point(cx + Ui.S(4), cy) });
                    }
                }
                e.Handled = true;
                return;
            }

            if (e.ColumnIndex == 7)
            {
                e.PaintBackground(e.CellBounds, true);
                if (hoverRow == e.RowIndex && !grid.Rows[e.RowIndex].Selected)
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(250, 252, 255)))
                        e.Graphics.FillRectangle(hb, e.CellBounds);
                SessionInfo s = grid.Rows[e.RowIndex].Tag as SessionInfo;
                double c = s == null ? 0 : SessCost(s);
                if (c > 0 && maxRowCost > 0)
                {
                    int w = (int)Math.Round((e.CellBounds.Width - Ui.S(12)) * Math.Min(1.0, c / maxRowCost));
                    if (w < 3) w = 3;
                    Rectangle r = new Rectangle(e.CellBounds.Right - w - Ui.S(5), e.CellBounds.Y + Ui.S(9), w, e.CellBounds.Height - Ui.S(18));
                    using (GraphicsPath path = Ui.RoundPath(r, Ui.S(3)))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 244, 254))) e.Graphics.FillPath(b, path);
                }
                e.PaintContent(e.CellBounds);
                e.Handled = true;
            }
        }

        void KpiCardPaint(object sender, PaintEventArgs e)
        {
            Panel p = (Panel)sender;
            Ui.DrawCard(e.Graphics, new Rectangle(1, 1, p.Width - 3, p.Height - 3), Color.White, Ui.Line, 8);
        }

        void FlatCardPaint(object sender, PaintEventArgs e)
        {
            Panel p = (Panel)sender;
            Ui.DrawCard(e.Graphics, new Rectangle(1, 1, p.Width - 3, p.Height - 3), Color.White, Ui.Line, 8);
        }

        void SearchBoxPaint(object sender, PaintEventArgs e)
        {
            Panel p = (Panel)sender;
            Ui.DrawCard(e.Graphics, new Rectangle(0, 0, p.Width - 1, p.Height - 1), Color.White, Ui.Line, Ui.S(6));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Color.FromArgb(166, 174, 189), 1.4f))
            {
                e.Graphics.DrawEllipse(pen, Ui.S(10), Ui.S(9), Ui.S(9), Ui.S(9));
                e.Graphics.DrawLine(pen, Ui.S(18), Ui.S(17), Ui.S(22), Ui.S(21));
            }
        }

        string SortLabel()
        {
            string[] names = { "列", "列", "会话名", "项目", "总 token", "闲时", "高峰", "费用", "最后活动" };
            int i = sortCol;
            if (i < 0 || i >= names.Length) i = 8;
            return names[i] + (sortAsc ? " 升序" : " 降序");
        }

        void RefreshHeaders()
        {
            string[] heads = new string[] { "", "主/子", "会话", "归属 / 项目", "总 token", "闲时", "高峰", "费用", "最后活动" };
            for (int i = 0; i < heads.Length; i++)
                grid.Columns[i].HeaderText = heads[i] + (sortCol == i ? (sortAsc ? "  ▲" : "  ▼") : "");
        }

        void SyncChips()
        {
            int x = Ui.S(276);
            for (int i = 0; i < chips.Count; i++)
            {
                Button b = chips[i];
                bool on = (i == 4) ? (timeIdx == 4) : (timeIdx == i && rangeA == null);
                b.BackColor = on ? Ui.AccentSoft : Color.White;
                b.ForeColor = on ? Ui.Accent : Ui.Ink2;
                b.FlatAppearance.BorderColor = on ? Ui.AccentLine : Ui.Line;
                b.Font = on ? Ui.FB(9f) : Ui.F(9f);
                string label = chipTexts[i];
                if (i == 4 && timeIdx == 4 && rangeA != null)
                    label = string.Format("自定义 {0} ~ {1}", rangeA.Value.ToString("MM-dd HH:mm"), rangeB.Value.ToString("HH:mm"));
                b.Text = label;
                b.Width = Ui.S(TextRenderer.MeasureText(label, b.Font).Width + (i == 4 ? 36 : 26));
                b.Location = new Point(x, Ui.S(9));
                Ui.RoundControl(b, 14);
                x += b.Width + Ui.S(6);
            }
        }

        void PickTime(int idx)
        {
            if (idx == 4)
            {
                using (RangeForm dlg = new RangeForm(rangeA, rangeB))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) { SyncChips(); return; }
                    rangeA = dlg.StartAt;
                    rangeB = dlg.EndAt;
                }
                timeIdx = 4;
            }
            else
            {
                timeIdx = idx;
                rangeA = null;
                rangeB = null;
            }
            SyncChips();
            RefreshView(true);
        }

        void ClearChecks()
        {
            checkedIds.Clear();
            RefreshView(true);
        }

        void CheckAll()
        {
            foreach (SessionInfo s in shown) checkedIds[s.Id] = true;
            RefreshView(true);
        }

        void CompareSelected()
        {
            List<SessionInfo> pick = new List<SessionInfo>();
            foreach (SessionInfo s in all)
                if (checkedIds.ContainsKey(s.Id)) pick.Add(s);
            if (pick.Count < 2)
            {
                MessageBox.Show("请先在行首方框里勾选至少两个会话（想比几个就勾几个），再点【对比】。", "会话对比");
                return;
            }
            using (CompareForm f = new CompareForm(pick.ToArray()))
                f.ShowDialog(this);
        }

        void ToggleCheck(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            SessionInfo s = grid.Rows[rowIndex].Tag as SessionInfo;
            if (s == null) return;
            if (checkedIds.ContainsKey(s.Id)) checkedIds.Remove(s.Id); else checkedIds[s.Id] = true;
            DataGridViewRow row = grid.Rows[rowIndex];
            Color baseC = (rowIndex % 2 == 1) ? Color.FromArgb(252, 253, 255) : Color.White;
            row.DefaultCellStyle.BackColor = checkedIds.ContainsKey(s.Id) ? Ui.RowSel : baseC;
            grid.InvalidateRow(rowIndex);
            UpdateTotal();
        }

        void GridCellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            dbgClick++;
        }

        void GridMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            DataGridView.HitTestInfo h = grid.HitTest(e.X, e.Y);
            if (h.Type == DataGridViewHitTestType.Cell && h.ColumnIndex == 0 && h.RowIndex >= 0)
                ToggleCheck(h.RowIndex);
            if (h.Type == DataGridViewHitTestType.Cell && h.ColumnIndex == 1 && h.RowIndex >= 0)
            {
                SessionInfo s = grid.Rows[h.RowIndex].Tag as SessionInfo;
                if (s != null && !s.IsSub && subCount.ContainsKey(s.Id) && subCount[s.Id] > 0)
                {
                    ToggleFold(s.Id);
                    return;
                }
            }
        }
        int dbgClick, dbgDown;

        void GridHeaderClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 2) return;
            if (sortCol == e.ColumnIndex) sortAsc = !sortAsc;
            else { sortCol = e.ColumnIndex; sortAsc = (e.ColumnIndex == 2 || e.ColumnIndex == 3); }
            topRow = 0;
            RefreshView(false);
        }

        void GridHover(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                // 表头：可排序列给手型，让人知道能点
                grid.Cursor = (e.ColumnIndex >= 2) ? Cursors.Hand : Cursors.Default;
                return;
            }
            if (e.RowIndex >= shown.Count) return;
            SessionInfo hovS = shown[e.RowIndex];
            bool canFold = e.ColumnIndex == 1 && hovS != null && !hovS.IsSub
                && subCount.ContainsKey(hovS.Id) && subCount[hovS.Id] > 0;
            grid.Cursor = (e.ColumnIndex == 0 || canFold) ? Cursors.Hand : Cursors.Default;
            if (hoverRow != e.RowIndex) { hoverRow = e.RowIndex; grid.Invalidate(); }
            SessionInfo it = shown[e.RowIndex];
            string sid = it.IsSub ? "子智能体" : "主对话";
            if (it.IsSub && it.ParentTitle.Length > 0) sid += "（所属：" + it.ParentTitle + "）";
            string costTxt = "—";
            double c = SessCost(it);
            if (c > 0) costTxt = string.Format("¥{0:F2}", c);
            long offT = 0, pkT = 0;
            Timeline et = Eff(it);
            if (et != null)
            {
                offT = et.OffU + et.OffC + et.OffO;
                pkT = et.PkU + et.PkC + et.PkO;
            }
            lblStatus.Text = string.Format("{0}  |  {1} 轮  |  闲时 {2} / 高峰 {3}  |  命中 {4}%  |  {5}{6}  |  {7}",
                sid, it.Turns, Fmt(offT), Fmt(pkT),
                it.Total > 0 ? (int)((double)it.Cache / it.Total * 100) : 0,
                costTxt, it.Approx ? "≈" : "", it.Cwd);
        }

        string OwnLabel(SessionInfo s)
        {
            if (s.IsSub && s.ParentTitle.Length > 0) return "← " + s.ParentTitle;
            if (string.IsNullOrEmpty(s.Cwd)) return "—";
            string t = s.Cwd.TrimEnd('\\', '/');
            string[] parts = t.Split(new char[] { '\\', '/' });
            if (parts.Length == 0) return s.Cwd;
            string leaf = parts[parts.Length - 1];
            if (leaf == "default-workspace" && parts.Length >= 2) return parts[parts.Length - 2];
            return leaf;
        }

        static long TokOf(SessionInfo s)
        {
            Timeline t = s.RangeTl != null ? s.RangeTl : s.Tl;
            if (t == null) return s.Total;
            long v = t.TotalTokens();
            return v > 0 ? v : s.Total;
        }

        static double OffCostOf(SessionInfo s)
        {
            Timeline t = s.RangeTl != null ? s.RangeTl : s.Tl;
            return t == null ? 0 : t.OffCost();
        }

        static double PkCostOf(SessionInfo s)
        {
            Timeline t = s.RangeTl != null ? s.RangeTl : s.Tl;
            return t == null ? 0 : t.PkCost();
        }

        static string FmtBig(long n)
        {
            if (n >= 1000000) return string.Format("{0:N1}M", n / 1000000.0);
            if (n >= 1000) return string.Format("{0:N1}K", n / 1000.0);
            return n.ToString();
        }

        void UpdateKpi()
        {
            long tokAll = 0, tokMain = 0, tokSub = 0, pk = 0, offTok = 0;
            double pkCost = 0, offCost = 0;
            int mainN = 0, subN = 0, emptyN = 0;
            lock (all)
            {
                foreach (SessionInfo s in all)
                {
                    if (s.IsSub) subN++; else mainN++;
                    Timeline t = s.Tl;
                    if (t == null) { emptyN++; continue; }
                    long v = t.TotalTokens();
                    tokAll += v;
                    if (s.IsSub) tokSub += v; else tokMain += v;
                    pk += t.PkU + t.PkC + t.PkO;
                    offTok += t.OffU + t.OffC + t.OffO;
                    pkCost += t.PkCost();
                    offCost += t.OffCost();
                    if (v <= 0) emptyN++;
                }
            }
            kpi1v.Text = FmtBig(tokAll);
            kpi1n.Text = string.Format("主 {0} / 子 {1}", FmtBig(tokMain), FmtBig(tokSub));

            kpi2v.Text = string.Format("¥{0:F2}", monthCost);
            if (cfg.Budget > 0)
            {
                double pct = monthCost / cfg.Budget * 100.0;
                kpi2n.Text = string.Format("预算 ¥{0:F0} · 已用 {1:F0}%", cfg.Budget, pct);
                kpi2v.ForeColor = pct >= 100 ? Color.FromArgb(200, 40, 40) : (pct >= 80 ? Color.FromArgb(200, 125, 20) : Ui.Ink);
            }
            else
            {
                kpi2n.Text = "未设预算（可在设置里填）";
                kpi2v.ForeColor = Ui.Ink;
            }

            kpi3v.Text = all.Count + " 个";
            kpi3n.Text = string.Format("主 {0} · 子 {1}{2}", mainN, subN, emptyN > 0 ? " · " + emptyN + " 个无 token" : "");

            long tot = offTok + pk;
            int pkPct = tot > 0 ? (int)((double)pk / tot * 100.0) : 0;
            kpi4v.Text = pkPct + " %";
            kpi4n.Text = string.Format("高峰 ¥{0:F2} · 闲时 ¥{1:F2}", pkCost, offCost);
        }

        SessionInfo IndexedSession()
        {
            int idx = -1;
            if (grid.CurrentCell != null) idx = grid.CurrentCell.RowIndex;
            if (idx < 0) idx = hoverRow;
            if (idx < 0 || idx >= shown.Count) return null;
            return shown[idx];
        }

        void ShowTurns()
        {
            SessionInfo s = IndexedSession();
            if (s == null) return;
            if (rawIndex.Count == 0) rawIndex = Raw.Index(Raw.Root(cfg.DataDir));
            string rawPath;
            if (!rawIndex.TryGetValue(s.Id, out rawPath) || !File.Exists(rawPath))
            {
                MessageBox.Show("这个会话没有原始记录，只能看总量。\n（原始文件缺失时，明细无法还原）", "逐轮明细");
                return;
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                lblStatus.Text = "正在解析逐轮明细…";
                var turns = Raw.ParseTurns(rawPath);
                Cursor = Cursors.Default;
                if (turns.Count == 0)
                {
                    MessageBox.Show("原始记录里没找到带 usage 的助手消息。", "逐轮明细");
                    lblStatus.Text = "";
                    return;
                }
                lblStatus.Text = string.Format("已解析 {0} 轮", turns.Count);
                using (var dlg = new TurnForm(s, turns)) dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show("解析失败：" + ex.Message, "逐轮明细");
            }
        }

        void OpenProjectDir()
        {
            SessionInfo s = IndexedSession();
            if (s == null) return;
            if (string.IsNullOrEmpty(s.Cwd) || !Directory.Exists(s.Cwd))
            {
                lblStatus.Text = "这个会话没有可打开的项目目录";
                return;
            }
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + s.Cwd + "\""); }
            catch (Exception ex) { lblStatus.Text = "打开失败：" + ex.Message; }
        }

        void FilterByProject()
        {
            SessionInfo s = IndexedSession();
            if (s == null) return;
            if (string.IsNullOrEmpty(s.Cwd))
            {
                lblStatus.Text = "这个会话没有项目目录信息";
                return;
            }
            string t = s.Cwd.TrimEnd('\\', '/');
            string[] parts = t.Split(new char[] { '\\', '/' });
            txt.Text = parts.Length > 0 ? parts[parts.Length - 1] : t;
            lblStatus.Text = "已按项目过滤：" + txt.Text;
        }

        void CopyOneSession()
        {
            SessionInfo s = IndexedSession();
            if (s == null) return;
            long offT = 0, pkT = 0;
            if (s.Tl != null) { offT = s.Tl.OffTokens(); pkT = s.Tl.PkTokens(); }
            string txt2 = string.Format(
                "{0}\r\n总 token {1:N0}（{2}）\r\n未命中 {3:N0} + 缓存命中 {4:N0}（{5}%）+ 输出 {6:N0}\r\n闲时 {7} / 高峰 {8}\r\n等价价 ¥{9:F2}\r\n最后活动 {10}\r\n项目 {11}",
                s.Title, s.Total, Fmt(s.Total), s.Uncached, s.Cache,
                s.Total > 0 ? (int)((double)s.Cache / s.Total * 100) : 0, s.OutN,
                Fmt(offT), Fmt(pkT), SessCost(s), s.Last.ToString("yyyy-MM-dd HH:mm"), s.Cwd);
            try { Clipboard.SetText(txt2); lblStatus.Text = "这条会话的明细已复制"; }
            catch { lblStatus.Text = "复制失败（剪贴板被占用）"; }
        }

        void BuildBottom()
        {
            footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 96;
            footer.BackColor = Color.White;

            Panel line = new Panel();
            line.Height = 1;
            line.Dock = DockStyle.Top;
            line.BackColor = Ui.Line;

            lblSel = new Label();
            lblSel.Text = "已选 0 个会话";
            lblSel.AutoSize = true;
            lblSel.Font = Ui.F(8.5f);
            lblSel.ForeColor = Ui.Ink3;
            lblSel.BackColor = Color.White;
            lblSel.Location = new Point(18, 12);

            lblTotalBig = new Label();
            lblTotalBig.Text = "0 token";
            lblTotalBig.AutoSize = true;
            lblTotalBig.Font = Ui.FB(14f);
            lblTotalBig.ForeColor = Ui.Ink;
            lblTotalBig.BackColor = Color.White;
            lblTotalBig.Location = new Point(16, 30);

            lblDetail = new Label();
            lblDetail.Text = "";
            lblDetail.AutoSize = true;
            lblDetail.Font = Ui.F(8.5f);
            lblDetail.ForeColor = Ui.Ink3;
            lblDetail.BackColor = Color.White;
            lblDetail.Location = new Point(18, 62);

            lblEst = new Label();
            lblEst.Text = "";
            lblEst.AutoSize = true;
            lblEst.Font = Ui.F(8.5f);
            lblEst.ForeColor = Ui.Nice;
            lblEst.BackColor = Color.White;
            lblEst.Location = new Point(18, 78);

            lblTotal = new Label();
            lblTotal.Visible = false;
            lblTotal.Text = "";
            lblTotal.AutoSize = true;

            lblBudget = new Label();
            lblBudget.Visible = false;
            lblBudget.Text = "";
            lblBudget.AutoSize = true;

            FlowLayoutPanel flowB = new FlowLayoutPanel();
            flowB.Dock = DockStyle.Right;
            flowB.Width = 372;
            flowB.FlowDirection = FlowDirection.RightToLeft;
            flowB.WrapContents = false;
            flowB.BackColor = Color.White;
            flowB.Padding = new Padding(0, 32, 14, 0);

            btnExport = Ui.Ghost("导出 CSV", 104, 30);
            btnExport.Click += new EventHandler(delegate { ExportCsv(); });

            btnCopy = Ui.Ghost("复制合计", 104, 30);
            btnCopy.Click += new EventHandler(delegate { CopyTotal(); });

            btnCompare = Ui.Ghost("对比选中", 104, 30);
            btnCompare.Click += new EventHandler(delegate { CompareSelected(); });

            flowB.Controls.Add(btnExport);
            flowB.Controls.Add(btnCopy);
            flowB.Controls.Add(btnCompare);

            footer.Controls.Add(line);
            footer.Controls.Add(lblSel);
            footer.Controls.Add(lblTotalBig);
            footer.Controls.Add(lblDetail);
            footer.Controls.Add(lblEst);
            footer.Controls.Add(lblTotal);
            footer.Controls.Add(lblBudget);
            footer.Controls.Add(flowB);
        }

        bool quiet = false;
        Dictionary<string, Timeline> tlCache = new Dictionary<string, Timeline>();
        Dictionary<string, string> rawIndex = new Dictionary<string, string>();
        bool building = false;
        bool pendingBuild = false;
        int progDone = 0, progTotal = 0;
        string lastRawErr = "";

        void OnFirstLoad(object sender, EventArgs e)
        {
            if (!Directory.Exists(cfg.DataDir))
            {
                string d = Detector.FindDataDir();
                if (d != null) { cfg.DataDir = d; cfg.Save(); }
            }
            LoadTimelineCache();
            all = LoadSessions();
            RefreshView(false);
            SetupTray();
            SetupHotkey();
            if (chkAuto.Checked) timer.Start();
            if (!Directory.Exists(cfg.DataDir))
                lblStatus.Text = "没找到 dsh 缓存目录，点【设置】手动指定";
            else
                KickTimeline();
            cfg.Save();
            if (!cfg.NoGuide && !SelfCheck)
                BeginInvoke(new Action(delegate { ShowGuide(true); }));
        }

        // ---------- 时段统计 ----------
        static string TimelineCachePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dsh-usage");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "timeline3.cache");
            }
        }

        int dbgCacheLines, dbgCacheX, dbgCacheXFail;
        void LoadTimelineCache()
        {
            try
            {
                if (!File.Exists(TimelineCachePath)) return;
                foreach (string line in File.ReadAllLines(TimelineCachePath))
                {
                    dbgCacheLines++;
                    string[] p = line.Split('|');
                    if (p[0] == "X") dbgCacheX++;
                    if (p.Length < 2) continue;
                    string id = p[1];
                    Timeline t;
                    if (p[0] == "S")
                    {
                        if (p.Length < 10) continue;
                        t = new Timeline();
                        t.RawSize = long.Parse(p[2]);
                        t.RawTicks = long.Parse(p[3]);
                        t.OffU = long.Parse(p[4]); t.OffC = long.Parse(p[5]); t.OffO = long.Parse(p[6]);
                        t.PkU = long.Parse(p[7]); t.PkC = long.Parse(p[8]); t.PkO = long.Parse(p[9]);
                        t.FromRaw = true;
                        tlCache[id] = t;
                    }
                    else if (p[0] == "D")
                    {
                        if (p.Length < 9) continue;
                        if (!tlCache.TryGetValue(id, out t)) continue;
                        var b = new Slot();
                        b.OffU = long.Parse(p[3]); b.OffC = long.Parse(p[4]); b.OffO = long.Parse(p[5]);
                        b.PkU = long.Parse(p[6]); b.PkC = long.Parse(p[7]); b.PkO = long.Parse(p[8]);
                        t.Days[p[2]] = b;
                    }
                    else if (p[0] == "X")
                    {
                        if (p.Length < 9) { dbgCacheXFail++; continue; }
                        if (!tlCache.TryGetValue(id, out t)) continue;
                        var b = new Slot();
                        b.OffU = long.Parse(p[3]); b.OffC = long.Parse(p[4]); b.OffO = long.Parse(p[5]);
                        b.PkU = long.Parse(p[6]); b.PkC = long.Parse(p[7]); b.PkO = long.Parse(p[8]);
                        t.DateHours[p[2]] = b;
                    }
                    else if (p[0] == "H")
                    {
                        if (p.Length < 9) continue;
                        if (!tlCache.TryGetValue(id, out t)) continue;
                        int h = int.Parse(p[2]);
                        if (h < 0 || h > 23) continue;
                        t.Hours[h].OffU = long.Parse(p[3]); t.Hours[h].OffC = long.Parse(p[4]); t.Hours[h].OffO = long.Parse(p[5]);
                        t.Hours[h].PkU = long.Parse(p[6]); t.Hours[h].PkC = long.Parse(p[7]); t.Hours[h].PkO = long.Parse(p[8]);
                    }
                }
            }
            catch { }
        }

        void SaveTimelineCache()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in tlCache)
                {
                    Timeline t = kv.Value;
                    if (!t.FromRaw) continue;
                    sb.AppendLine(string.Format("S|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}",
                        kv.Key, t.RawSize, t.RawTicks, t.OffU, t.OffC, t.OffO, t.PkU, t.PkC, t.PkO));
                    foreach (var d in t.Days)
                    {
                        Slot b = d.Value;
                        sb.AppendLine(string.Format("D|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}",
                            kv.Key, d.Key, b.OffU, b.OffC, b.OffO, b.PkU, b.PkC, b.PkO));
                    }
                    for (int h = 0; h < 24; h++)
                    {
                        Slot b = t.Hours[h];
                        if (b.TotalTokens() == 0) continue;
                        sb.AppendLine(string.Format("H|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}",
                            kv.Key, h, b.OffU, b.OffC, b.OffO, b.PkU, b.PkC, b.PkO));
                    }
                    foreach (var x in t.DateHours)
                    {
                        Slot b = x.Value;
                        if (b.TotalTokens() == 0) continue;
                        sb.AppendLine(string.Format("X|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}",
                            kv.Key, x.Key, b.OffU, b.OffC, b.OffO, b.PkU, b.PkC, b.PkO));
                    }
                }
                string tmp = TimelineCachePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                try
                {
                    if (File.Exists(TimelineCachePath)) File.Replace(tmp, TimelineCachePath, null);
                    else File.Move(tmp, TimelineCachePath);
                }
                catch
                {
                    try { File.Copy(tmp, TimelineCachePath, true); File.Delete(tmp); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogErr("cache save -> " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        void KickTimeline()
        {
            if (building) { pendingBuild = true; return; }
            building = true;
            pendingBuild = false;
            var th = new Thread(new ThreadStart(BuildTimeline));
            th.IsBackground = true;
            th.Start();
        }

        void BuildTimeline()
        {
            try
            {
                if (rawIndex.Count == 0) rawIndex = Raw.Index(Raw.Root(cfg.DataDir));

                List<SessionInfo> items;
                lock (all) items = new List<SessionInfo>(all);
                progTotal = items.Count;
                progDone = 0;

                var results = new Dictionary<string, Timeline>();
                var gate = new object();
                int doneCount = 0;

                int nThreads = Math.Max(1, Math.Min(8, Environment.ProcessorCount));
                if (items.Count < 8) nThreads = 1;

                var workers = new List<Thread>();
                for (int ti = 0; ti < nThreads; ti++)
                {
                    int slotIdx = ti;
                    var th = new Thread(new ThreadStart(delegate
                    {
                        for (int i = slotIdx; i < items.Count; i += nThreads)
                        {
                            SessionInfo s = items[i];
                            Timeline got = null;
                            string rawPath;
                            if (rawIndex.TryGetValue(s.Id, out rawPath))
                            {
                                try
                                {
                                    long size = new FileInfo(rawPath).Length;
                                    long ticks = File.GetLastWriteTimeUtc(rawPath).Ticks;
                                    Timeline cached;
                                    if (tlCache.TryGetValue(s.Id, out cached) && cached.FromRaw && cached.RawSize == size && cached.RawTicks == ticks)
                                        got = cached;
                                    else
                                        got = Raw.Parse(rawPath);
                                }
                                catch (Exception ex)
                                {
                                    got = null;
                                    lastRawErr = ex.GetType().Name + ": " + ex.Message;
                                    LogErr("parse " + s.Id + " -> " + lastRawErr);
                                }
                            }
                            if (got != null)
                            {
                                lock (gate) { results[s.Id] = got; }
                            }
                            int dNow = Interlocked.Increment(ref doneCount);
                            if (dNow % 5 == 0 || dNow == items.Count)
                            {
                                progDone = dNow;
                                int tot = items.Count;
                                try
                                {
                                    BeginInvoke(new Action(delegate
                                    {
                                        lblUpdated.Text = string.Format("按时段统计 {0}/{1}…", dNow, tot);
                                    }));
                                }
                                catch { }
                            }
                        }
                    }));
                    th.IsBackground = true;
                    th.Start();
                    workers.Add(th);
                }
                foreach (Thread w in workers) w.Join();

                BeginInvoke(new Action(delegate
                {
                    ApplyTimeline(results);
                    building = false;
                    if (pendingBuild) KickTimeline();
                }));
            }
            catch (Exception ex)
            {
                building = false;
                LogErr("build -> " + ex.GetType().Name + ": " + ex.Message);
                try
                {
                    BeginInvoke(new Action(delegate { lblStatus.Text = "时段统计失败：" + ex.Message; }));
                }
                catch { }
            }
        }

        static string LogPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dsh-usage");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "error.log");
            }
        }

        static void LogErr(string msg)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        void ApplyTimeline(Dictionary<string, Timeline> results)
        {
            foreach (var kv in results) tlCache[kv.Key] = kv.Value;
            SaveTimelineCache();

            lock (all)
            {
                foreach (SessionInfo s in all)
                {
                    Timeline t;
                    if (results.TryGetValue(s.Id, out t) && t != null)
                    {
                        s.Tl = t;
                        s.Approx = false;
                    }
                    else
                    {
                        // 没有原始记录时，按“最后活动时间”整体归段，并标记为估算
                        var f = new Timeline();
                        bool pk = Peak.IsPeak(s.Last);
                        f.Add(s.Uncached, s.Cache, s.OutN, pk);
                        string dk = s.Last.ToString("yyyy-MM-dd");
                        var ds = new Slot();
                        ds.Add(s.Uncached, s.Cache, s.OutN, pk);
                        f.Days[dk] = ds;
                        f.Hours[s.Last.Hour].Add(s.Uncached, s.Cache, s.OutN, pk);
                        s.Tl = f;
                        s.Approx = true;
                    }
                }
            }
            RefreshView(true);
        }

        double SessCost(SessionInfo s)
        {
            Timeline t = Eff(s);
            if (t != null) return t.TotalCost();
            return 0;
        }

        // ---------- 数据 ----------
        static object Get(Dictionary<string, object> m, string k)
        {
            object v;
            if (m != null && m.TryGetValue(k, out v)) return v;
            return null;
        }

        static object Path2(object o, string k1, string k2)
        {
            var m = Json.AsMap(o);
            if (m == null) return null;
            object v;
            if (!m.TryGetValue(k1, out v)) return null;
            var m2 = Json.AsMap(v);
            if (m2 == null) return null;
            object v2;
            if (!m2.TryGetValue(k2, out v2)) return null;
            return v2;
        }

        // 单个会话 json 的解析结果（按文件大小+修改时间做增量，未变就跳过读盘和 JSON 解析）
        class SesProto
        {
            public long Size, Ticks;
            public long Unc, Cache, OutN;
            public string Id = "", Title = "", Cwd = "", Model = "", SubLabel = "";
            public DateTime Last;
            public int Turns;
            public bool IsSub;
            public List<string> ChildIds = new List<string>();
        }

        Dictionary<string, SesProto> sesProto = new Dictionary<string, SesProto>();
        int dbgSesHit, dbgSesNew;
        long dbgSesMs;

        SesProto ParseProto(string f)
        {
            try
            {
                object obj = Json.Parse(File.ReadAllText(f, Encoding.UTF8));
                var rec = Json.AsMap(Get(Json.AsMap(obj), "record"));
                if (rec == null) return null;
                var rows = Json.AsMap(Get(rec, "rows"));
                if (rows == null) return null;
                object tuObj = Get(rows, "tokenUsage");
                object totObj = Path2(tuObj, "val", "totals");
                var tot = Json.AsMap(totObj);
                if (tot == null) return null;
                long unc = Json.Long(Get(tot, "uncachedInputTokens"));
                long cache = Json.Long(Get(tot, "cacheReadTokens"));
                long outN = Json.Long(Get(tot, "outputTokens"));
                long total = unc + cache + outN;
                if (total <= 0) return null;

                var ps = new SesProto();
                ps.Unc = unc; ps.Cache = cache; ps.OutN = outN;
                ps.Title = Json.Str2(Path2(rows, "title", "val"));
                if (ps.Title.Length == 0) ps.Title = "(无标题)";
                ps.Id = Path.GetFileNameWithoutExtension(f).Replace("session-", "");

                ps.Last = File.GetLastWriteTime(f);
                var tc = Json.AsMap(Path2(rows, "timeContext", "val"));
                if (tc != null)
                {
                    object ms;
                    if (tc.TryGetValue("lastMessageTime", out ms) && Json.ToDouble(ms) > 0)
                    {
                        try { ps.Last = DateTimeOffset.FromUnixTimeMilliseconds((long)Json.ToDouble(ms)).LocalDateTime; } catch { }
                    }
                }

                ps.Cwd = Json.Str2(Get(Json.AsMap(Get(rec, "identity")), "cwd"));

                var mm = Json.AsMap(Path2(rows, "modelSelection", "val"));
                if (mm != null)
                {
                    var lu = Json.AsMap(Get(mm, "lastUsed"));
                    if (lu != null)
                    {
                        ps.Model = Json.Str2(Get(lu, "model"));
                        string prov = Json.Str2(Get(lu, "provider"));
                        if (prov.Length > 0) ps.Model = prov + " / " + ps.Model;
                    }
                }

                var stMap = Json.AsMap(Get(rows, "sessionStats"));
                var stVal = stMap != null ? Json.AsMap(Get(stMap, "val")) : null;
                object turnsObj = stVal != null ? Get(stVal, "turns") : null;
                ps.Turns = (int)Json.Long(turnsObj);

                // 子智能体：rows.subagent.val.identity {mode,label}
                var subVal = Json.AsMap(Path2(rows, "subagent", "val"));
                if (subVal != null && subVal.Count > 0)
                {
                    var idm = Json.AsMap(Get(subVal, "identity"));
                    ps.IsSub = true;
                    if (idm != null) ps.SubLabel = Json.Str2(Get(idm, "label"));
                    if (ps.SubLabel.Length == 0) ps.SubLabel = ps.Title;
                }

                // 父子关系：rows.subagentCatalog.val.head.values[] {childId,label}
                var catVal = Json.AsMap(Path2(rows, "subagentCatalog", "val"));
                if (catVal != null)
                {
                    var headMap = Json.AsMap(Get(catVal, "head"));
                    var vals = headMap != null ? Json.AsList(Get(headMap, "values")) : null;
                    if (vals != null)
                    {
                        foreach (object vo in vals)
                        {
                            var vm = Json.AsMap(vo);
                            string cid = Json.Str2(Get(vm, "childId"));
                            if (cid.Length > 0 && !ps.ChildIds.Contains(cid)) ps.ChildIds.Add(cid);
                        }
                    }
                }
                return ps;
            }
            catch { return null; }
        }

        List<SessionInfo> LoadSessions()
        {
            var output = new List<SessionInfo>();
            var fresh = new Dictionary<string, SesProto>();
            dbgSesHit = 0; dbgSesNew = 0;
            var swAll = new System.Diagnostics.Stopwatch();
            swAll.Start();
            try
            {
                if (!Directory.Exists(cfg.DataDir)) { sesProto.Clear(); return output; }
                foreach (string f in Directory.GetFiles(cfg.DataDir, "*.json"))
                {
                    SesProto ps = null;
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        long sz = fi.Length, tk = fi.LastWriteTimeUtc.Ticks;
                        if (!sesProto.TryGetValue(f, out ps) || ps == null || ps.Size != sz || ps.Ticks != tk)
                        {
                            ps = ParseProto(f);
                            if (ps != null) { ps.Size = sz; ps.Ticks = tk; dbgSesNew++; }
                        }
                        else dbgSesHit++;
                    }
                    catch { ps = null; }
                    if (ps == null) continue;
                    fresh[f] = ps;

                    var s = new SessionInfo();
                    s.Uncached = ps.Unc; s.Cache = ps.Cache; s.OutN = ps.OutN;
                    s.Total = ps.Unc + ps.Cache + ps.OutN;
                    s.Title = ps.Title;
                    s.Id = ps.Id;
                    s.Last = ps.Last;
                    s.Cwd = ps.Cwd;
                    s.Model = ps.Model;
                    s.Turns = ps.Turns;
                    s.IsSub = ps.IsSub;
                    s.SubLabel = ps.SubLabel;
                    if (ps.ChildIds.Count > 0) s.ChildIds.AddRange(ps.ChildIds);

                    output.Add(s);
                }
            }
            catch { }
            sesProto = fresh;
            swAll.Stop();
            dbgSesMs = swAll.ElapsedMilliseconds;

            // 反向补齐：子体 → 父会话
            var byId = new Dictionary<string, SessionInfo>();
            foreach (SessionInfo s in output) byId[s.Id] = s;
            foreach (SessionInfo s in output)
            {
                foreach (string cid in s.ChildIds)
                {
                    SessionInfo child = null;
                    if (!byId.TryGetValue(cid, out child) || child == null || child == s) continue;
                    child.ParentId = s.Id;
                    child.ParentTitle = s.Title;
                }
            }
            return output;
        }

        // ---------- 视图 ----------
        static Timeline Eff(SessionInfo s)
        {
            return s.RangeTl != null ? s.RangeTl : s.Tl;
        }

        List<SessionInfo> GetFiltered()
        {
            var rows = new List<SessionInfo>(all);
            DateTime now = DateTime.Now;
            if (timeIdx == 1)
            {
                rows = rows.FindAll(delegate(SessionInfo s) { return s.Last.Date == now.Date; });
            }
            else if (timeIdx == 2)
            {
                DateTime cut = now.AddDays(-7);
                rows = rows.FindAll(delegate(SessionInfo s) { return s.Last >= cut; });
            }
            else if (timeIdx == 3)
            {
                DateTime cut = now.AddDays(-30);
                rows = rows.FindAll(delegate(SessionInfo s) { return s.Last >= cut; });
            }

            string kind = cmbKind == null ? "全部会话" : (cmbKind.SelectedItem as string);
            if (kind == "只看主对话")
                rows = rows.FindAll(delegate(SessionInfo s) { return !s.IsSub; });
            else if (kind == "只看子智能体")
                rows = rows.FindAll(delegate(SessionInfo s) { return s.IsSub; });

            // 自定义时段：按“逐小时”记录裁剪，只留有消耗的会话
            foreach (SessionInfo s in rows) s.RangeTl = null;
            if (rangeA != null && rangeB != null)
            {
                var kept = new List<SessionInfo>();
                foreach (SessionInfo s in rows)
                {
                    if (s.Tl == null) continue;
                    Timeline sl = Tl.Slice(s.Tl, rangeA.Value, rangeB.Value);
                    if (sl == null || sl.TotalTokens() <= 0) continue;
                    s.RangeTl = sl;
                    kept.Add(s);
                }
                rows = kept;
            }

            string key = txt.Text.Trim().ToLowerInvariant();
            if (key.Length > 0)
            {
                rows = rows.FindAll(delegate(SessionInfo s)
                {
                    return (s.Title.ToLowerInvariant().IndexOf(key) >= 0)
                        || (s.SubLabel.ToLowerInvariant().IndexOf(key) >= 0)
                        || (s.Id.ToLowerInvariant().IndexOf(key) >= 0)
                        || ((s.Cwd == null ? "" : s.Cwd.ToLowerInvariant()).IndexOf(key) >= 0);
                });
            }

            if (chkLow != null && chkLow.Checked)
                rows = rows.FindAll(delegate(SessionInfo s)
                {
                    if (s.Total <= 0) return false;
                    return s.Total >= 500000 && s.Cache * 100.0 / s.Total < 80.0;
                });

            Comparison<SessionInfo> cmp = delegate(SessionInfo a, SessionInfo b)
            {
                int r = 0;
                switch (sortCol)
                {
                    case 2:
                        r = string.Compare(a.IsSub ? a.SubLabel : a.Title, b.IsSub ? b.SubLabel : b.Title, StringComparison.OrdinalIgnoreCase);
                        break;
                    case 3:
                        r = string.Compare(OwnLabel(a), OwnLabel(b), StringComparison.OrdinalIgnoreCase);
                        break;
                    case 4:
                        {
                            long x = TokOf(a), y = TokOf(b);
                            r = x < y ? -1 : (x > y ? 1 : 0);
                            break;
                        }
                    case 5:
                        {
                            double x = OffCostOf(a), y = OffCostOf(b);
                            r = x < y ? -1 : (x > y ? 1 : 0);
                            break;
                        }
                    case 6:
                        {
                            double x = PkCostOf(a), y = PkCostOf(b);
                            r = x < y ? -1 : (x > y ? 1 : 0);
                            break;
                        }
                    case 7:
                        {
                            double x = SessCost(a), y = SessCost(b);
                            r = x < y ? -1 : (x > y ? 1 : 0);
                            break;
                        }
                    default:
                        r = a.Last.CompareTo(b.Last);
                        break;
                }
                return sortAsc ? r : -r;
            };
            rows.Sort(cmp);

            return rows;
        }

        // 把子会话折叠到父会话下面：父行按当前排序排，子行只在其后出现
        void FoldShown()
        {
            subCount.Clear();
            HashSet<string> mainIds = new HashSet<string>();
            foreach (SessionInfo s in shown) if (!s.IsSub) mainIds.Add(s.Id);

            Dictionary<string, List<SessionInfo>> by = new Dictionary<string, List<SessionInfo>>();
            List<SessionInfo> orphans = new List<SessionInfo>();
            foreach (SessionInfo s in shown)
            {
                if (!s.IsSub) continue;
                string pid = s.ParentId == null ? "" : s.ParentId;
                if (pid.Length > 0 && mainIds.Contains(pid))
                {
                    if (!by.ContainsKey(pid)) by[pid] = new List<SessionInfo>();
                    by[pid].Add(s);
                }
                else orphans.Add(s);
            }

            bool fold = foldSubs && txt.Text.Trim().Length == 0 && cmbKind.SelectedIndex != 2;
            List<SessionInfo> flat = new List<SessionInfo>();
            foreach (SessionInfo m in shown)
            {
                if (m.IsSub) continue;
                flat.Add(m);
                List<SessionInfo> kids;
                if (by.TryGetValue(m.Id, out kids))
                {
                    subCount[m.Id] = kids.Count;
                    if (!fold || expanded.Contains(m.Id))
                        foreach (SessionInfo k in kids) flat.Add(k);
                }
            }
            foreach (SessionInfo o in orphans) flat.Add(o);
            shown = flat;
        }

        void ToggleFold(string id)
        {
            if (expanded.Contains(id)) expanded.Remove(id); else expanded.Add(id);
            RefreshView(true);
        }

        void RefreshView(bool keep)
        {
            if (keep && grid.Rows.Count > 0)
            {
                try { if (grid.FirstDisplayedScrollingRowIndex >= 0) topRow = grid.FirstDisplayedScrollingRowIndex; }
                catch { }
            }

            shown = GetFiltered();
            FoldShown();
            maxRowCost = 1;
            foreach (SessionInfo s in shown)
            {
                double c = SessCost(s);
                if (c > maxRowCost) maxRowCost = c;
            }

            grid.SuspendLayout();
            grid.Rows.Clear();
            for (int n = 0; n < shown.Count; n++)
            {
                SessionInfo s = shown[n];
                Timeline et = Eff(s);
                long tok = et != null ? et.TotalTokens() : s.Total;
                if (et != null && tok <= 0) tok = s.Total;
                double offC = et != null ? et.OffCost() : 0;
                double pkC = et != null ? et.PkCost() : 0;
                double allC = offC + pkC;
                string costText = allC > 0 ? ((s.Approx ? "≈" : "") + string.Format("¥{0:F2}", allC)) : "—";

                int idx = grid.Rows.Add();
                DataGridViewRow row = grid.Rows[idx];
                row.Tag = s;
                row.Cells[0].Value = "";
                row.Cells[1].Value = s.IsSub ? "子" : "主";
                row.Cells[2].Value = s.IsSub ? (s.SubLabel.Length > 0 ? s.SubLabel : s.Title) : s.Title;
                if (s.IsSub)
                {
                    DataGridViewCellStyle ind = new DataGridViewCellStyle();
                    ind.Padding = new Padding(Ui.S(22), 0, 6, 0);
                    ind.ForeColor = Color.FromArgb(90, 98, 114);
                    row.Cells[2].Style = ind;
                }
                row.Cells[3].Value = OwnLabel(s);
                row.Cells[4].Value = Fmt(tok);
                row.Cells[5].Value = offC > 0 ? string.Format("¥{0:F2}", offC) : "—";
                row.Cells[6].Value = pkC > 0 ? string.Format("¥{0:F2}", pkC) : "—";
                row.Cells[7].Value = costText;
                row.Cells[8].Value = s.Last.ToString("MM-dd HH:mm");

                DataGridViewCellStyle badge = new DataGridViewCellStyle();
                badge.Alignment = DataGridViewContentAlignment.MiddleCenter;
                badge.Font = Ui.F(8.5f);
                badge.Padding = new Padding(0);
                if (s.IsSub)
                {
                    badge.ForeColor = Color.FromArgb(122, 130, 146);
                    badge.BackColor = Color.FromArgb(245, 246, 249);
                }
                else
                {
                    badge.ForeColor = Ui.Accent;
                    badge.BackColor = Ui.AccentSoft;
                }
                row.Cells[1].Style = badge;

                if (checkedIds.ContainsKey(s.Id)) row.DefaultCellStyle.BackColor = Ui.RowSel;
            }
            if (keep && topRow > 0 && topRow < grid.Rows.Count)
            {
                try { grid.FirstDisplayedScrollingRowIndex = topRow; } catch { }
            }
            grid.ResumeLayout();
            RefreshHeaders();
            SyncChips();

            if (btnFold != null)
            {
                bool anyFolded = false;
                foreach (KeyValuePair<string, int> kv in subCount)
                    if (!expanded.Contains(kv.Key)) { anyFolded = true; break; }
                btnFold.Text = anyFolded ? "展开全部" : "收起全部";
                btnFold.Visible = subCount.Count > 0;
            }

            int subsAll = 0, subsShown = 0;
            foreach (SessionInfo s in all) if (s.IsSub) subsAll++;
            foreach (SessionInfo s in shown) if (s.IsSub) subsShown++;
            string foldNote = (subsAll > 0 && subsShown < subsAll)
                ? string.Format("（另收起 {0} 个子会话，点左侧三角展开）", subsAll - subsShown)
                : "";

            lblUpdated.Text = "更新于 " + DateTime.Now.ToString("HH:mm:ss");
            int coarse = 0;
            if (rangeA != null) foreach (SessionInfo s in shown) if (s.Tl != null && s.Tl.DateHours.Count == 0) coarse++;
            string coarseNote = coarse > 0 ? "（其中 " + coarse + " 个无逐小时记录，按整天估算）" : "";
            string modeNote = "";
            scopeText = timeIdx == 4 && rangeA != null && rangeB != null
                ? string.Format("共 {0} 个会话；当前显示 {1} 个{2}  ·  时段 {3} ~ {4}{5}  ·  排序：{6}{7}", all.Count, shown.Count, foldNote,
                    rangeA.Value.ToString("MM-dd HH:mm"), rangeB.Value.ToString("MM-dd HH:mm"), coarseNote, SortLabel(), modeNote)
                : string.Format("共 {0} 个会话；当前显示 {1} 个{2}  ·  排序：{3}（点表头可切换）{4}", all.Count, shown.Count, foldNote, SortLabel(), modeNote);
            lblStatus.Text = scopeText;
            UpdateTotal();
            UpdateKpi();
        }

        string RowText(SessionInfo s)
        {
            string costText = "—";
            Timeline eff = Eff(s);
            long tok = eff != null ? eff.TotalTokens() : s.Total;
            if (eff != null && eff.TotalTokens() <= 0) tok = s.Total;
            double c = SessCost(s);
            if (c > 0) costText = (s.Approx ? "≈" : "") + string.Format("¥{0:F2}", c);
            return string.Format("{0}  {1}  {2} {3}",
                Fit(Fmt(tok), 8), Fit(s.ShowName(), 52), s.Last.ToString("MM-dd HH:mm"), PadL(costText, 8));
        }

        static bool IsWide(char c)
        {
            if (c >= '\u1100' && c <= '\u115F') return true;
            if (c >= '\u2E80' && c <= '\uA4CF') return true;
            if (c >= '\uAC00' && c <= '\uD7A3') return true;
            if (c >= '\uF900' && c <= '\uFAFF') return true;
            if (c >= '\uFE30' && c <= '\uFE4F') return true;
            if (c >= '\uFF00' && c <= '\uFF60') return true;
            if (c >= '\uFFE0' && c <= '\uFFE6') return true;
            return false;
        }

        static int DisplayWidth(string t)
        {
            int w = 0;
            foreach (char c in t) w += IsWide(c) ? 2 : 1;
            return w;
        }

        // 按“半角字符”为单位对齐（新宋体下中文正好 2 格）
        string Fit(string t, int units)
        {
            int w = 0;
            if (DisplayWidth(t) > units)
            {
                int cut = 0;
                for (int i = 0; i < t.Length; i++)
                {
                    int cw = IsWide(t[i]) ? 2 : 1;
                    if (w + cw > units - 2) break;
                    w += cw;
                    cut = i + 1;
                }
                t = t.Substring(0, cut) + "…";
                w += 2;
            }
            else w = DisplayWidth(t);
            return t + new string(' ', Math.Max(0, units - w));
        }

        string PadL(string t, int units)
        {
            return new string(' ', Math.Max(0, units - DisplayWidth(t))) + t;
        }

        static string Fmt(long n)
        {
            if (n >= 1000000) return string.Format("{0:F2}M", n / 1000000.0);
            if (n >= 1000) return string.Format("{0:F1}K", n / 1000.0);
            return n.ToString();
        }

        void UpdateBudget()
        {
            string mk = DateTime.Now.ToString("yyyy-MM");
            double c = 0;
            lock (all)
            {
                foreach (SessionInfo s in all)
                {
                    if (s.Tl == null) continue;
                    foreach (var kv in s.Tl.Days)
                        if (kv.Key.StartsWith(mk, StringComparison.Ordinal)) c += kv.Value.TotalCost();
                }
            }
            monthCost = c;
            if (cfg.Budget > 0)
            {
                double pct = c / cfg.Budget;
                lblBudget.Text = string.Format("本月 ¥{0:F2} / 预算 ¥{1:F2}（{2:F0}%）", c, cfg.Budget, pct * 100);
                if (pct >= 1.0) lblBudget.ForeColor = Color.FromArgb(200, 40, 40);
                else if (pct >= 0.8) lblBudget.ForeColor = Color.FromArgb(200, 125, 20);
                else lblBudget.ForeColor = Color.FromArgb(60, 140, 90);
            }
            else
            {
                lblBudget.Text = string.Format("本月已用 ¥{0:F2}", c);
                lblBudget.ForeColor = Color.FromArgb(110, 112, 122);
            }
        }

        void UpdateTotal()
        {
            UpdateBudget();
            var s2 = new List<SessionInfo>();
            foreach (SessionInfo s in shown) if (checkedIds.ContainsKey(s.Id)) s2.Add(s);
            sel = s2;

            if (btnCompare != null)
                btnCompare.Text = checkedIds.Count > 0 ? "对比选中 (" + checkedIds.Count + ")" : "对比选中";

            // 连带子体：勾了主对话就把它的子智能体一起计入
            if (chkChain != null && chkChain.Checked)
            {
                var have = new Hashtable();
                foreach (SessionInfo s in s2) have[s.Id] = true;
                var extra = new List<SessionInfo>();
                foreach (SessionInfo p in s2)
                {
                    if (p.IsSub) continue;
                    foreach (SessionInfo c in all)
                    {
                        if (c.ParentId != p.Id) continue;
                        if (have.ContainsKey(c.Id)) continue;
                        have[c.Id] = true;
                        extra.Add(c);
                    }
                }
                s2.AddRange(extra);
                sel = s2;
            }

            long total = 0, unc = 0, cch = 0, outN = 0;
            long offTok = 0, pkTok = 0;
            int turns = 0;
            double offC = 0, pkC = 0;
            bool approx = false;
            foreach (SessionInfo s in sel)
            {
                Timeline et = Eff(s);
                long sTotal = et != null ? et.TotalTokens() : s.Total;
                total += sTotal;
                if (rangeA == null || rangeB == null)
                {
                    unc += s.Uncached; cch += s.Cache; outN += s.OutN;
                }
                turns += s.Turns;
                if (et != null)
                {
                    offTok += et.OffU + et.OffC + et.OffO;
                    pkTok += et.PkU + et.PkC + et.PkO;
                    offC += et.OffCost();
                    pkC += et.PkCost();
                    if (rangeA != null && rangeB != null)
                    {
                        unc += et.OffU + et.PkU;
                        cch += et.OffC + et.PkC;
                        outN += et.OffO + et.PkO;
                    }
                }
                if (s.Approx) approx = true;
            }
            selTurns = turns;
            estOff = offC;
            estOn = pkC;

            if (sel.Count == 0)
            {
                lblSel.Text = "已选 0 个会话";
                lblTotalBig.Text = "0 token";
                lblDetail.Text = "勾选左侧方框后，这里显示合计 token、费用与峰谷拆分";
                lblEst.Text = "";
                estOff = 0; estOn = 0;
                return;
            }

            int pct = 0;
            if (total > 0) pct = (int)((double)cch / total * 100.0);
            int subN = 0, mainN = 0;
            foreach (SessionInfo s in sel) { if (s.IsSub) subN++; else mainN++; }

            // 合计里到底算进了多少个“当前列表上点不到”的会话（连带子体 / 被折叠 / 被筛选）
            var visIds = new Hashtable();
            foreach (SessionInfo s in shown) visIds[s.Id] = true;
            int hiddenInSel = 0;
            foreach (SessionInfo s in sel) if (!visIds.ContainsKey(s.Id)) hiddenInSel++;
            int goneChecked = 0;
            foreach (DictionaryEntry de in checkedIds) if (!visIds.ContainsKey((string)de.Key)) goneChecked++;

            string extraNote = "";
            if (sel.Count == 1 && sel[0].IsSub && sel[0].ParentTitle.Length > 0)
                extraNote = "  ｜ 属于：" + sel[0].ParentTitle;
            if (hiddenInSel > 0) extraNote += string.Format("  ｜ 含 {0} 个未在列表显示（连带子体或被收起），仍计入合计", hiddenInSel);
            if (goneChecked > 0) extraNote += string.Format("  ｜ 另有 {0} 个已勾选但被筛选隐藏，未计入合计", goneChecked);
            lblSel.Text = string.Format("已选 {0} 个会话（主 {1} · 子 {2} · 共 {3} 轮）{4}", sel.Count, mainN, subN, turns, extraNote);
            lblTotalBig.Text = string.Format("{0} token   ·   合计 ¥{1:F2}", Fmt(total), offC + pkC);
            lblDetail.Text = string.Format("未命中 {0}  +  缓存命中 {1}（{2}%）  +  输出 {3}", Fmt(unc), Fmt(cch), pct, Fmt(outN));
            lblDetail.ForeColor = pct < 80 ? Color.FromArgb(190, 110, 20) : Ui.Ink3;

            lblEst.Text = string.Format("官方峰谷价：闲时 {0} → ¥{1:F2}   ｜   高峰 {2} → ¥{3:F2}   ｜   合计 ¥{4:F2}{5}",
                Fmt(offTok), offC, Fmt(pkTok), pkC, offC + pkC, approx ? "   （≈ 含估算）" : "");
        }

        void OnAutoChanged(object sender, EventArgs e)
        {
            cfg.AutoRefresh = chkAuto.Checked;
            if (chkAuto.Checked)
            {
                timer.Interval = numInterval.Value * 1000;
                timer.Start();
            }
            else timer.Stop();
        }

        void OnTick(object sender, EventArgs e)
        {
            if (chkAuto.Checked) Reload(true);
        }

        void Reload(bool keep)
        {
            all = LoadSessions();
            RefreshView(keep);
            KickTimeline();
        }

        void CopyTotal()
        {
            if (sel.Count == 0)
            {
                MessageBox.Show("先勾选几个会话。", Program.AppTitle);
                return;
            }
            long total = 0, unc = 0, cch = 0, outN = 0;
            foreach (SessionInfo s in sel) { total += s.Total; unc += s.Uncached; cch += s.Cache; outN += s.OutN; }
            var sb = new StringBuilder();
            sb.AppendLine("dsh 会话用量  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine(string.Format("选中 {0} 个会话，共 {1} 轮", sel.Count, selTurns));
            sb.AppendLine(string.Format("总 token  {0:N0}  ({1})", total, Fmt(total)));
            sb.AppendLine(string.Format("  未命中 {0:N0} + 缓存命中 {1:N0} + 输出 {2:N0}", unc, cch, outN));
            sb.AppendLine(string.Format("按官方峰谷价：闲时 ¥{0:F2}   高峰 ¥{1:F2}   合计 ¥{2:F2}", estOff, estOn, estOff + estOn));
            sb.AppendLine();
            int i = 0;
            foreach (SessionInfo s in sel)
            {
                i++;
                string c = "—";
                double cc = SessCost(s);
                if (cc > 0) c = (s.Approx ? "≈" : "") + string.Format("¥{0:F2}", cc);
                long offT = 0, pkT = 0;
                Timeline et3 = Eff(s);
                long st3 = et3 != null ? et3.TotalTokens() : s.Total;
                if (et3 != null) { offT = et3.OffU + et3.OffC + et3.OffO; pkT = et3.PkU + et3.PkC + et3.PkO; }
                sb.AppendLine(string.Format("{0}. {1}  {2}  {3}  闲时 {4} / 高峰 {5}  费用 {6}",
                    i, Fmt(st3), s.ShowName(), s.Last.ToString("MM-dd HH:mm"), Fmt(offT), Fmt(pkT), c));
            }
            try { Clipboard.SetText(sb.ToString()); lblStatus.Text = "合计已复制到剪贴板"; }
            catch { lblStatus.Text = "复制失败（剪贴板被占用）"; }
        }

        void ExportCsv()
        {
            var rows = new List<SessionInfo>(sel);
            bool useAll = false;
            if (rows.Count == 0) { rows = new List<SessionInfo>(shown); useAll = true; }
            if (rows.Count == 0) { MessageBox.Show("没有可导出的会话。", Program.AppTitle); return; }

            var dlg = new SaveFileDialog();
            dlg.Filter = "CSV 文件 (*.csv)|*.csv";
            dlg.FileName = "dsh-token-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var sb = new StringBuilder();
            sb.AppendLine("会话id,标题,最后活动,总token,未命中缓存,缓存命中,输出,轮数,闲时token,高峰token,闲时(元),高峰(元),合计(元),项目目录,类型,所属主对话,子体标签");
            foreach (SessionInfo s in rows)
            {
                string title = s.Title.Replace("\"", "\"\"");
                string cwd = (s.Cwd == null ? "" : s.Cwd).Replace("\"", "\"\"");
                long offT = 0, pkT = 0;
                double offC = 0, pkC = 0;
                Timeline et4 = Eff(s);
                long st4 = et4 != null ? et4.TotalTokens() : s.Total;
                long unc4 = et4 != null ? et4.OffU + et4.PkU : s.Uncached;
                long cch4 = et4 != null ? et4.OffC + et4.PkC : s.Cache;
                long out4 = et4 != null ? et4.OffO + et4.PkO : s.OutN;
                if (et4 != null)
                {
                    offT = et4.OffU + et4.OffC + et4.OffO;
                    pkT = et4.PkU + et4.PkC + et4.PkO;
                    offC = et4.OffCost();
                    pkC = et4.PkCost();
                }
                sb.AppendLine(string.Format("\"{0}\",\"{1}\",{2},{3},{4},{5},{6},{7},{8},{9},{10:F2},{11:F2},{12:F2},\"{13}\",{14},\"{15}\",\"{16}\"",
                    s.Id, title, s.Last.ToString("yyyy-MM-dd HH:mm"), st4, unc4, cch4, out4, s.Turns,
                    offT, pkT, offC, pkC, offC + pkC, cwd,
                    s.KindText, s.ParentTitle.Replace("\"", "\"\""), s.SubLabel.Replace("\"", "\"\"")));
            }
            try
            {
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                lblStatus.Text = string.Format("已导出 {0} {1} 条：{2}", useAll ? "当前列表全部" : "已勾选", rows.Count, dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message, Program.AppTitle);
            }
        }

        void ShowDist()
        {
            List<SessionInfo> rows;
            string scope;
            if (sel.Count > 0) { rows = new List<SessionInfo>(sel); scope = string.Format("已选 {0} 个会话", sel.Count); }
            else { rows = new List<SessionInfo>(shown); scope = string.Format("当前列表 {0} 个会话", shown.Count); }
            if (rows.Count == 0) { MessageBox.Show("没有可统计的会话。", Program.AppTitle); return; }
            using (var dlg = new DistForm(rows, scope, null, all, rangeA, rangeB)) dlg.ShowDialog(this);
        }

        void ShowGuide(bool fromStartup)
        {
            using (var dlg = new GuideForm(cfg, fromStartup))
            {
                dlg.ShowDialog(this);
                if (fromStartup && dlg.NoShow) { cfg.NoGuide = true; cfg.Save(); }
            }
        }

        void ShowSettings()
        {
            using (var dlg = new SettingsForm(cfg))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    cfg = dlg.Result;
                    cfg.Save();
                    Pricing.OffMiss = cfg.PriceOffMiss; Pricing.OffHit = cfg.PriceOffHit; Pricing.OffOut = cfg.PriceOffOut;
                    Pricing.PkMiss = cfg.PricePkMiss; Pricing.PkHit = cfg.PricePkHit; Pricing.PkOut = cfg.PricePkOut;
                    all = LoadSessions();
                    RefreshView(true);
                }
            }
        }

        // ---------- 截图自检 ----------
        public void TurnShot(string path, string checks)
        {
            Show();
            Application.DoEvents();
            WaitBuild(90);
            if (rawIndex.Count == 0) rawIndex = Raw.Index(Raw.Root(cfg.DataDir));
            SessionInfo pick = null;
            string rawPath = null;
            long bestSize = -1;
            foreach (SessionInfo s in all)
            {
                string rp;
                if (!rawIndex.TryGetValue(s.Id, out rp)) continue;
                long sz = 0;
                try { sz = new FileInfo(rp).Length; } catch { }
                if (sz > bestSize) { bestSize = sz; pick = s; rawPath = rp; }
            }
            if (pick == null) { File.WriteAllText(path, "no raw", new UTF8Encoding(false)); exiting = true; Close(); return; }
            var turns = Raw.ParseTurns(rawPath);
            var dlg = new TurnForm(pick, turns);
            dlg.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(400);
            Application.DoEvents();
            var bmp = new Bitmap(dlg.Width, dlg.Height);
            dlg.DrawToBitmap(bmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
            dlg.Close();
            exiting = true;
            Close();
        }

        public void DistShot(string path, string checks, int tab, string csv, string ra, string rb)
        {
            Show();
            Application.DoEvents();
            WaitBuild(90);
            DateTime? qa = null, qb = null;
            if (ra != null && rb != null)
            {
                try
                {
                    qa = DateTime.Parse(ra.Replace('T', ' '));
                    qb = DateTime.Parse(rb.Replace('T', ' '));
                    rangeA = qa; rangeB = qb;
                    timeIdx = 4;
                    RefreshView(true);
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(checks))
            {
                foreach (string part in checks.Split(','))
                {
                    int idx;
                    if (int.TryParse(part.Trim(), out idx) && idx >= 0 && idx < shown.Count)
                        checkedIds[shown[idx].Id] = true;
                }
                RefreshView(true);
                Application.DoEvents();
            }
            var picked = new List<SessionInfo>();
            for (int i = 0; i < shown.Count && picked.Count < 20; i++)
                if (checkedIds.ContainsKey(shown[i].Id)) picked.Add(shown[i]);
            if (picked.Count == 0)
                for (int i = 0; i < shown.Count && i < 3; i++) picked.Add(shown[i]);

            var dlg = new DistForm(picked, string.Format("已选 {0} 个会话", picked.Count), csv, all, qa, qb);
            if (tab >= 0 && tab < dlg.Tabs.TabPages.Count) dlg.Tabs.SelectedIndex = tab;
            dlg.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(400);
            Application.DoEvents();
            var bmp = new Bitmap(dlg.Width, dlg.Height);
            dlg.DrawToBitmap(bmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
            dlg.Close();
            exiting = true;
            Close();
        }

        public void Diag(string path, string ra, string rb)
        {
            Show();
            Application.DoEvents();
            WaitBuild(90);
            if (ra != null && rb != null)
            {
                try
                {
                    rangeA = DateTime.Parse(ra.Replace('T', ' '));
                    rangeB = DateTime.Parse(rb.Replace('T', ' '));
                    timeIdx = 4;
                    RefreshView(true);
                }
                catch { }
            }
            var sb = new StringBuilder();
            using (var g = CreateGraphics())
            {
                sb.AppendLine("dpi=" + g.DpiX);
                sb.AppendLine("autoscale=" + AutoScaleMode + " factor=" + AutoScaleFactor);
            }
            sb.AppendLine("form=" + Width + "x" + Height + " client=" + ClientSize.Width + "x" + ClientSize.Height);
            sb.AppendLine("wa=" + Screen.FromPoint(Cursor.Position).WorkingArea + " frame=" + (Width - ClientSize.Width) + "x" + (Height - ClientSize.Height) + " min=" + MinimumSize);
            sb.AppendLine("txt=" + txt.Bounds);
            sb.AppendLine("grid=" + grid.Bounds + " rowH=" + grid.RowTemplate.Height + " rows=" + grid.Rows.Count);
            sb.AppendLine("dataDir=" + cfg.DataDir + " exists=" + Directory.Exists(cfg.DataDir));
            sb.AppendLine("rawRoot=" + Raw.Root(cfg.DataDir) + " rawFiles=" + rawIndex.Count);
            sb.AppendLine("scopeText=" + scopeText);
            sb.AppendLine("sessions=" + all.Count + " sesCacheHit=" + dbgSesHit + " sesParsed=" + dbgSesNew);
            // 第二次全量刷新：验证增量缓存是否真的没再读盘解析
            {
                long coldMs = dbgSesMs;
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                List<SessionInfo> tmp2 = LoadSessions();
                sw.Stop();
                sb.AppendLine("reload2 ms=" + sw.ElapsedMilliseconds + " hit=" + dbgSesHit + " parsed=" + dbgSesNew + " count=" + tmp2.Count + " coldMs=" + coldMs);
            }
            sb.AppendLine("range=" + (rangeA == null ? "none" : rangeA.Value.ToString("yyyy-MM-dd HH:mm") + " ~ " + rangeB.Value.ToString("yyyy-MM-dd HH:mm")));
            sb.AppendLine("shown=" + shown.Count);
            int ok = 0, ap = 0;
            foreach (SessionInfo s in all) { if (s.Tl != null && !s.Approx) ok++; if (s.Approx) ap++; }
            sb.AppendLine("timelineOK=" + ok + " approx=" + ap);
            int dhOk = 0;
            foreach (SessionInfo s in all) if (s.Tl != null && s.Tl.DateHours.Count > 0) dhOk++;
            sb.AppendLine("dateHoursOK=" + dhOk);
            int cDh = 0;
            foreach (var kv in tlCache) if (kv.Value.DateHours.Count > 0) cDh++;
            sb.AppendLine("cacheEntries=" + tlCache.Count + " withDateHours=" + cDh + " lines=" + dbgCacheLines + " x=" + dbgCacheX + " xFail=" + dbgCacheXFail);
            sb.AppendLine("tlCachePath=" + TimelineCachePath + " lenAtNow=" + (File.Exists(TimelineCachePath) ? new FileInfo(TimelineCachePath).Length : -1) + " linesAtNow=" + (File.Exists(TimelineCachePath) ? File.ReadAllLines(TimelineCachePath).Length : -1));
            foreach (SessionInfo s in shown)
            {
                if (s.Tl == null) { sb.AppendLine("item " + s.ShowName() + " tl=null"); continue; }
                sb.AppendLine(string.Format("item {0} tl={1:N0} eff={2:N0} dh={3}", s.ShowName(),
                    s.Tl.TotalTokens(), Eff(s) == null ? -1 : Eff(s).TotalTokens(), s.Tl.DateHours.Count));
            }
            sb.AppendLine("rawErr=" + lastRawErr);
            sb.AppendLine("zstdDll=" + File.Exists(Path.Combine(Path.GetTempPath(), "dsh-usage", "ZstdSharp.dll")));
            foreach (SessionInfo s in all)
            {
                if (sb.Length > 4000) break;
                long tt = 0; if (s.Tl != null) tt = s.Tl.OffU + s.Tl.OffC + s.Tl.OffO + s.Tl.PkU + s.Tl.PkC + s.Tl.PkO;
                sb.AppendLine(string.Format("{0} total={1} split={2} {3} cost={4:F2}",
                    s.Id.Substring(0, 8), s.Total, tt, (s.Approx ? "≈" : "真"), s.Tl == null ? 0 : s.Tl.TotalCost()));
            }
            sb.AppendLine("lblEst=" + lblEst.Text);
            sb.AppendLine("tray=" + (tray != null) + " trayCfg=" + cfg.Tray + " hotkeyCfg=" + cfg.Hotkey + " hotkeyOk=" + hotkeyOk);
            sb.AppendLine("peakCheck holiday1001=" + Peak.IsPeak(new System.DateTime(2026, 10, 1, 10, 0, 0))
                + " workday0920=" + Peak.IsPeak(new System.DateTime(2026, 9, 20, 10, 0, 0))
                + " sat1114=" + Peak.IsPeak(new System.DateTime(2026, 11, 14, 10, 0, 0))
                + " wed1118am=" + Peak.IsPeak(new System.DateTime(2026, 11, 18, 10, 0, 0))
                + " wed1118pm=" + Peak.IsPeak(new System.DateTime(2026, 11, 18, 20, 0, 0)));
            sb.AppendLine("holidays=" + Peak.Holidays.Count + " workExtra=" + Peak.WorkExtra.Count
                + " priceOff=" + Pricing.OffMiss + "/" + Pricing.OffHit + "/" + Pricing.OffOut
                + " pricePeak=" + Pricing.PkMiss + "/" + Pricing.PkHit + "/" + Pricing.PkOut);
            sb.AppendLine("budget=" + cfg.Budget + " monthCost=" + monthCost.ToString("F2") + " lbl=" + lblBudget.Text);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            exiting = true;
            Close();
        }

        public void Shot(string path, string checks, bool chain, string ra, string rb)
        {
            Show();
            Application.DoEvents();
            WaitBuild(60);
            if (ra != null && rb != null)
            {
                try
                {
                    rangeA = DateTime.Parse(ra.Replace('T', ' '));
                    rangeB = DateTime.Parse(rb.Replace('T', ' '));
                    timeIdx = 4;
                    RefreshView(true);
                    WaitBuild(20);
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(checks))
            {
                foreach (string part in checks.Split(','))
                {
                    int idx;
                    if (int.TryParse(part.Trim(), out idx) && idx >= 0 && idx < shown.Count)
                        checkedIds[shown[idx].Id] = true;
                }
                RefreshView(true);
                Application.DoEvents();
            }
            if (chain && chkChain != null) { chkChain.Checked = true; Application.DoEvents(); }
            System.Threading.Thread.Sleep(350);
            Application.DoEvents();
            var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
            exiting = true;
            Close();
        }

        // 抓真实屏幕渲染（走 WM_PAINT 之后再 CopyFromScreen），用于核对“用户实际所见”
        public void LiveShot(string path, int forceW)
        {
            if (forceW > 0) ClientSize = new Size(forceW, ClientSize.Height);
            Show();
            TopMost = true;
            Activate();
            Application.DoEvents();
            WaitBuild(60);
            foreach (KeyValuePair<string, int> kv in subCount) { expanded.Add(kv.Key); break; }
            RefreshView(false);
            System.Threading.Thread.Sleep(1600);
            Invalidate(true);
            for (int k = 0; k < 25; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            Rectangle r = Bounds;
            Dump(path + ".txt");
            var bmp = new Bitmap(r.Width, r.Height);
            using (Graphics g = Graphics.FromImage(bmp))
                g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
            exiting = true;
            Close();
        }

        void WaitBuild(int seconds)
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            while (building && sw.ElapsedMilliseconds < seconds * 1000)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(25);
            }
            Application.DoEvents();
        }

        public void ComboShot(string path)
        {
            Show();
            Application.DoEvents();
            WaitBuild(60);
            System.Threading.Thread.Sleep(250);
            cmbKind.OpenPop();
            Application.DoEvents();
            System.Threading.Thread.Sleep(300);
            Application.DoEvents();
            var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            ComboPopup pp = cmbKind.CurrentPop;
            if (pp != null)
            {
                var pbmp = new Bitmap(pp.Width, pp.Height);
                pp.DrawToBitmap(pbmp, new Rectangle(0, 0, pp.Width, pp.Height));
                using (Graphics g = Graphics.FromImage(bmp))
                    g.DrawImage(pbmp, pp.Left - Left, pp.Top - Top);
                pbmp.Dispose();
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
            exiting = true;
            Close();
        }
    }

    // ---------------- 峰谷时段 ----------------
    static class Peak
    {
        public static HashSet<string> Holidays = new HashSet<string>();   // 放假（不计高峰）
        public static HashSet<string> WorkExtra = new HashSet<string>();  // 周末但上班（算工作日）

        static void Range(HashSet<string> set, int y1, int m1, int d1, int y2, int m2, int d2)
        {
            DateTime a = new DateTime(y1, m1, d1);
            DateTime b = new DateTime(y2, m2, d2);
            for (DateTime d = a; d <= b; d = d.AddDays(1)) set.Add(d.ToString("yyyy-MM-dd"));
        }

        // 2026 年国务院办公厅公布的放假安排（国办发明电〔2025〕7号）
        public static void LoadBuiltin()
        {
            Holidays.Clear(); WorkExtra.Clear();
            Range(Holidays, 2026, 1, 1, 2026, 1, 3);
            Range(Holidays, 2026, 2, 15, 2026, 2, 23);
            Range(Holidays, 2026, 4, 4, 2026, 4, 6);
            Range(Holidays, 2026, 5, 1, 2026, 5, 5);
            Range(Holidays, 2026, 6, 19, 2026, 6, 21);
            Range(Holidays, 2026, 9, 25, 2026, 9, 27);
            Range(Holidays, 2026, 10, 1, 2026, 10, 7);

            WorkExtra.Add("2026-01-04");
            WorkExtra.Add("2026-02-14");
            WorkExtra.Add("2026-02-28");
            WorkExtra.Add("2026-05-09");
            WorkExtra.Add("2026-09-20");
            WorkExtra.Add("2026-10-10");
        }

        // 官方规则：工作日 9:00-12:00、14:00-18:00 为高峰；节假日与周末空闲
        public static bool IsPeak(DateTime t)
        {
            string dk = t.ToString("yyyy-MM-dd");
            bool workday;
            if (Holidays.Contains(dk)) workday = false;
            else if (WorkExtra.Contains(dk)) workday = true;
            else workday = t.DayOfWeek != DayOfWeek.Saturday && t.DayOfWeek != DayOfWeek.Sunday;
            if (!workday) return false;
            int m = t.Hour * 60 + t.Minute;
            return (m >= 540 && m < 720) || (m >= 840 && m < 1080);
        }
    }

    // 单轮（一条 assistant 消息）
    class Turn
    {
        public DateTime T;
        public long In, Cache, Out;
        public bool Peak;

        public long Total { get { return In + Cache + Out; } }

        public double Cost()
        {
            double m = Peak ? Pricing.PkMiss : Pricing.OffMiss;
            double h = Peak ? Pricing.PkHit : Pricing.OffHit;
            double o = Peak ? Pricing.PkOut : Pricing.OffOut;
            return (In / 1000000.0) * m + (Cache / 1000000.0) * h + (Out / 1000000.0) * o;
        }
    }

    // ---------------- 原始记录（zstd）解析 ----------------
    static class Raw
    {
        public static string Root(string dataDir)
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(dataDir);
                for (int i = 0; i < 3 && di != null; i++) di = di.Parent;
                if (di != null)
                {
                    string p = Path.Combine(di.FullName, "sessions");
                    if (Directory.Exists(p)) return p;
                    if (Directory.Exists(Path.Combine(di.FullName, ".dsh\\sessions"))) return Path.Combine(di.FullName, ".dsh\\sessions");
                }
            }
            catch { }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string d = Path.Combine(home, ".dsh\\sessions");
            return Directory.Exists(d) ? d : null;
        }

        public static Dictionary<string, string> Index(string root)
        {
            var map = new Dictionary<string, string>();
            if (root == null || !Directory.Exists(root)) return map;
            try
            {
                foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                {
                    string f = Path.Combine(dir, "session.v4.jsonl.zstd");
                    if (!File.Exists(f)) continue;
                    string id = Path.GetFileName(dir);
                    if (id.StartsWith("session-")) id = id.Substring("session-".Length);
                    if (id.Length < 8) continue;
                    map[id] = f;
                }
            }
            catch { }
            return map;
        }

        static long Num(string s, int from, int limit, string key)
        {
            int i = s.IndexOf("\"" + key + "\":", from, StringComparison.Ordinal);
            if (i < 0 || i > limit) return -1;
            i = i + key.Length + 3;
            while (i < s.Length && s[i] == ' ') i++;
            long v = 0;
            bool any = false;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { v = v * 10 + (s[i] - '0'); i++; any = true; }
            return any ? v : -1;
        }

        static string ReadPlain(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(data))
            using (var ds = new ZstdSharp.DecompressionStream(ms))
            using (var outMs = new MemoryStream())
            {
                ds.CopyTo(outMs);
                return Encoding.UTF8.GetString(outMs.ToArray());
            }
        }

        // 逐轮明细：每条 assistant 消息的时间与 token
        public static List<Turn> ParseTurns(string path)
        {
            var list = new List<Turn>();
            string text = ReadPlain(path);
            int pos = 0;
            while (pos < text.Length)
            {
                int nl = text.IndexOf('\n', pos);
                if (nl < 0) nl = text.Length;
                string line = text.Substring(pos, nl - pos);
                pos = nl + 1;
                if (line.IndexOf("\"type\":\"assistant/message\"", StringComparison.Ordinal) < 0) continue;

                long ms = Num(line, 0, Math.Min(line.Length, 200), "time");
                int u = line.IndexOf("\"usage\":{", StringComparison.Ordinal);
                if (ms <= 0 || u < 0) continue;
                long iN = Num(line, u, u + 300, "inputTokens");
                long oN = Num(line, u, u + 300, "outputTokens");
                long cR = Num(line, u, u + 300, "cacheReadTokens");
                if (iN < 0) iN = 0;
                if (oN < 0) oN = 0;
                if (cR < 0) cR = 0;

                DateTime t;
                try { t = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime; }
                catch { continue; }

                var tn = new Turn();
                tn.T = t;
                tn.In = iN;
                tn.Cache = cR;
                tn.Out = oN;
                tn.Peak = Peak.IsPeak(t);
                list.Add(tn);
            }
            return list;
        }

        public static Timeline Parse(string path)
        {
            var tl = new Timeline();
            string text = ReadPlain(path);
            int pos = 0;
            while (pos < text.Length)
            {
                int nl = text.IndexOf('\n', pos);
                if (nl < 0) nl = text.Length;
                string line = text.Substring(pos, nl - pos);
                pos = nl + 1;
                if (line.IndexOf("\"type\":\"assistant/message\"", StringComparison.Ordinal) < 0) continue;

                long ms = Num(line, 0, Math.Min(line.Length, 200), "time");
                int u = line.IndexOf("\"usage\":{", StringComparison.Ordinal);
                if (ms <= 0 || u < 0) continue;
                long iN = Num(line, u, u + 300, "inputTokens");
                long oN = Num(line, u, u + 300, "outputTokens");
                long cR = Num(line, u, u + 300, "cacheReadTokens");
                if (iN < 0) iN = 0;
                if (oN < 0) oN = 0;
                if (cR < 0) cR = 0;

                DateTime t;
                try { t = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime; }
                catch { continue; }

                bool peak = Peak.IsPeak(t);
                tl.Add(iN, cR, oN, peak);

                string dk = t.ToString("yyyy-MM-dd");
                Slot ds;
                if (!tl.Days.TryGetValue(dk, out ds)) { ds = new Slot(); tl.Days[dk] = ds; }
                ds.Add(iN, cR, oN, peak);
                tl.Hours[t.Hour].Add(iN, cR, oN, peak);

                string hk = t.ToString("yyyy-MM-dd HH");
                Slot hs;
                if (!tl.DateHours.TryGetValue(hk, out hs)) { hs = new Slot(); tl.DateHours[hk] = hs; }
                hs.Add(iN, cR, oN, peak);
            }
            tl.FromRaw = true;
            tl.RawSize = new FileInfo(path).Length;
            tl.RawTicks = File.GetLastWriteTimeUtc(path).Ticks;
            return tl;
        }
    }

    // ---------------- 自动探测 ----------------
    static class Detector
    {
        // 找 dsh 会话缓存目录：先常规位置，再扫各盘
        public static string FindDataDir()
        {
            var cands = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            cands.Add(Path.Combine(home, ".dsh\\storages\\session_projcache\\sessions"));
            string dshHome = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrEmpty(dshHome))
                cands.Add(Path.Combine(dshHome, "storages\\session_projcache\\sessions"));

            string[] drives = new string[] { "C:", "D:", "E:", "F:", "G:" };
            foreach (string d in drives)
                cands.Add(d + "\\.dsh\\storages\\session_projcache\\sessions");

            foreach (string d in drives)
            {
                string users = d + "\\Users";
                try
                {
                    if (!Directory.Exists(users)) continue;
                    foreach (string u in Directory.GetDirectories(users))
                        cands.Add(Path.Combine(u, ".dsh\\storages\\session_projcache\\sessions"));
                }
                catch { }
            }

            string found = null;
            foreach (string c in cands)
            {
                try
                {
                    if (Directory.Exists(c) && Directory.GetFiles(c, "*.json").Length > 0) return c;
                    if (found == null && Directory.Exists(c)) found = c;
                }
                catch { }
            }
            return found;
        }

    }

    // ---------------- 设置窗口 ----------------

    // ---------------- 使用教程窗 ----------------
    class GuideForm : Form
    {
        public bool NoShow = false;
        ScrollHost body;

        public void ToBottom()
        {
            try { body.AutoScrollPosition = new Point(0, body.VerticalScroll.Maximum); } catch { }
        }

        class ScrollHost : Panel
        {
            public ScrollHost()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                AutoScroll = true;
                BackColor = Color.White;
            }
        }

        public GuideForm(Config cfg, bool fromStartup)
        {
            Text = "使用教程";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.S(764), Ui.S(642));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = Ui.F(9f);
            BackColor = Color.White;
            ShowInTaskbar = false;
            KeyPreview = true;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = Ui.S(88);
            head.BackColor = Color.White;
            Ui.Buf(head);

            var line1 = new Panel();
            line1.Dock = DockStyle.Bottom;
            line1.Height = Ui.S(1);
            line1.BackColor = Ui.Line;

            var t1 = new Label();
            t1.Text = "使用教程";
            t1.Font = Ui.FB(17f);
            t1.ForeColor = Ui.Ink;
            t1.AutoSize = true;
            t1.BackColor = Color.White;
            t1.Location = new Point(Ui.S(30), Ui.S(16));

            var t2 = new Label();
            t2.Text = "三分钟看完，每个按钮、每列数字是什么就都清楚了";
            t2.Font = Ui.F(9f);
            t2.ForeColor = Ui.Ink3;
            t2.AutoSize = true;
            t2.BackColor = Color.White;
            t2.Location = new Point(Ui.S(32), Ui.S(56));

            head.Controls.Add(t1);
            head.Controls.Add(t2);
            head.Controls.Add(line1);

            var foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = Ui.S(62);
            foot.BackColor = Ui.Bg;
            Ui.Buf(foot);

            var chk = new FlatCheck();
            chk.Text = "以后不再自动弹出";
            chk.BackColor = Ui.Bg;
            chk.Size = new Size(Ui.S(210), Ui.S(30));
            chk.Location = new Point(Ui.S(24), Ui.S(16));

            var ok = Ui.Primary("开始使用", Ui.S(116), Ui.S(32));
            ok.Location = new Point(ClientSize.Width - Ui.S(116) - Ui.S(24), Ui.S(15));
            ok.DialogResult = DialogResult.OK;
            ok.Click += new EventHandler(delegate { if (fromStartup) NoShow = chk.Checked; });

            foot.Controls.Add(ok);
            if (fromStartup) foot.Controls.Add(chk);

            var host = new ScrollHost();
            host.Dock = DockStyle.Fill;
            body = host;

            string[][] items = new string[][]
            {
                new string[] { "它读的是你电脑上的哪些东西",
                    "只读两个地方：~/.dsh/storages/session_projcache/sessions 里的会话缓存（用来算 token），和 ~/.dsh/sessions 里的原始记录（用来算每次请求落在闲时还是高峰）。全程本地读取，不联网、不上传、不往外发任何东西。" },
                new string[] { "最上面那四个数字",
                    "总 token 是所有会话加起来的总量；本月花费按月累计，闲时和高峰分开计价。会话数那格顺带写了主对话、子智能体各多少。高峰占比就是高峰时段 token 占总量的比例，越高说明请求越集中在偏贵的时间段。" },
                new string[] { "中间那一行筛选",
                    "搜索框按标题过滤；全部 / 今天 / 近 7 天 / 近 30 天 / 自定义 切时间段；类型下拉可以只看主对话或只看子体；「只看命中」筛缓存命中率高的会话；「连带子体」勾一个主会话，会把它下面的子会话一起带上。" },
                new string[] { "表格怎么点",
                    "点表头任意一列都能排序，再点一次切换升序降序。会话名左边的小三角是子会话折叠，点开才展开它下面的子体。最左边的方框勾选之后，底部会实时算出这几个合起来多少 token、多少钱。" },
                new string[] { "底部那三个按钮",
                    "「对比选中」选两个以上会话，横向比 token 构成、费用、命中率，还会给出差值和倍数；「复制合计」把当前合计贴成一段文字，方便发给别人；「导出 CSV」整表导出，Excel 里直接能打开。" },
                new string[] { "设置里能改什么",
                    "会话目录（点「自动检测」能自己找）、月预算（填了就会显示已用百分之几）、关窗口收托盘、Ctrl+Alt+D 热键，还有闲时 / 高峰这六个单价。默认单价是 DeepSeek 官方 deepseek-flash 的价格，换了模型自己改一下就行。" },
                new string[] { "费用是怎么算出来的",
                    "按每次请求发生的时刻判断闲时还是高峰，把未命中输入、命中输入、输出三类 token 分开累加，再各自乘单价。所以这是按官方价估算出来的数字，和官方账单通常差几个百分点，不是精确扣费。" },
                new string[] { "列表是空的怎么办",
                    "说明这台机器上还没找到 dsh 的会话缓存。点右上角【设置】，用右侧「…」选目录，或者点「自动检测」。等你用 dsh 聊过天、产生了会话，再点【刷新】就有数据了。" }
            };

            int y = Ui.S(22);
            int w = Ui.S(636);
            int i = 0;
            foreach (string[] it in items)
            {
                i++;

                Label num = new Label();
                num.Text = i.ToString("00");
                num.Font = Ui.FB(9f);
                num.ForeColor = Ui.Accent;
                num.AutoSize = true;
                num.BackColor = Color.White;
                num.Location = new Point(Ui.S(30), y + Ui.S(3));

                Label ti = new Label();
                ti.Text = it[0];
                ti.Font = Ui.FB(10.5f);
                ti.ForeColor = Ui.Ink;
                ti.AutoSize = true;
                ti.BackColor = Color.White;
                ti.Location = new Point(Ui.S(64), y);

                y += ti.PreferredHeight + Ui.S(5);

                Size sz = TextRenderer.MeasureText(it[1], Ui.F(9f), new Size(w, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                Label bd = new Label();
                bd.Text = it[1];
                bd.Font = Ui.F(9f);
                bd.ForeColor = Ui.Ink2;
                bd.AutoSize = false;
                bd.Width = w;
                bd.Height = sz.Height + Ui.S(4);
                bd.Location = new Point(Ui.S(64), y);
                bd.BackColor = Color.White;

                y += bd.Height + Ui.S(18);

                host.Controls.Add(num);
                host.Controls.Add(ti);
                host.Controls.Add(bd);
            }

            Label tail = new Label();
            tail.Text = "随时点右上角【使用教程】可以再打开这一页。";
            tail.Font = Ui.F(9f);
            tail.ForeColor = Ui.Ink3;
            tail.AutoSize = true;
            tail.BackColor = Color.White;
            tail.Location = new Point(Ui.S(30), y);
            host.Controls.Add(tail);
            y += tail.PreferredHeight + Ui.S(16);

            host.AutoScrollMinSize = new Size(0, y);

            Controls.Add(host);
            Controls.Add(foot);
            Controls.Add(head);

            try
            {
                Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                if (Height > wa.Height - Ui.S(20)) Height = wa.Height - Ui.S(20);
                if (Width > wa.Width - Ui.S(20)) Width = wa.Width - Ui.S(20);
            }
            catch { }

            KeyDown += new KeyEventHandler(delegate(object s2, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Escape) Close();
            });
        }
    }

    class SettingsForm : Form
    {
        public Config Result;
        TextBox txtDir;
        CheckBox chkTray, chkHot;
        SpinBox numInt, numBudget;
        SpinBox numOMiss, numOHit, numOOut, numPMiss, numPHit, numPOut;

        public SettingsForm(Config cfg)
        {
            Result = cfg;
            AutoScaleMode = AutoScaleMode.None;
            Text = "设置";
            ClientSize = new Size(600, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("微软雅黑", 9f);
            BackColor = Ui.Bg;

            int yInt = 104;
            int yBudget = 140;
            int yTray = 170;
            int yOff = 208;
            int yPk = 234;
            int yTip = 262;
            int yBtn = 290;

            var l1 = new Label(); l1.Text = "会话缓存目录"; l1.AutoSize = true; l1.Location = new Point(16, 18);
            txtDir = new TextBox(); txtDir.BorderStyle = BorderStyle.FixedSingle; txtDir.Location = new Point(16, 40); txtDir.Width = 470; txtDir.Text = cfg.DataDir;
            var b1 = Ui.Ghost("…", 32, 24); b1.Location = new Point(492, 40);
            b1.Click += new EventHandler(delegate
            {
                var fb = new FolderBrowserDialog();
                if (Directory.Exists(txtDir.Text)) fb.SelectedPath = txtDir.Text;
                if (fb.ShowDialog(this) == DialogResult.OK) txtDir.Text = fb.SelectedPath;
            });

            var l3 = new Label(); l3.Text = "自动刷新间隔"; l3.AutoSize = true;
            l3.Location = new Point(16, yInt + 2);
            numInt = new SpinBox(); numInt.Location = new Point(102, yInt); numInt.Width = 84;
            numInt.Unit = "秒";
            numInt.Minimum = 3; numInt.Maximum = 600; numInt.Value = Math.Max(3, Math.Min(600, cfg.IntervalSec));

            var ok = Ui.Primary("确定", 84, 28); ok.Location = new Point(400, yBtn);
            ok.DialogResult = DialogResult.OK;
            ok.Click += new EventHandler(delegate
            {
                Result.DataDir = txtDir.Text.Trim();
                Result.IntervalSec = (int)numInt.Value;
                Result.Budget = (double)numBudget.Value;
                Result.Tray = chkTray.Checked;
                Result.Hotkey = chkHot.Checked;
                Result.PriceOffMiss = (double)numOMiss.ValueD;
                Result.PriceOffHit = (double)numOHit.ValueD;
                Result.PriceOffOut = (double)numOOut.ValueD;
                Result.PricePkMiss = (double)numPMiss.ValueD;
                Result.PricePkHit = (double)numPHit.ValueD;
                Result.PricePkOut = (double)numPOut.ValueD;
            });
            var cancel = Ui.Ghost("取消", 84, 28); cancel.Location = new Point(492, yBtn);
            cancel.DialogResult = DialogResult.Cancel;

            var btnAuto = Ui.Ghost("自动检测", 100, 28); btnAuto.Location = new Point(16, 64);
            var lblAuto = new Label(); lblAuto.AutoSize = true; lblAuto.ForeColor = Color.FromArgb(130, 132, 142);
            lblAuto.Text = "点一下自动找 dsh 会话目录，找不到再手动填";
            lblAuto.Location = new Point(124, 70);
            btnAuto.Click += new EventHandler(delegate
            {
                string d2 = Detector.FindDataDir();
                txtDir.Text = d2 != null ? d2 : "(没找到)";
                MessageBox.Show(d2 != null ? "已找到 dsh 会话缓存目录。" : "没找到，请手动选择目录。",
                    "自动检测", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });

            var l4 = new Label();
            l4.Text = "月度预算（元，填 0 不启用）";
            l4.AutoSize = true;
            l4.Location = new Point(16, yBudget);

            numBudget = new SpinBox();
            numBudget.Location = new Point(206, yBudget - 4);
            numBudget.Width = 104;
            numBudget.Unit = "元";
            numBudget.Maximum = 100000;
            numBudget.Step = 5m;
            numBudget.Value = (int)Math.Max(0, Math.Min(100000, cfg.Budget));

            chkTray = new FlatCheck();
            chkTray.Text = "关窗口时收到托盘（后台继续刷新）";
            chkTray.BackColor = Ui.Bg;
            chkTray.Size = new Size(300, 30);
            chkTray.Checked = cfg.Tray;
            chkTray.Location = new Point(16, yTray);

            chkHot = new FlatCheck();
            chkHot.Text = "热键 Ctrl+Alt+D 唤起";
            chkHot.BackColor = Ui.Bg;
            chkHot.Size = new Size(210, 30);
            chkHot.Checked = cfg.Hotkey;
            chkHot.Location = new Point(320, yTray);

            var lblHoliday = new Label();
            lblHoliday.AutoSize = true;
            lblHoliday.ForeColor = Color.FromArgb(150, 152, 162);
            lblHoliday.Location = new Point(16, yTip);
            lblHoliday.Text = "单价单位：元/百万 token；默认＝DeepSeek 官方 deepseek-flash 价";

            var l5 = new Label();
            l5.AutoSize = true;
            l5.Text = "闲时单价（未命中 / 命中 / 输出）";
            l5.Location = new Point(16, yOff);

            numOMiss = MakeNum(cfg.PriceOffMiss, 216, yOff - 6);
            numOHit = MakeNum(cfg.PriceOffHit, 288, yOff - 6);
            numOOut = MakeNum(cfg.PriceOffOut, 360, yOff - 6);

            var l6 = new Label();
            l6.AutoSize = true;
            l6.Text = "高峰单价（未命中 / 命中 / 输出）";
            l6.Location = new Point(16, yPk);

            numPMiss = MakeNum(cfg.PricePkMiss, 216, yPk - 6);
            numPHit = MakeNum(cfg.PricePkHit, 288, yPk - 6);
            numPOut = MakeNum(cfg.PricePkOut, 360, yPk - 6);

            Controls.Add(l1); Controls.Add(txtDir); Controls.Add(b1);
            Controls.Add(btnAuto); Controls.Add(lblAuto);
            Controls.Add(l3); Controls.Add(numInt);
            Controls.Add(l4); Controls.Add(numBudget);
            Controls.Add(chkTray); Controls.Add(chkHot);
            Controls.Add(l5); Controls.Add(numOMiss); Controls.Add(numOHit); Controls.Add(numOOut);
            Controls.Add(l6); Controls.Add(numPMiss); Controls.Add(numPHit); Controls.Add(numPOut);
            Controls.Add(lblHoliday);
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Ui.ScaleTree(this);
        }

        static SpinBox MakeNum(double v, int x, int y)
        {
            var n = new SpinBox();
            n.Location = new Point(x, y);
            n.Width = 64;
            n.Decimals = 3;
            n.Step = 0.01m;
            n.MinimumD = 0m;
            n.MaximumD = 1000m;
            n.ValueD = Math.Max(0m, Math.Min(1000m, (decimal)v));
            return n;
        }
    }

    // ---------------- 自定义时段 ----------------
    class RangeForm : Form
    {
        public DateTime StartAt, EndAt;
        FlatDate dpA, dpB;

        public RangeForm(DateTime? a, DateTime? b)
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "选择时段";
            ClientSize = new Size(392, 238);
            MinimumSize = new Size(392, 238);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.Bg;
            Font = new Font("微软雅黑", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            DateTime s0 = a.HasValue ? a.Value : DateTime.Today;
            DateTime s1 = b.HasValue ? b.Value : DateTime.Now;

            var l1 = new Label();
            l1.Text = "开始";
            l1.AutoSize = true;
            l1.Location = new Point(20, 24);

            dpA = MakePicker(s0);
            dpA.Location = new Point(64, 18);
            dpA.Width = 216;

            var l2 = new Label();
            l2.Text = "结束";
            l2.AutoSize = true;
            l2.Location = new Point(20, 60);

            dpB = MakePicker(s1);
            dpB.Location = new Point(64, 54);
            dpB.Width = 216;

            var lblTip = new Label();
            lblTip.AutoSize = true;
            lblTip.ForeColor = Color.FromArgb(140, 142, 152);
            lblTip.Location = new Point(20, 92);
            lblTip.Text = "按小时精确裁剪；无逐小时记录者按整天估算";

            int qy = 112;
            AddQuick("今天", 20, qy, 70, delegate { SetRange(DateTime.Today, DateTime.Today.AddDays(1)); });
            AddQuick("昨天", 96, qy, 70, delegate { SetRange(DateTime.Today.AddDays(-1), DateTime.Today); });
            AddQuick("最近 24 小时", 172, qy, 116, delegate { SetRange(DateTime.Now.AddHours(-24), DateTime.Now.AddMinutes(1)); });
            AddQuick("本周", 20, qy + 32, 70, delegate
            {
                DateTime m = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + (DateTime.Today.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));
                SetRange(m, DateTime.Now);
            });
            AddQuick("上月", 96, qy + 32, 70, delegate
            {
                DateTime first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
                SetRange(first, first.AddMonths(1));
            });

            var ok = Ui.Primary("确定", 90, 30);
            ok.Location = new Point(190, 190);
            ok.Click += new EventHandler(delegate
            {
                StartAt = dpA.Value;
                EndAt = dpB.Value;
                if (EndAt <= StartAt)
                {
                    MessageBox.Show("结束时间要比开始时间晚。", "选择时段");
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            });

            var cancel = Ui.Ghost("取消", 90, 30);
            cancel.Location = new Point(288, 190);
            cancel.Click += new EventHandler(delegate { DialogResult = DialogResult.Cancel; Close(); });

            Controls.Add(l1);
            Controls.Add(dpA);
            Controls.Add(l2);
            Controls.Add(dpB);
            Controls.Add(lblTip);
            Controls.Add(ok);
            Controls.Add(cancel);
            Ui.ScaleTree(this);
        }

        static FlatDate MakePicker(DateTime v)
        {
            var p = new FlatDate();
            p.Value = v;
            return p;
        }

        void AddQuick(string text, int x, int y, int w, EventHandler h)
        {
            var b = Ui.Ghost(text, w, 26);
            b.Location = new Point(x, y);
            b.Click += h;
            Controls.Add(b);
        }

        void SetRange(DateTime a, DateTime b)
        {
            dpA.Value = a;
            dpB.Value = b;
        }

        public void CalShot(string path)
        {
            Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(250);
            Application.DoEvents();
            dpA.OpenPop();
            Application.DoEvents();
            System.Threading.Thread.Sleep(250);
            Application.DoEvents();
            Form pp = dpA.CurrentPop;
            int outerW = Width, outerH = Height;
            if (pp != null)
            {
                Rectangle pr = pp.Bounds;
                outerW = Math.Max(outerW, pr.Right - Left);
                outerH = Math.Max(outerH, pr.Bottom - Top);
            }
            var bmp = new Bitmap(outerW + 8, outerH + 8);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            if (pp != null)
            {
                var pb = new Bitmap(pp.Width, pp.Height);
                pp.DrawToBitmap(pb, new Rectangle(0, 0, pp.Width, pp.Height));
                using (Graphics g = Graphics.FromImage(bmp))
                    g.DrawImage(pb, pp.Left - Left, pp.Top - Top);
                pb.Dispose();
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
            Close();
        }
    }

    // ---------------- 逐轮明细窗口 ----------------
    // 逐轮明细表顶部的合计 / 平均条，按 grid 实际列宽对齐
    class TurnSumPanel : Panel
    {
        public DataGridView G;
        public string[] Line1 = new string[0];
        public string[] Line2 = new string[0];

        public TurnSumPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Height = 56;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Pre(this, g);
            if (G == null || G.Columns.Count == 0) return;
            int half = Height / 2;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(250, 251, 254)))
                g.FillRectangle(b, 0, half, Width, Height - half);
            using (Pen p = new Pen(Ui.Line2))
            {
                g.DrawLine(p, 0, 0, Width, 0);
                g.DrawLine(p, 0, half, Width, half);
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawLine(g, Line1, 0, half, true);
            DrawLine(g, Line2, half, half, false);
        }

        void DrawLine(Graphics g, string[] cells, int top, int h, bool bold)
        {
            if (cells == null) return;
            Font f = bold ? Ui.FB(9f) : Ui.F(9f);
            int x = 0;
            for (int i = 0; i < cells.Length && i < G.Columns.Count; i++)
            {
                int cw = G.Columns[i].Width;
                if (cells[i] != null && cells[i].Length > 0 && cw > 8)
                {
                    DataGridViewContentAlignment al = G.Columns[i].DefaultCellStyle.Alignment;
                    TextFormatFlags tf = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                    if (al == DataGridViewContentAlignment.MiddleRight) tf |= TextFormatFlags.Right;
                    else if (al == DataGridViewContentAlignment.MiddleCenter) tf |= TextFormatFlags.HorizontalCenter;
                    else tf |= TextFormatFlags.Left;
                    Rectangle rc = new Rectangle(x + Ui.S(6), top, cw - Ui.S(10), h);
                    Color c = bold ? Ui.Ink : Ui.Ink2;
                    TextRenderer.DrawText(g, cells[i], f, rc, c, tf);
                }
                x += cw;
            }
        }
    }

    // 多个会话的 token 构成对比条（同一比例尺，长度可直接比大小）
    class CompareBars : Panel
    {
        public List<string> Names = new List<string>();
        public List<long> Miss = new List<long>();
        public List<long> Cache = new List<long>();
        public List<long> Out = new List<long>();
        public List<long> Total = new List<long>();

        public CompareBars()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Height = 120;
        }

        // 逻辑高度（ScaleTree 会再乘 K，这里只按逻辑像素算）
        public static int LogicalHeight(int n)
        {
            if (n < 1) n = 1;
            double bh, gap;
            BarSize(n, out bh, out gap);
            return (int)Math.Round(26 + n * (bh + gap) + 22);
        }

        static void BarSize(int n, out double bh, out double gap)
        {
            if (n <= 4) { bh = 18; gap = 18; }
            else if (n <= 6) { bh = 15; gap = 13; }
            else { bh = 12; gap = 9; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Pre(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int n = Names.Count;
            if (n == 0) return;
            long mx = 1;
            foreach (long v in Total) if (v > mx) mx = v;

            int leftPad = Ui.S(120), rightPad = Ui.S(16);
            int barW = Width - leftPad - rightPad;
            if (barW < 60) return;

            double bhd, gapd;
            BarSize(n, out bhd, out gapd);
            int bh = Ui.S((int)Math.Round(bhd)), gap = Ui.S((int)Math.Round(gapd)), y0 = Ui.S(26);
            bool compact = n > 6;
            int maxBar = compact ? Math.Max(Ui.S(60), barW - Ui.S(158)) : barW;
            double scale = (double)maxBar / mx;

            Font fb = Ui.FB(9f), f = Ui.F(8.5f);
            for (int k = 0; k < n; k++)
            {
                int y = y0 + k * (bh + gap);
                TextRenderer.DrawText(g, Names[k], fb, new Rectangle(Ui.S(12), y, leftPad - Ui.S(18), bh), Ui.Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                int wMiss = (int)Math.Round(Miss[k] * scale);
                int wCache = (int)Math.Round(Cache[k] * scale);
                int wOut = (int)Math.Round(Out[k] * scale);

                int x = leftPad;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(246, 178, 107)))
                { if (wMiss > 0) g.FillRectangle(b, new Rectangle(x, y, wMiss, bh)); }
                x += wMiss;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(96, 156, 247)))
                { if (wCache > 0) g.FillRectangle(b, new Rectangle(x, y, wCache, bh)); }
                x += wCache;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(47, 107, 224)))
                { if (wOut > 0) g.FillRectangle(b, new Rectangle(x, y, wOut, bh)); }
                x += wOut;

                string txt = string.Format("命中 {0}%  ·  {1}", Total[k] > 0 ? (int)(Cache[k] * 100.0 / Total[k]) : 0, Fmt2(Total[k]));
                if (compact)
                    TextRenderer.DrawText(g, txt, f, new Rectangle(leftPad + maxBar + Ui.S(8), y, rightPad + Ui.S(150), bh), Ui.Ink3,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                else
                    TextRenderer.DrawText(g, txt, f, new Rectangle(leftPad, y + bh + Ui.S(1), barW, Ui.S(14)), Ui.Ink3,
                        TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }

            // 图例
            int lx = leftPad, ly = y0 + n * (bh + gap) + Ui.S(2);
            Legend(g, ref lx, ly, Color.FromArgb(246, 178, 107), "未命中输入");
            Legend(g, ref lx, ly, Color.FromArgb(96, 156, 247), "缓存命中");
            Legend(g, ref lx, ly, Color.FromArgb(47, 107, 224), "输出");
        }

        void Legend(Graphics g, ref int x, int y, Color c, string text)
        {
            using (SolidBrush b = new SolidBrush(c))
                g.FillRectangle(b, new Rectangle(x, y + Ui.S(3), Ui.S(10), Ui.S(10)));
            Font f = Ui.F(8.5f);
            Size sz = TextRenderer.MeasureText(text, f);
            TextRenderer.DrawText(g, text, f, new Rectangle(x + Ui.S(14), y, sz.Width + Ui.S(4), Ui.S(16)), Ui.Ink3,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            x += Ui.S(14) + sz.Width + Ui.S(16);
        }

        public static string Fmt2(long n)
        {
            if (n >= 1000000) return string.Format("{0:F2}M", n / 1000000.0);
            if (n >= 1000) return string.Format("{0:F1}K", n / 1000.0);
            return n.ToString();
        }
    }

    // 多个会话并排对比（勾几个比几个）
    class CompareForm : Form
    {
        readonly int ncol;
        readonly DataGridView gv;

        public CompareForm(SessionInfo[] ss)
        {
            ncol = ss.Length;
            AutoScaleMode = AutoScaleMode.None;
            Text = ncol == 2 ? "两个会话对比" : ncol + " 个会话对比";
            int bw = ncol == 2 ? 780 : Math.Min(1400, 240 + ncol * 132);
            int bh = CompareBars.LogicalHeight(ncol);
            int rows = ncol == 2 ? 9 : 10;
            int hh = Math.Max(660, 60 + bh + 8 + (rows * 28 + 44) + 72 + 16);
            ClientSize = new Size(bw, hh);
            MinimumSize = new Size(700, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.Bg;
            Font = new Font("微软雅黑", 9f);

            long[] ms = new long[ncol], ca = new long[ncol], ou = new long[ncol], tt = new long[ncol];
            double[] off = new double[ncol], pk = new double[ncol];
            int[] tn = new int[ncol];
            for (int k = 0; k < ncol; k++)
            {
                Timeline t = ss[k].RangeTl != null ? ss[k].RangeTl : ss[k].Tl;
                if (t != null)
                {
                    ms[k] = t.OffU + t.PkU;
                    ca[k] = t.OffC + t.PkC;
                    ou[k] = t.OffO + t.PkO;
                    off[k] = t.OffCost();
                    pk[k] = t.PkCost();
                    tt[k] = t.TotalTokens();
                }
                else
                {
                    ms[k] = ss[k].Total; ca[k] = ss[k].Cache; tt[k] = ss[k].Total;
                    off[k] = 0; pk[k] = 0;
                }
                tn[k] = ss[k].Turns;
            }

            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 60;
            head.BackColor = Color.White;

            Label ttl = new Label();
            ttl.AutoSize = true;
            ttl.Location = new Point(16, 10);
            ttl.Font = Ui.FB(11f);
            ttl.Text = ncol == 2 ? "两个会话对比" : ncol + " 个会话对比";
            head.Controls.Add(ttl);

            Label sub = new Label();
            sub.AutoSize = true;
            sub.Location = new Point(16, 32);
            sub.ForeColor = Ui.Ink3;
            sub.Font = Ui.F(8.5f);
            sub.Text = "同一比例尺：条形长度＝token 总量，颜色段＝未命中 / 缓存命中 / 输出 的构成";
            head.Controls.Add(sub);

            var bars = new CompareBars();
            for (int k = 0; k < ncol; k++)
            {
                string nm = ss[k].ShowName();
                bars.Names.Add((ss[k].IsSub ? "[子] " : "") + (nm.Length > 16 ? nm.Substring(0, 16) + "…" : nm));
                bars.Miss.Add(ms[k]);
                bars.Cache.Add(ca[k]);
                bars.Out.Add(ou[k]);
                bars.Total.Add(tt[k]);
            }
            bars.Dock = DockStyle.Top;
            bars.Height = bh;

            gv = new DataGridView();
            gv.ReadOnly = true;
            gv.AllowUserToAddRows = false;
            gv.AllowUserToDeleteRows = false;
            gv.AllowUserToResizeRows = false;
            gv.RowHeadersVisible = false;
            gv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            gv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gv.MultiSelect = false;
            gv.BackgroundColor = Color.White;
            gv.BorderStyle = BorderStyle.None;
            gv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            gv.EnableHeadersVisualStyles = false;
            gv.ColumnHeadersHeight = Ui.S(34);
            gv.RowTemplate.Height = Ui.S(28);
            gv.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            gv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 246, 250);
            gv.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(108, 117, 135);
            gv.DefaultCellStyle.Font = new Font("微软雅黑", 9f);
            gv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(241, 246, 254);
            gv.DefaultCellStyle.SelectionForeColor = Color.Black;
            gv.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(252, 253, 255);
            gv.GridColor = Color.FromArgb(237, 240, 245);
            gv.Dock = DockStyle.Fill;

            gv.Columns.Add("k", "指标");
            for (int k = 0; k < ncol; k++)
                gv.Columns.Add("c" + k, Nm(ss, k) + " · " + Short(ss[k]));
            if (ncol == 2)
            {
                gv.Columns.Add("d", "差值 (A-B)");
                gv.Columns.Add("r", "A / B");
            }
            for (int i = 1; i < gv.Columns.Count; i++)
                gv.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            AddLine("轮数", Build(delegate(int k) { return tn[k].ToString("N0"); }),
                ncol == 2 ? (tn[0] - tn[1]).ToString("N0") : "", ncol == 2 ? Ratio(tn[0], tn[1]) : "");
            AddLine("未命中输入", Build(delegate(int k) { return ms[k].ToString("N0"); }),
                ncol == 2 ? (ms[0] - ms[1]).ToString("N0") : "", ncol == 2 ? Ratio(ms[0], ms[1]) : "");
            AddLine("缓存命中", Build(delegate(int k) { return ca[k].ToString("N0"); }),
                ncol == 2 ? (ca[0] - ca[1]).ToString("N0") : "", ncol == 2 ? Ratio(ca[0], ca[1]) : "");
            AddLine("输出", Build(delegate(int k) { return ou[k].ToString("N0"); }),
                ncol == 2 ? (ou[0] - ou[1]).ToString("N0") : "", ncol == 2 ? Ratio(ou[0], ou[1]) : "");
            AddLine("总 token", Build(delegate(int k) { return tt[k].ToString("N0"); }),
                ncol == 2 ? (tt[0] - tt[1]).ToString("N0") : "", ncol == 2 ? Ratio(tt[0], tt[1]) : "");
            AddLine("缓存命中率", Build(delegate(int k) { return Pct(ca[k], tt[k]); }),
                ncol == 2 ? (PctNum(ca[0], tt[0]) - PctNum(ca[1], tt[1])).ToString("F1") + " pp" : "",
                ncol == 2 ? Ratio2(PctNum(ca[0], tt[0]), PctNum(ca[1], tt[1])) : "");
            if (ncol == 2)
                AddLine("合计费用", Build(delegate(int k) { return string.Format("¥{0:F2}（闲 {1:F2} / 峰 {2:F2}）", off[k] + pk[k], off[k], pk[k]); }),
                    Money(off[0] + pk[0] - off[1] - pk[1]), Ratio2(off[0] + pk[0], off[1] + pk[1]));
            else
            {
                AddLine("合计费用", Build(delegate(int k) { return string.Format("¥{0:F2}", off[k] + pk[k]); }), "", "");
                AddLine("闲 / 峰", Build(delegate(int k) { return string.Format("{0:F2} / {1:F2}", off[k], pk[k]); }), "", "");
            }
            AddLine("平均每轮 token", Build(delegate(int k) { return (tt[k] / Math.Max(1, tn[k])).ToString("N0"); }),
                "", ncol == 2 ? Ratio(tt[0] / Math.Max(1, tn[0]), tt[1] / Math.Max(1, tn[1])) : "");
            AddLine("平均每轮费用", Build(delegate(int k) { return string.Format("¥{0:F3}", (off[k] + pk[k]) / Math.Max(1, tn[k])); }),
                "", ncol == 2 ? Ratio2((off[0] + pk[0]) / Math.Max(1, tn[0]), (off[1] + pk[1]) / Math.Max(1, tn[1])) : "");

            var foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = 72;
            foot.BackColor = Ui.Bg;

            Label concl = new Label();
            concl.AutoSize = false;
            concl.Dock = DockStyle.Fill;
            concl.Padding = new Padding(Ui.S(16), Ui.S(10), Ui.S(16), 0);
            concl.ForeColor = Ui.Ink;
            concl.Font = Ui.F(9f);
            concl.Text = Verdict(ss, ca, tt, off, pk, tn);
            foot.Controls.Add(concl);

            var host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(14, 8, 14, 0);
            host.BackColor = Ui.Bg;
            host.Controls.Add(gv);

            Controls.Add(host);
            Controls.Add(foot);
            Controls.Add(bars);
            Controls.Add(head);
            Ui.ScaleTree(this);
            Ui.Buf(this);

            SizeChanged += delegate { FitCols(); };
            FitCols();
        }

        void FitCols()
        {
            int w = gv.ClientSize.Width;
            if (w < Ui.S(120)) return;
            int k0 = Ui.S(96);
            int tail = ncol == 2 ? Ui.S(92) + Ui.S(46) : 0;
            int per = (w - k0 - tail) / ncol;
            if (per < Ui.S(112)) per = Ui.S(112);
            gv.Columns[0].Width = k0;
            for (int k = 0; k < ncol; k++) gv.Columns[k + 1].Width = per;
            if (ncol == 2)
            {
                gv.Columns[ncol + 1].Width = Ui.S(92);
                gv.Columns[ncol + 2].Width = Ui.S(46);
            }
        }

        string[] Build(Func<int, string> f)
        {
            string[] r = new string[ncol];
            for (int k = 0; k < ncol; k++) r[k] = f(k);
            return r;
        }

        void AddLine(string key, string[] vals, string diff, string ratio)
        {
            List<string> c = new List<string>();
            c.Add(key);
            foreach (string v in vals) c.Add(v);
            if (ncol == 2) { c.Add(diff); c.Add(ratio); }
            int i = gv.Rows.Add(c.ToArray());
            gv.Rows[i].Cells[0].Style.ForeColor = Ui.Ink2;
        }

        static string Short(SessionInfo s)
        {
            string n = s.ShowName();
            return n.Length > 14 ? n.Substring(0, 14) + "…" : n;
        }

        static readonly string[] ColTag = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };

        static string Nm(SessionInfo[] ss, int k)
        {
            return k < ColTag.Length ? ColTag[k] : "#" + (k + 1);
        }

        static string Pct(long v, long t) { return t > 0 ? string.Format("{0:F1}%", v * 100.0 / t) : "—"; }
        static double PctNum(long v, long t) { return t > 0 ? v * 100.0 / t : 0; }
        static string Money(double v) { return string.Format("¥{0:F2}", v); }
        static string Ratio(long a, long b) { return b > 0 ? string.Format("{0:F2}×", (double)a / b) : "—"; }
        static string Ratio2(double a, double b) { return b > 0.000001 ? string.Format("{0:F2}×", a / b) : "—"; }

        static string Verdict(SessionInfo[] ss, long[] ca, long[] tt, double[] off, double[] pk, int[] tn)
        {
            int n = ss.Length;
            double[] rate = new double[n], cost = new double[n], per = new double[n];
            for (int k = 0; k < n; k++)
            {
                rate[k] = tt[k] > 0 ? ca[k] * 100.0 / tt[k] : 0;
                cost[k] = off[k] + pk[k];
                per[k] = tn[k] > 0 ? cost[k] / tn[k] : 0;
            }
            if (n == 2)
            {
                string who = rate[0] >= rate[1] ? "A" : "B";
                double dp = Math.Abs(rate[0] - rate[1]);
                var sb = new StringBuilder();
                sb.AppendFormat("{0} 的缓存命中率高 {1:F1} 个百分点（{2:F1}% vs {3:F1}%）；", who, dp, rate[0], rate[1]);
                if (cost[1] > 0.0001)
                    sb.AppendFormat("花费 A 是 B 的 {0:F2} 倍（¥{1:F2} vs ¥{2:F2}）；", cost[0] / cost[1], cost[0], cost[1]);
                if (per[1] > 0.0001)
                    sb.AppendFormat("单轮成本 A 是 B 的 {0:F2} 倍。", per[0] / per[1]);
                return sb.ToString();
            }
            int hiC = 0, loC = 0, hiM = 0, loM = 0, hiU = 0, loU = 0;
            for (int k = 1; k < n; k++)
            {
                if (rate[k] > rate[hiC]) hiC = k;
                if (rate[k] < rate[loC]) loC = k;
                if (cost[k] > cost[hiM]) hiM = k;
                if (cost[k] < cost[loM]) loM = k;
                if (per[k] > per[hiU]) hiU = k;
                if (per[k] < per[loU]) loU = k;
            }
            var sb2 = new StringBuilder();
            sb2.AppendFormat("命中率最高 {0}（{1:F1}%），最低 {2}（{3:F1}%），差 {4:F1} 个百分点；",
                Nm(ss, hiC), rate[hiC], Nm(ss, loC), rate[loC], Math.Abs(rate[hiC] - rate[loC]));
            sb2.AppendFormat("花费最高 {0}（¥{1:F2}），最低 {2}（¥{3:F2}）",
                Nm(ss, hiM), cost[hiM], Nm(ss, loM), cost[loM]);
            if (cost[loM] > 0.0001) sb2.AppendFormat("，相差 {0:F1} 倍", cost[hiM] / cost[loM]);
            sb2.AppendFormat("；单轮成本最高 {0}（¥{1:F3}），最低 {2}（¥{3:F3}）。",
                Nm(ss, hiU), per[hiU], Nm(ss, loU), per[loU]);
            return sb2.ToString();
        }
    }
    class TurnForm : Form
    {
        List<Turn> turns;
        DataGridView grid;

        public TurnForm(SessionInfo s, List<Turn> list)
        {
            turns = list;
            AutoScaleMode = AutoScaleMode.None;
            string shown2 = s.ShowName();
            Text = "逐轮明细 - " + (shown2.Length > 34 ? shown2.Substring(0, 34) + "…" : shown2);
            ClientSize = new Size(920, 620);
            MinimumSize = new Size(760, 440);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.Bg;
            Font = new Font("微软雅黑", 9f);

            long totIn = 0, totCache = 0, totOut = 0;
            double offCost = 0, pkCost = 0;
            long offTok = 0, pkTok = 0;
            foreach (Turn t in turns)
            {
                totIn += t.In; totCache += t.Cache; totOut += t.Out;
                if (t.Peak) { pkCost += t.Cost(); pkTok += t.Total; }
                else { offCost += t.Cost(); offTok += t.Total; }
            }
            long totAll = totIn + totCache + totOut;
            int pct = totAll > 0 ? (int)((double)totCache / totAll * 100) : 0;

            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 72;
            head.BackColor = Ui.Bg;

            var chart = new TurnChartPanel();
            chart.Dock = DockStyle.Top;
            chart.Height = 176;
            chart.Data = turns;
            chart.BackColor = Color.White;

            var t1 = new Label();
            t1.AutoSize = true;
            t1.Location = new Point(16, 10);
            t1.Font = new Font("微软雅黑", 10.5f, FontStyle.Bold);
            t1.Text = shown2;

            var t2 = new Label();
            t2.AutoSize = true;
            t2.Location = new Point(16, 36);
            t2.ForeColor = Color.FromArgb(110, 112, 122);
            t2.Text = string.Format(
                "{0} 轮  |  总 token {1:N0}  |  缓存命中 {2}%  |  闲时 {3} → ¥{4:F2}   高峰 {5} → ¥{6:F2}   合计 ¥{7:F2}",
                turns.Count, totAll, pct, FmtShort(offTok), offCost, FmtShort(pkTok), pkCost, offCost + pkCost);

            var tip = new Label();
            tip.AutoSize = true;
            tip.Location = new Point(16, 54);
            tip.ForeColor = Ui.Ink3;
            tip.Text = string.Format("{0}    ·    按官方峰谷单价逐轮折算；橙色底＝高峰时段发生的轮次",
                turns[0].T.ToString("MM-dd HH:mm") + " ~ " + turns[turns.Count - 1].T.ToString("MM-dd HH:mm"));
            if (s.IsSub)
                tip.Text = "子智能体" + (s.ParentTitle.Length > 0 ? "（属于主对话：" + s.ParentTitle + "）" : "") + "    ·    " + tip.Text;

            head.Controls.Add(t1);
            head.Controls.Add(t2);
            head.Controls.Add(tip);

            grid = new DataGridView();
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = Ui.S(32);
            grid.RowTemplate.Height = Ui.S(28);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 246, 250);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(108, 117, 135);
            grid.DefaultCellStyle.Font = new Font("微软雅黑", 9f);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(241, 246, 254);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(252, 253, 255);
            grid.GridColor = Color.FromArgb(237, 240, 245);
            grid.Dock = DockStyle.Fill;

            grid.Columns.Add("t1", "轮次");
            grid.Columns.Add("t2", "时间");
            grid.Columns.Add("t3", "未命中输入");
            grid.Columns.Add("t4", "缓存命中");
            grid.Columns.Add("t5", "输出");
            grid.Columns.Add("t6", "本轮 token");
            grid.Columns.Add("t7", "费用 (元)");
            grid.Columns.Add("t8", "档位");
            grid.Columns[0].FillWeight = 60;
            grid.Columns[1].FillWeight = 130;
            grid.Columns[7].FillWeight = 70;
            grid.Columns[7].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            for (int i = 2; i <= 6; i++)
                grid.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            int cap = Math.Min(turns.Count, 500);
            var peakStyle = new DataGridViewCellStyle();
            peakStyle.BackColor = Color.FromArgb(253, 244, 232);
            for (int i = turns.Count - 1; i >= turns.Count - cap; i--)
            {
                Turn t = turns[i];
                int idx = grid.Rows.Add("#" + (i + 1), t.T.ToString("MM-dd HH:mm:ss"),
                    t.In.ToString("N0"), t.Cache.ToString("N0"), t.Out.ToString("N0"),
                    t.Total.ToString("N0"), string.Format("{0:F3}", t.Cost()),
                    t.Peak ? "高峰" : "闲时");
                if (t.Peak)
                {
                    for (int c = 0; c < grid.Columns.Count; c++) grid.Rows[idx].Cells[c].Style = peakStyle;
                }
            }

            var foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = 44;
            foot.BackColor = Ui.Bg;

            var lblFoot = new Label();
            lblFoot.AutoSize = true;
            lblFoot.Location = new Point(16, 14);
            lblFoot.ForeColor = Ui.Ink3;
            lblFoot.Text = cap < turns.Count
                ? string.Format("共 {0} 轮，这里显示最近的 {1} 轮（按时间倒序）", turns.Count, cap)
                : string.Format("共 {0} 轮，已全部列出（按时间倒序）", turns.Count);

            var btnCsv = Ui.Ghost("导出明细 CSV", 120, 28);
            btnCsv.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCsv.Location = new Point(foot.Width - 136, 8);
            btnCsv.Click += new EventHandler(delegate { Export(); });

            foot.Controls.Add(lblFoot);
            foot.Controls.Add(btnCsv);

            var sumBar = new TurnSumPanel();
            sumBar.G = grid;
            sumBar.Dock = DockStyle.Top;
            int cnt = Math.Max(1, turns.Count);
            string cargo = string.Format("{0:F2}", offCost + pkCost);
            string avgCost = string.Format("{0:F3}", (offCost + pkCost) / cnt);
            sumBar.Line1 = new string[] {
                "合计", turns.Count + " 轮",
                totIn.ToString("N0"), totCache.ToString("N0"), totOut.ToString("N0"),
                totAll.ToString("N0"), cargo, "" };
            sumBar.Line2 = new string[] {
                "平均每轮", "",
                (totIn / cnt).ToString("N0"), (totCache / cnt).ToString("N0"), (totOut / cnt).ToString("N0"),
                (totAll / cnt).ToString("N0"), avgCost, "" };

            var gridHost = new Panel();
            gridHost.Dock = DockStyle.Fill;
            gridHost.Padding = new Padding(14, 0, 14, 0);
            gridHost.BackColor = Ui.Bg;
            gridHost.Controls.Add(grid);
            gridHost.Controls.Add(sumBar);

            Controls.Add(gridHost);
            Controls.Add(chart);
            Controls.Add(head);
            Controls.Add(foot);
            Ui.ScaleTree(this);
        }

        static string FmtShort(long n)
        {
            if (n >= 1000000) return string.Format("{0:F2}M", n / 1000000.0);
            if (n >= 1000) return string.Format("{0:F1}K", n / 1000.0);
            return n.ToString();
        }

        void Export()
        {
            var dlg = new SaveFileDialog();
            dlg.Filter = "CSV 文件 (*.csv)|*.csv";
            dlg.FileName = "dsh-turns-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var sb = new StringBuilder();
            sb.AppendLine("轮次,时间,未命中输入,缓存命中,输出,本轮token,费用(元),档位");
            for (int i = 0; i < turns.Count; i++)
            {
                Turn t = turns[i];
                sb.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6:F4},{7}",
                    i + 1, t.T.ToString("yyyy-MM-dd HH:mm:ss"), t.In, t.Cache, t.Out, t.Total, t.Cost(),
                    t.Peak ? "高峰" : "闲时"));
            }
            try { File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message, "逐轮明细"); }
        }
    }

    // ---------------- 统计窗口：按天 / 按小时 / 按项目 / 趋势 ----------------
    // 按天花费柱状图（仿 DeepSeek 官网：闲时/高峰两段叠起来）
    class BarChartPanel : Panel
    {
        public List<string> Labels = new List<string>();
        public List<double> OffCost = new List<double>();
        public List<double> PkCost = new List<double>();
        public List<double> OffTok = new List<double>();
        public List<double> PkTok = new List<double>();
        public bool TokMode = false;
        public string Title = "";

        double Get1(int i) { return TokMode ? (i < OffTok.Count ? OffTok[i] : 0) : (i < OffCost.Count ? OffCost[i] : 0); }
        double Get2(int i) { return TokMode ? (i < PkTok.Count ? PkTok[i] : 0) : (i < PkCost.Count ? PkCost[i] : 0); }

        static string TokText(double v)
        {
            if (v >= 1000000) return string.Format("{0:F1}M", v / 1000000.0);
            if (v >= 1000) return string.Format("{0:F0}K", v / 1000.0);
            return v.ToString("F0");
        }

        public BarChartPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            MouseMove += new MouseEventHandler(OnMove);
            MouseLeave += new EventHandler(delegate(object s, EventArgs a) { if (hover >= 0) { hover = -1; Invalidate(); } });
            Resize += new EventHandler(delegate(object s, EventArgs a) { Invalidate(); });
        }

        int hover = -1;

        static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double e = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double f = raw / e;
            double nf;
            if (f <= 1) nf = 1;
            else if (f <= 2) nf = 2;
            else if (f <= 2.5) nf = 2.5;
            else if (f <= 5) nf = 5;
            else nf = 10;
            return nf * e;
        }

        Rectangle PlotArea()
        {
            return new Rectangle(Ui.S(58), Ui.S(34), Width - Ui.S(58) - Ui.S(26), Height - Ui.S(34) - Ui.S(40));
        }

        int HitIndex(int x)
        {
            int n = Labels.Count;
            Rectangle area = PlotArea();
            if (n == 0 || area.Width <= 0) return -1;
            if (x < area.Left || x > area.Right) return -1;
            int i = (int)((x - area.Left) / ((double)area.Width / n));
            if (i < 0) i = 0;
            if (i >= n) i = n - 1;
            return i;
        }

        void OnMove(object s, MouseEventArgs e)
        {
            int i = HitIndex(e.X);
            if (i != hover) { hover = i; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var font = Ui.F(8f);
            var fontT = Ui.FB(9f);
            int n = Labels.Count;
            if (n == 0)
            {
                g.DrawString("还没有数据", font, new SolidBrush(Ui.Ink3), 60, 60);
                return;
            }
            double total = 0, max = 0;
            for (int i = 0; i < n; i++)
            {
                double v = Get1(i) + Get2(i);
                total += v;
                if (v > max) max = v;
            }
            if (max <= 0) max = 1;
            var area = PlotArea();
            if (area.Width < 100 || area.Height < 60) return;
            // 让刻度落在 1/2/2.5/5 的整数档
            double step = NiceStep(max / 3.0);
            double top = step * 3;

            if (Title.Length > 0)
                g.DrawString(Title, fontT, new SolidBrush(Ui.Ink), Ui.S(16), Ui.S(8));
            string tot = TokMode ? (TokText(total) + " token") : ("¥" + total.ToString("N2"));
            SizeF tsz = g.MeasureString(tot, fontT);
            float tx = Width - Ui.S(20) - tsz.Width;
            g.DrawString(tot, fontT, new SolidBrush(Ui.Ink2), tx, Ui.S(8));

            var gridPen = new Pen(Ui.Line2);
            string fmt = step >= 1 ? "F0" : "F1";
            for (int i = 0; i <= 3; i++)
            {
                double v = step * i;
                int y = area.Bottom - (int)(area.Height * (v / top));
                g.DrawLine(gridPen, area.Left, y, area.Right, y);
                g.DrawString(TokMode ? TokText(v) : ("¥" + v.ToString(fmt)), font, new SolidBrush(Ui.Ink3), 8, y - 7);
            }

            double slot = (double)area.Width / n;
            int barW = (int)Math.Round(Math.Min(44, Math.Max(6, slot * 0.55)));
            int every = Math.Max(1, (int)Math.Ceiling(n / 8.0));
            var offBrush = new SolidBrush(Color.FromArgb(127, 176, 255));
            var pkBrush = new SolidBrush(Color.FromArgb(47, 95, 224));
            var hoverBrush = new SolidBrush(Color.FromArgb(255, 246, 231));
            for (int i = 0; i < n; i++)
            {
                int x = area.Left + (int)(slot * i) + (int)((slot - barW) / 2);
                int h1 = (int)(Get1(i) / top * area.Height);
                int h2 = (int)(Get2(i) / top * area.Height);
                if (i == hover)
                    g.FillRectangle(hoverBrush, area.Left + (int)(slot * i), area.Top, (int)Math.Max(1, slot), area.Height);
                if (h1 > 0) g.FillRectangle(offBrush, x, area.Bottom - h1, barW, h1);
                if (h2 > 0) g.FillRectangle(pkBrush, x, area.Bottom - h1 - h2, barW, h2);
                if (i % every == 0 || i == n - 1)
                {
                    string t = Labels[i];
                    SizeF sz = g.MeasureString(t, font);
                    float lx = x + barW / 2f - sz.Width / 2f;
                    if (lx < area.Left - 8) lx = area.Left - 8;
                    if (lx + sz.Width > area.Right + 10) lx = area.Right + 10 - sz.Width;
                    g.DrawString(t, font, new SolidBrush(Ui.Ink3), lx, area.Bottom + 6);
                }
            }
            // 图例（贴在合计左侧，避免重叠）
            int lx2 = (int)Math.Max(area.Left, tx - Ui.S(150));
            g.FillRectangle(offBrush, lx2, Ui.S(12), Ui.S(10), Ui.S(10));
            g.DrawString("闲时", font, new SolidBrush(Ui.Ink2), lx2 + Ui.S(14), Ui.S(9));
            g.FillRectangle(pkBrush, lx2 + Ui.S(58), Ui.S(12), Ui.S(10), Ui.S(10));
            g.DrawString("高峰", font, new SolidBrush(Ui.Ink2), lx2 + Ui.S(72), Ui.S(9));

            // 悬停明细
            if (hover >= 0 && hover < n)
            {
                string l1 = Labels[hover];
                string l2 = TokMode
                    ? ("闲 " + TokText(Get1(hover)) + "   峰 " + TokText(Get2(hover)))
                    : ("闲 ¥" + Get1(hover).ToString("N2") + "   峰 ¥" + Get2(hover).ToString("N2"));
                string l3 = TokMode
                    ? ("合计 " + TokText(Get1(hover) + Get2(hover)) + " token")
                    : ("合计 ¥" + (Get1(hover) + Get2(hover)).ToString("N2"));
                SizeF s1 = g.MeasureString(l1, fontT);
                SizeF s2 = g.MeasureString(l2, font);
                float bw = Math.Max(s1.Width, s2.Width) + 24;
                float bh = 54;
                float bx = area.Left + (float)(slot * hover) + (float)(slot / 2) - bw / 2;
                if (bx < 4) bx = 4;
                if (bx + bw > Width - 4) bx = Width - 4 - bw;
                float by = area.Top + 6;
                Rectangle rr = new Rectangle((int)bx, (int)by, (int)bw, (int)bh);
                using (GraphicsPath path = Ui.RoundPath(rr, 8))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(27, 33, 48))) g.FillPath(b, path);
                }
                g.DrawString(l1, fontT, Brushes.White, bx + Ui.S(12), by + Ui.S(6));
                g.DrawString(l2, font, new SolidBrush(Color.FromArgb(190, 200, 220)), bx + Ui.S(12), by + Ui.S(24));
                g.DrawString(l3, font, new SolidBrush(Color.FromArgb(190, 200, 220)), bx + Ui.S(12), by + Ui.S(38));
            }
        }
    }

    // 单会话：逐轮花费柱 + 累计花费线
    class TurnChartPanel : Panel
    {
        public List<Turn> Data = new List<Turn>();

        public TurnChartPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        static string TokLabel(long v)
        {
            if (v >= 1000000) return string.Format("{0:F1}M", v / 1000000.0);
            if (v >= 1000) return string.Format("{0:F0}K", v / 1000.0);
            return v.ToString();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var font = new Font("微软雅黑", 8f);
            var fontT = new Font("微软雅黑", 9f);
            int n = Data.Count;
            if (n == 0) { g.DrawString("还没有数据", font, Brushes.Gray, 60, 40); return; }

            var area = new Rectangle(Ui.S(58), Ui.S(32), Width - Ui.S(58) - Ui.S(78), Height - Ui.S(32) - Ui.S(34));
            if (area.Width < 100 || area.Height < 50) return;

            double maxT = 0, sum = 0, maxCost = 0;
            for (int i = 0; i < n; i++)
            {
                double c = Data[i].Cost();
                sum += c;
                if (c > maxCost) maxCost = c;
                if (Data[i].Total > maxT) maxT = Data[i].Total;
            }
            if (maxT <= 0) maxT = 1;
            if (sum <= 0) sum = 0.001;

            var gridPen = new Pen(Ui.Line2);
            for (int i = 0; i <= 3; i++)
            {
                int y = area.Bottom - (int)(area.Height * i / 3.0);
                g.DrawLine(gridPen, area.Left, y, area.Right, y);
                g.DrawString(TokLabel((long)(maxT * i / 3.0)), font, new SolidBrush(Ui.Ink3), Ui.S(6), y - Ui.S(7));
                if (i != 3)
                    g.DrawString("¥" + (sum * i / 3.0).ToString("F2"), font, new SolidBrush(Color.FromArgb(70, 90, 140)), area.Right + Ui.S(6), y - Ui.S(7));
            }

            int step = Math.Max(1, (int)Math.Ceiling(n / (double)Math.Max(40, area.Width / 4)));
            double slot = (double)area.Width / Math.Max(1, n / step);
            int barW = (int)Math.Round(Math.Max(2, Math.Min(18, slot * 0.7)));
            var barBrush = new SolidBrush(Color.FromArgb(140, 190, 252));
            var peakBrush = new SolidBrush(Color.FromArgb(60, 110, 220));

            int bi = 0;
            var linePts = new List<PointF>();
            double acc = 0;
            for (int i = 0; i < n; i += step)
            {
                acc += Data[i].Cost();
                int x = area.Left + (int)(slot * bi) + (int)((slot - barW) / 2);
                int h = (int)(Data[i].Total / maxT * area.Height);
                if (h > 0) g.FillRectangle(Data[i].Peak ? peakBrush : barBrush, x, area.Bottom - h, barW, h);
                linePts.Add(new PointF(x + barW / 2f, area.Bottom - (int)(acc / sum * area.Height)));
                bi++;
            }
            var linePen = new Pen(Color.FromArgb(40, 90, 200), 1.6f);
            if (linePts.Count > 1) g.DrawLines(linePen, linePts.ToArray());

            // 标题、图例、总量（同一行，互不重叠）
            string ttl = "每轮 token（柱）";
            SizeF tw = g.MeasureString(ttl, fontT);
            g.DrawString(ttl, fontT, new SolidBrush(Ui.Ink), Ui.S(16), Ui.S(6));
            int gx = Ui.S(16) + (int)tw.Width + Ui.S(18);
            g.FillRectangle(barBrush, gx, Ui.S(11), Ui.S(8), Ui.S(8));
            g.DrawString("闲时", font, new SolidBrush(Ui.Ink2), gx + Ui.S(11), Ui.S(7));
            g.FillRectangle(peakBrush, gx + Ui.S(52), Ui.S(11), Ui.S(8), Ui.S(8));
            g.DrawString("高峰", font, new SolidBrush(Ui.Ink2), gx + Ui.S(63), Ui.S(7));
            g.DrawString("蓝线＝累计花费", font, new SolidBrush(Ui.Ink3), gx + Ui.S(104), Ui.S(7));

            string tot = string.Format("累计 ¥{0:F2}", sum);
            SizeF tsz = g.MeasureString(tot, fontT);
            g.DrawString(tot, fontT, new SolidBrush(Ui.Ink2), Width - Ui.S(20) - tsz.Width, Ui.S(6));

            // x 轴：首/中/末
            if (n > 0)
            {
                string t0 = Data[0].T.ToString("MM-dd HH:mm");
                string t1 = Data[n - 1].T.ToString("MM-dd HH:mm");
                g.DrawString(t0, font, Brushes.Gray, area.Left, area.Bottom + 5);
                SizeF s1 = g.MeasureString(t1, font);
                g.DrawString(t1, font, Brushes.Gray, area.Right - s1.Width, area.Bottom + 5);
                if (n > 2)
                {
                    string tm = Data[n / 2].T.ToString("MM-dd HH:mm");
                    SizeF sm = g.MeasureString(tm, font);
                    g.DrawString(tm, font, Brushes.Gray, area.Left + area.Width / 2 - sm.Width / 2, area.Bottom + 5);
                }
            }
        }
    }

    class TrendPanel : Panel
    {
        public List<DateTime> Days = new List<DateTime>();
        public List<double> Cost = new List<double>();
        public List<long> Tokens = new List<long>();

        public TrendPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var area = new Rectangle(54, 22, Width - 54 - 26, Height - 22 - 46);
            if (area.Width < 80 || area.Height < 50) return;
            var font = new Font("微软雅黑", 8f);
            if (Days.Count == 0)
            {
                g.DrawString("还没有数据", font, Brushes.Gray, 60, 60);
                return;
            }
            double max = 0;
            foreach (double c in Cost) if (c > max) max = c;
            if (max <= 0) max = 1;
            var gridPen = new Pen(Color.FromArgb(233, 236, 242));
            for (int i = 0; i <= 4; i++)
            {
                double v = max * i / 4.0;
                int y = area.Bottom - (int)(area.Height * i / 4.0);
                g.DrawLine(gridPen, area.Left, y, area.Right, y);
                g.DrawString("¥" + v.ToString("F1"), font, Brushes.Gray, 6, y - 7);
            }
            int n = Days.Count;
            double step = n > 1 ? (double)area.Width / (n - 1) : 0;
            int every = Math.Max(1, (int)Math.Ceiling(n / 6.0));
            for (int i = 0; i < n; i += every)
            {
                int x = area.Left + (int)(step * i);
                string t = Days[i].ToString("MM-dd");
                SizeF sz = g.MeasureString(t, font);
                g.DrawString(t, font, Brushes.Gray, x - sz.Width / 2f, area.Bottom + 6);
            }
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                int x = area.Left + (int)(step * i);
                int y = area.Bottom - (int)(Cost[i] / max * area.Height);
                pts[i] = new PointF(x, y);
            }
            var linePen = new Pen(Color.FromArgb(46, 120, 210), 2f);
            if (n > 1) g.DrawLines(linePen, pts);
            var dot = new SolidBrush(Color.FromArgb(46, 120, 210));
            for (int i = 0; i < n; i++)
                g.FillEllipse(dot, pts[i].X - 2.5f, pts[i].Y - 2.5f, 5f, 5f);
            int mi = 0;
            for (int i = 1; i < n; i++) if (Cost[i] > Cost[mi]) mi = i;
            string lab = string.Format("{0}  ¥{1:F2}", Days[mi].ToString("MM-dd"), Cost[mi]);
            SizeF ls = g.MeasureString(lab, font);
            float lx = pts[mi].X - ls.Width / 2f;
            if (lx < area.Left) lx = area.Left;
            if (lx + ls.Width > area.Right) lx = area.Right - ls.Width;
            float ly = pts[mi].Y - ls.Height - 6;
            if (ly < area.Top) ly = pts[mi].Y + 8;
            g.FillRectangle(Brushes.White, lx - 2, ly, ls.Width + 4, ls.Height);
            g.DrawString(lab, font, Brushes.DimGray, lx, ly);
        }
    }

    class DistForm : Form
    {
        public TabControl Tabs;
        string csvPath;
        List<SessionInfo> universe;
        DateTime? rangeA, rangeB;
        Dictionary<string, Timeline> clip = new Dictionary<string, Timeline>();

        Timeline Eff(SessionInfo s)
        {
            if (rangeA == null || rangeB == null) return s.Tl;
            Timeline t;
            if (clip.TryGetValue(s.Id, out t)) return t;
            t = Tl.Slice(s.Tl, rangeA.Value, rangeB.Value);
            clip[s.Id] = t;
            return t;
        }

        double CostOf(SessionInfo s)
        {
            Timeline t = Eff(s);
            return t == null ? 0 : t.TotalCost();
        }

        static string F(long n)
        {
            if (n >= 1000000) return string.Format("{0:F2}M", n / 1000000.0);
            if (n >= 1000) return string.Format("{0:F1}K", n / 1000.0);
            return n.ToString();
        }

        static string PjName(string cwd)
        {
            if (string.IsNullOrEmpty(cwd)) return "(未知)";
            string t = cwd.TrimEnd('\\', '/');
            string[] parts = t.Split(new char[] { '\\', '/' });
            string leaf = parts.Length > 0 ? parts[parts.Length - 1] : t;
            if (leaf == "default-workspace" && parts.Length >= 2) return parts[parts.Length - 2] + "\\" + leaf;
            return leaf;
        }

        static DataGridView MakeGrid()
        {
            var g = new DataGridView();
            g.ReadOnly = true;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.RowHeadersVisible = false;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.BackgroundColor = Color.White;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersHeight = Ui.S(32);
            g.RowTemplate.Height = Ui.S(30);
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 246, 250);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(108, 117, 135);
            g.ColumnHeadersDefaultCellStyle.Font = new Font("微软雅黑", 9f);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            g.DefaultCellStyle.Font = new Font("微软雅黑", 9f);
            g.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(241, 246, 254);
            g.DefaultCellStyle.SelectionForeColor = Color.Black;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(252, 253, 255);
            g.GridColor = Color.FromArgb(237, 240, 245);
            return g;
        }

        // 给某一列加“占比条”背景
        static void AttachBars(DataGridView g, int col)
        {
            double max = 0;
            foreach (DataGridViewRow r in g.Rows)
            {
                string t = Convert.ToString(r.Cells[col].Value);
                if (t == null || t.Length == 0) continue;
                double v = 0;
                double.TryParse(t.Replace("¥", "").Replace(",", ""), out v);
                if (v > max) max = v;
            }
            if (max <= 0) return;
            g.CellPainting += new DataGridViewCellPaintingEventHandler(delegate(object snd, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != col) return;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.PaintBackground(e.CellBounds, true);
                double v = 0;
                string t = Convert.ToString(e.Value);
                if (t != null) double.TryParse(t.Replace("¥", "").Replace(",", ""), out v);
                if (v > 0)
                {
                    int w = (int)Math.Round((e.CellBounds.Width - 16) * 0.45 * Math.Min(1.0, v / max));
                    if (w < 3) w = 3;
                    Rectangle r = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + 7, w, e.CellBounds.Height - 14);
                    using (System.Drawing.Drawing2D.GraphicsPath path = Ui.RoundPath(r, 3))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(233, 240, 254))) e.Graphics.FillPath(b, path);
                }
                e.PaintContent(e.CellBounds);
                e.Handled = true;
            });
        }

        static Font boldFont()
        {
            return new Font("微软雅黑", 9f, FontStyle.Bold);
        }

        TabPage Page(string title, Control body, string tip)
        {
            var p = new TabPage(title);
            p.BackColor = Color.White;
            p.Padding = new Padding(10);
            var lbl = new Label();
            lbl.Text = tip;
            lbl.ForeColor = Color.FromArgb(140, 142, 152);
            lbl.Dock = DockStyle.Top;
            lbl.Height = 22;
            body.Dock = DockStyle.Fill;
            p.Controls.Add(body);
            p.Controls.Add(lbl);
            return p;
        }

        TabPage Page(string title, Control body)
        {
            var p = new TabPage(title);
            p.BackColor = Color.White;
            p.Padding = new Padding(10);
            body.Dock = DockStyle.Fill;
            p.Controls.Add(body);
            return p;
        }

        public DistForm(List<SessionInfo> sessions, string scope)
            : this(sessions, scope, null, null)
        {
        }

        public DistForm(List<SessionInfo> sessions, string scope, string importCsv)
            : this(sessions, scope, importCsv, null)
        {
        }

        public DistForm(List<SessionInfo> sessions, string scope, string importCsv, List<SessionInfo> univ)
            : this(sessions, scope, importCsv, univ, null, null)
        {
        }

        public DistForm(List<SessionInfo> sessions, string scope, string importCsv, List<SessionInfo> univ,
            DateTime? ra, DateTime? rb)
        {
            rangeA = ra;
            rangeB = rb;
            csvPath = importCsv;
            universe = univ;
            AutoScaleMode = AutoScaleMode.None;
            Text = "统计";
            ClientSize = new Size(880, 700);
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.Bg;
            Font = new Font("微软雅黑", 9f);

            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 42;
            head.BackColor = Ui.Bg;
            var lbl = new Label();
            lbl.AutoSize = true;
            lbl.Location = new Point(18, 13);
            lbl.Font = Ui.F(8.5f);
            lbl.ForeColor = Ui.Ink3;
            lbl.Text = scope + "    高峰＝工作日 9:00-12:00、14:00-18:00；其余空闲（空闲价为高峰价一半）";
            head.Controls.Add(lbl);

            Tabs = new TabControl();
            Tabs.Dock = DockStyle.Fill;
            Tabs.Padding = new Point(14, 6);
            Tabs.Font = Ui.F(9f);
            Tabs.Appearance = TabAppearance.FlatButtons;
            Tabs.SizeMode = TabSizeMode.Fixed;
            Tabs.ItemSize = new Size(Ui.S(78), Ui.S(30));
            Tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            Tabs.DrawItem += new DrawItemEventHandler(delegate(object snd, DrawItemEventArgs de)
            {
                Rectangle tr = Tabs.GetTabRect(de.Index);
                Graphics g = de.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush wb = new SolidBrush(Color.White)) g.FillRectangle(wb, tr.X, tr.Y, tr.Width, tr.Height);
                bool selNow = de.Index == Tabs.SelectedIndex;
                Rectangle pill = new Rectangle(tr.X + 3, tr.Y + 3, tr.Width - 6, tr.Height - 8);
                if (selNow)
                    Ui.DrawCard(g, pill, Ui.AccentSoft, Ui.AccentLine, 8);
                string t = Tabs.TabPages[de.Index].Text;
                using (Font f = selNow ? Ui.FB(9f) : Ui.F(9f))
                {
                    SizeF sz = g.MeasureString(t, f);
                    using (SolidBrush b = new SolidBrush(selNow ? Ui.Accent : Ui.Ink2))
                        g.DrawString(t, f, b, pill.X + (pill.Width - sz.Width) / 2f, pill.Y + (pill.Height - sz.Height) / 2f);
                }
            });
            Tabs.SelectedIndexChanged += new EventHandler(delegate(object snd, EventArgs a) { Tabs.Invalidate(); });

            var card = new Panel();
            card.Dock = DockStyle.Fill;
            card.BackColor = Color.White;
            card.Padding = new Padding(10, 8, 10, 8);
            card.Paint += new PaintEventHandler(delegate(object snd, PaintEventArgs pe)
            {
                pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Ui.DrawCard(pe.Graphics, new Rectangle(0, 0, card.Width - 1, card.Height - 1), Color.White, Ui.Line, Ui.S(10));
            });
            card.Controls.Add(Tabs);

            var host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(16, 0, 16, 14);
            host.BackColor = Ui.Bg;
            host.Controls.Add(card);

            // 聚合
            var byDay = new SortedDictionary<string, Slot>(StringComparer.Ordinal);
            var byHour = new Slot[24];
            for (int i = 0; i < 24; i++) byHour[i] = new Slot();
            var byProj = new SortedDictionary<string, Slot>(StringComparer.Ordinal);
            var projCount = new Dictionary<string, int>();
            var projPath = new Dictionary<string, string>();
            var byModel = new SortedDictionary<string, Slot>(StringComparer.Ordinal);
            var modelCount = new Dictionary<string, int>();
            var allS = new Slot();
            foreach (SessionInfo s in sessions)
            {
                Timeline st = Eff(s);
                if (st == null) continue;
                allS.Merge(st);
                foreach (var kv in st.Days)
                {
                    Slot d;
                    if (!byDay.TryGetValue(kv.Key, out d)) { d = new Slot(); byDay[kv.Key] = d; }
                    d.Merge(kv.Value);
                }
                for (int h = 0; h < 24; h++) byHour[h].Merge(st.Hours[h]);

                string pn = PjName(s.Cwd);
                Slot ps;
                if (!byProj.TryGetValue(pn, out ps)) { ps = new Slot(); byProj[pn] = ps; projCount[pn] = 0; projPath[pn] = s.Cwd; }
                ps.Merge(st);
                projCount[pn] = projCount[pn] + 1;

                string mdl = s.Model.Length > 0 ? s.Model : "(未知模型)";
                Slot ms;
                if (!byModel.TryGetValue(mdl, out ms)) { ms = new Slot(); byModel[mdl] = ms; modelCount[mdl] = 0; }
                ms.Merge(st);
                modelCount[mdl] = modelCount[mdl] + 1;
            }

            // --- 按天 ---
            var g1 = MakeGrid();
            g1.Columns.Add("c1", "日期");
            g1.Columns.Add("c2", "闲时 token");
            g1.Columns.Add("c3", "高峰 token");
            g1.Columns.Add("c4", "闲时 (元)");
            g1.Columns.Add("c5", "高峰 (元)");
            g1.Columns.Add("c6", "合计 (元)");
            g1.Columns[0].FillWeight = 90;
            var keys = new List<string>(byDay.Keys);
            keys.Reverse();
            foreach (string k in keys)
            {
                Slot d = byDay[k];
                g1.Rows.Add(k, F(d.OffTokens()), F(d.PkTokens()),
                    string.Format("{0:F2}", d.OffCost()), string.Format("{0:F2}", d.PkCost()),
                    string.Format("{0:F2}", d.TotalCost()));
            }
            int r1 = g1.Rows.Add("合计", F(allS.OffTokens()), F(allS.PkTokens()),
                string.Format("{0:F2}", allS.OffCost()), string.Format("{0:F2}", allS.PkCost()),
                string.Format("{0:F2}", allS.TotalCost()));
            g1.Rows[r1].DefaultCellStyle.Font = boldFont();

            // --- 按小时 ---
            var g2 = MakeGrid();
            g2.Columns.Add("h1", "小时");
            g2.Columns.Add("h2", "闲时 token");
            g2.Columns.Add("h3", "高峰 token");
            g2.Columns.Add("h4", "合计 token");
            g2.Columns.Add("h5", "花费 (元)");
            g2.Columns[0].FillWeight = 70;
            var hourAll = new Slot();
            for (int h = 0; h < 24; h++)
            {
                Slot b = byHour[h];
                hourAll.Merge(b);
                if (b.TotalTokens() == 0) continue;
                g2.Rows.Add(string.Format("{0:00}:00", h), F(b.OffTokens()), F(b.PkTokens()),
                    F(b.TotalTokens()), string.Format("{0:F2}", b.TotalCost()));
            }
            int r2 = g2.Rows.Add("合计", F(hourAll.OffTokens()), F(hourAll.PkTokens()),
                F(hourAll.TotalTokens()), string.Format("{0:F2}", hourAll.TotalCost()));
            g2.Rows[r2].DefaultCellStyle.Font = boldFont();

            // --- 按项目 ---
            var g3 = MakeGrid();
            g3.Columns.Add("p1", "项目");
            g3.Columns.Add("p2", "会话数");
            g3.Columns.Add("p3", "总 token");
            g3.Columns.Add("p4", "闲时 (元)");
            g3.Columns.Add("p5", "高峰 (元)");
            g3.Columns.Add("p6", "合计 (元)");
            g3.Columns.Add("p7", "占比");
            g3.Columns[0].FillWeight = 260;
            var projKeys = new List<string>(byProj.Keys);
            projKeys.Sort(delegate(string a, string b) { return byProj[b].TotalCost().CompareTo(byProj[a].TotalCost()); });
            foreach (string pk in projKeys)
            {
                Slot ps = byProj[pk];
                double share = allS.TotalCost() > 0 ? ps.TotalCost() / allS.TotalCost() * 100 : 0;
                int idx = g3.Rows.Add(pk, projCount[pk], F(ps.TotalTokens()),
                    string.Format("{0:F2}", ps.OffCost()), string.Format("{0:F2}", ps.PkCost()),
                    string.Format("{0:F2}", ps.TotalCost()), string.Format("{0:F0}%", share));
                g3.Rows[idx].Cells[0].ToolTipText = projPath[pk];
            }
            int r3 = g3.Rows.Add("合计", sessions.Count, F(allS.TotalTokens()),
                string.Format("{0:F2}", allS.OffCost()), string.Format("{0:F2}", allS.PkCost()),
                string.Format("{0:F2}", allS.TotalCost()), "100%");
            g3.Rows[r3].DefaultCellStyle.Font = boldFont();

            // --- 按模型 ---
            var g7 = MakeGrid();
            g7.Columns.Add("q1", "模型");
            g7.Columns.Add("q2", "会话数");
            g7.Columns.Add("q3", "总 token");
            g7.Columns.Add("q4", "闲时 (元)");
            g7.Columns.Add("q5", "高峰 (元)");
            g7.Columns.Add("q6", "合计 (元)");
            g7.Columns.Add("q7", "占比");
            g7.Columns[0].FillWeight = 280;
            for (int i = 1; i < g7.Columns.Count; i++)
                g7.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            var modelKeys = new List<string>(byModel.Keys);
            modelKeys.Sort(delegate(string a, string b) { return byModel[b].TotalCost().CompareTo(byModel[a].TotalCost()); });
            foreach (string mk in modelKeys)
            {
                Slot ms = byModel[mk];
                double share = allS.TotalCost() > 0 ? ms.TotalCost() / allS.TotalCost() * 100 : 0;
                g7.Rows.Add(mk, modelCount[mk], F(ms.TotalTokens()),
                    string.Format("{0:N2}", ms.OffCost()), string.Format("{0:N2}", ms.PkCost()),
                    string.Format("{0:N2}", ms.TotalCost()), string.Format("{0:F0}%", share));
            }
            int r7 = g7.Rows.Add("合计", sessions.Count, F(allS.TotalTokens()),
                string.Format("{0:N2}", allS.OffCost()), string.Format("{0:N2}", allS.PkCost()),
                string.Format("{0:N2}", allS.TotalCost()), "100%");
            g7.Rows[r7].DefaultCellStyle.Font = boldFont();
            AttachBars(g7, 5);

            // --- 趋势 ---
            var trend = new TrendPanel();
            trend.Dock = DockStyle.Fill;
            var dayKeys = new List<string>(byDay.Keys);
            dayKeys.Sort(StringComparer.Ordinal);
            DateTime lastD = DateTime.Today;
            DateTime firstD = lastD;
            if (dayKeys.Count > 0)
            {
                DateTime d0;
                if (DateTime.TryParse(dayKeys[0], out d0)) firstD = d0.Date;
            }
            if (firstD < lastD.AddDays(-29)) firstD = lastD.AddDays(-29);
            double sum = 0, best = 0;
            string bestDay = "";
            for (DateTime d = firstD; d <= lastD; d = d.AddDays(1))
            {
                string key = d.ToString("yyyy-MM-dd");
                Slot v;
                if (!byDay.TryGetValue(key, out v)) v = new Slot();
                trend.Days.Add(d);
                trend.Cost.Add(v.TotalCost());
                trend.Tokens.Add(v.TotalTokens());
                sum += v.TotalCost();
                if (v.TotalCost() > best) { best = v.TotalCost(); bestDay = key; }
            }
            var wrap = new Panel();
            wrap.BackColor = Color.White;
            var foot = new Label();
            foot.Dock = DockStyle.Bottom;
            foot.Height = 30;
            foot.ForeColor = Color.FromArgb(110, 112, 122);
            foot.Text = string.Format("    近 {0} 天合计 ¥{1:F2}，日均 ¥{2:F2}；最高 {3} ¥{4:F2}",
                trend.Days.Count, sum, trend.Days.Count > 0 ? sum / trend.Days.Count : 0,
                bestDay, best);
            wrap.Controls.Add(trend);
            wrap.Controls.Add(foot);

            // --- 诊断：缓存命中率 ---
            var wrapD = new Panel();
            wrapD.BackColor = Color.White;
            long sumCache = 0, sumTotal = 0;
            foreach (SessionInfo s in sessions) { sumCache += s.Cache; sumTotal += s.Total; }
            int hitAll = sumTotal > 0 ? (int)((double)sumCache / sumTotal * 100.0) : 0;
            var lblDiag = new Label();
            lblDiag.Dock = DockStyle.Top;
            lblDiag.Height = 56;
            lblDiag.ForeColor = Color.FromArgb(110, 112, 122);
            lblDiag.Text = string.Format(
                "    总体缓存命中率 {0}%（命中 token 按 0.02/0.04 计价，未命中按 1/2 计价，差 50 倍）。\n    低于 80% 的会话列在下面：这类会话的上下文被反复按未命中价读进来。",
                hitAll);

            var g4 = MakeGrid();
            g4.Dock = DockStyle.Fill;
            g4.Columns.Add("d1", "会话");
            g4.Columns.Add("d2", "命中率");
            g4.Columns.Add("d3", "未命中 token");
            g4.Columns.Add("d4", "输出 token");
            g4.Columns.Add("d5", "费用 (元)");
            g4.Columns.Add("d6", "最后活动");
            g4.Columns[0].FillWeight = 260;
            var low = new List<SessionInfo>();
            foreach (SessionInfo s in sessions)
            {
                Timeline st9 = Eff(s);
                if (st9 != null && st9.TotalTokens() >= 500000) low.Add(s);
            }
            low.Sort(delegate(SessionInfo a, SessionInfo b)
            {
                Timeline ta = Eff(a), tb = Eff(b);
                long totA = ta == null ? 0 : ta.TotalTokens();
                long totB = tb == null ? 0 : tb.TotalTokens();
                long cchA = ta == null ? 0 : ta.OffC + ta.PkC;
                long cchB = tb == null ? 0 : tb.OffC + tb.PkC;
                double ha = totA > 0 ? (double)cchA / totA : 1;
                double hb = totB > 0 ? (double)cchB / totB : 1;
                if (ha != hb) return ha.CompareTo(hb);
                return totB.CompareTo(totA);
            });
            int badCount = 0;
            foreach (SessionInfo s in sessions)
            {
                Timeline st8 = Eff(s);
                if (st8 == null) continue;
                long tt8 = st8.TotalTokens();
                if (tt8 >= 500000 && (st8.OffC + st8.PkC) * 100.0 / tt8 < 80.0) badCount++;
            }
            lblDiag.Text = string.Format(
                "    总体缓存命中率 {0}%；其中 {1} 个会话低于 80%（下面按命中率从低到高列前 {2} 个）。\n    命中 token 按 0.02/0.04 计价，未命中按 1/2 计价，差 50 倍，所以低命中会明显拉高花费。",
                hitAll, badCount, Math.Min(10, low.Count));
            int rowShown = 0;
            foreach (SessionInfo s in low)
            {
                if (rowShown >= 10) break;
                rowShown++;
                Timeline st7 = Eff(s);
                long tt7 = st7 == null ? 0 : st7.TotalTokens();
                long cch7 = st7 == null ? 0 : st7.OffC + st7.PkC;
                int h = tt7 > 0 ? (int)(cch7 * 100.0 / tt7) : 0;
                int idx = g4.Rows.Add(s.Title.Length > 34 ? s.Title.Substring(0, 34) + "…" : s.Title,
                    h + "%", F(st7 == null ? 0 : st7.OffU + st7.PkU), F(st7 == null ? 0 : st7.OffO + st7.PkO),
                    string.Format("{0:F2}", CostOf(s)), s.Last.ToString("MM-dd HH:mm"));
                g4.Rows[idx].Cells[0].ToolTipText = s.Title;
                if (h < 80) g4.Rows[idx].Cells[1].Style.ForeColor = Color.FromArgb(190, 90, 20);
            }
            if (low.Count == 0)
                g4.Rows.Add("这里还没有数据", "", "", "", "", "");
            wrapD.Controls.Add(g4);
            wrapD.Controls.Add(lblDiag);

            // --- 主对话 / 子智能体 ---
            var wrapK = new Panel();
            wrapK.BackColor = Color.White;

            // 主对话 = 本次范围内的非子体；子体 = 它们在全库里的所有子智能体（没勾选也算进链）
            var mains = new List<SessionInfo>();
            foreach (SessionInfo s in sessions) if (!s.IsSub) mains.Add(s);
            var childMap = new Dictionary<string, List<SessionInfo>>();
            var childSeen = new Dictionary<string, bool>();
            foreach (SessionInfo m in mains)
            {
                var cl = new List<SessionInfo>();
                var src = universe != null ? universe : sessions;
                foreach (SessionInfo c in src)
                {
                    if (!c.IsSub || c.ParentId != m.Id) continue;
                    if (childSeen.ContainsKey(c.Id)) continue;
                    childSeen[c.Id] = true;
                    cl.Add(c);
                }
                childMap[m.Id] = cl;
            }

            long mTokK = 0, sTokK = 0;
            double mCostK = 0, sCostK = 0;
            int sNK = 0;
            foreach (SessionInfo m in mains)
            {
                Timeline em = Eff(m);
                mTokK += em == null ? 0 : em.TotalTokens(); mCostK += CostOf(m);
                foreach (SessionInfo c in childMap[m.Id])
                {
                    Timeline ec = Eff(c);
                    sTokK += ec == null ? 0 : ec.TotalTokens();
                    sCostK += CostOf(c);
                    sNK++;
                }
            }
            long totTokK = mTokK + sTokK;
            double totCostK = mCostK + sCostK;
            Func<long, string> pctOf = delegate(long v)
            {
                return totTokK > 0 ? string.Format("{0:F1}%", v * 100.0 / totTokK) : "—";
            };

            var lblK = new Label();
            lblK.Dock = DockStyle.Top;
            lblK.Height = 60;
            lblK.ForeColor = Color.FromArgb(90, 94, 104);
            lblK.Text = string.Format(
                "    主对话 {0} 个：{1} token（{2}），¥{3:F2}        子智能体 {4} 个：{5} token（{6}），¥{7:F2}\n    下面按“任务链”归并：一行＝一个主对话，加上它派出去的所有子智能体（没勾选也一并算进来）；父会话不在本次范围的子体，单列在最后。",
                mains.Count, F(mTokK), pctOf(mTokK), mCostK, sNK, F(sTokK), pctOf(sTokK), sCostK);

            var g6 = MakeGrid();
            g6.Dock = DockStyle.Fill;
            g6.Columns.Add("m1", "任务链（主对话）");
            g6.Columns.Add("m2", "子体数");
            g6.Columns.Add("m3", "主对话 token");
            g6.Columns.Add("m4", "子体 token");
            g6.Columns.Add("m5", "合计 token");
            g6.Columns.Add("m6", "合计 (元)");
            g6.Columns.Add("m7", "占比");
            g6.Columns[0].FillWeight = 300;

            // 链：[0]标题 [1]主体token [2]子体token [3]主体费用 [4]子体费用 [5]子体数 [6]主对话是否在本次范围内
            var chains = new Dictionary<string, object[]>();
            foreach (SessionInfo m in mains)
            {
                var ch = new object[] { m.Title, 0L, 0L, 0.0, 0.0, 0, true };
                chains[m.Id] = ch;
                Timeline em2 = Eff(m);
                ch[1] = em2 == null ? 0L : em2.TotalTokens();
                ch[3] = CostOf(m);
                foreach (SessionInfo c in childMap[m.Id])
                {
                    Timeline ec2 = Eff(c);
                    ch[2] = (long)ch[2] + (ec2 == null ? 0 : ec2.TotalTokens());
                    ch[4] = (double)ch[4] + CostOf(c);
                    ch[5] = (int)ch[5] + 1;
                }
            }
            // 父亲不在范围内（或父未知）的子体
            foreach (SessionInfo s in sessions)
            {
                if (!s.IsSub) continue;
                if (childSeen.ContainsKey(s.Id)) continue;
                string key = s.ParentId.Length > 0 ? s.ParentId : "orphan:" + s.Id;
                object[] ch;
                if (!chains.TryGetValue(key, out ch))
                {
                    string ttl = s.ParentTitle.Length > 0 ? "（主对话未选中）" + s.ParentTitle : "（父会话未知）" + s.SubLabel;
                    ch = new object[] { ttl, 0L, 0L, 0.0, 0.0, 0, false };
                    chains[key] = ch;
                }
                Timeline es = Eff(s);
                ch[2] = (long)ch[2] + (es == null ? 0 : es.TotalTokens());
                ch[4] = (double)ch[4] + CostOf(s);
                ch[5] = (int)ch[5] + 1;
                childSeen[s.Id] = true;
            }
            var chainList = new List<object[]>(chains.Values);
            chainList.Sort(delegate(object[] a, object[] b)
            {
                double ca = (double)a[3] + (double)a[4], cb = (double)b[3] + (double)b[4];
                return cb.CompareTo(ca);
            });
            foreach (object[] ch in chainList)
            {
                long mt = (long)ch[1], st = (long)ch[2];
                double mc = (double)ch[3], sc = (double)ch[4];
                long tt = mt + st;
                int idx = g6.Rows.Add(ch[0], (int)ch[5] == 0 ? "—" : ((int)ch[5]).ToString(),
                    F(mt), F(st), F(tt), string.Format("{0:F2}", mc + sc), pctOf(tt));
                g6.Rows[idx].Cells[0].ToolTipText = ch[0].ToString();
                if (!(bool)ch[6]) g6.Rows[idx].Cells[0].Style.ForeColor = Color.FromArgb(150, 152, 162);
            }
            int r6 = g6.Rows.Add("合计", sNK, F(mTokK), F(sTokK), F(totTokK),
                string.Format("{0:F2}", totCostK), "100%");
            g6.Rows[r6].DefaultCellStyle.Font = boldFont();
            var footK = new Label();
            footK.Dock = DockStyle.Bottom;
            footK.Height = 26;
            footK.ForeColor = Color.FromArgb(110, 112, 122);
            footK.Text = "    提示：主列表勾上“选中主对话时连带子智能体”，统计里选中的就是整条任务链。";
            wrapK.Controls.Add(g6);
            wrapK.Controls.Add(lblK);
            wrapK.Controls.Add(footK);

            // --- 对比：导入朋友的 CSV ---
            var wrapC = new Panel();
            wrapC.BackColor = Color.White;
            var barC = new Panel();
            barC.Dock = DockStyle.Top;
            barC.Height = 46;
            var btnImp = new Button();
            btnImp.Text = "导入朋友导出的 CSV…";
            btnImp.Size = new Size(180, 28);
            btnImp.Location = new Point(0, 4);
            var lblImp = new Label();
            lblImp.AutoSize = true;
            lblImp.ForeColor = Color.FromArgb(110, 112, 122);
            lblImp.Location = new Point(190, 10);
            lblImp.Text = "让对方在他程序里点【导出 CSV】，把文件发给你，选进来就能并排对比";
            barC.Controls.Add(btnImp);
            barC.Controls.Add(lblImp);

            var g5 = MakeGrid();
            g5.Dock = DockStyle.Fill;
            g5.Columns.Add("k1", "来源");
            g5.Columns.Add("k2", "会话数");
            g5.Columns.Add("k3", "总 token");
            g5.Columns.Add("k4", "闲时 token");
            g5.Columns.Add("k5", "高峰 token");
            g5.Columns.Add("k6", "合计 (元)");
            g5.Columns.Add("k7", "日均 (元)");
            g5.Columns[0].FillWeight = 180;

            double mineCost = allS.TotalCost();
            int mineDays = byDay.Count;
            string mineAvg = mineDays > 0 ? string.Format("{0:F2}", mineCost / mineDays) : "—";
            var paintCompare = new Action<double, int, long, long, long, int, string>(
                delegate(double oCost, int oN, long oTotal, long oOff, long oPk, int oDays, string who)
                {
                    g5.Rows.Clear();
                    g5.Rows.Add("我（本机）", sessions.Count, F(allS.TotalTokens()), F(allS.OffTokens()),
                        F(allS.PkTokens()), string.Format("{0:F2}", mineCost), mineAvg);
                    if (oN > 0)
                    {
                        g5.Rows.Add(who, oN, F(oTotal), F(oOff), F(oPk),
                            string.Format("{0:F2}", oCost), oDays > 0 ? string.Format("{0:F2}", oCost / oDays) : "—");
                        string diff = oCost >= mineCost
                            ? "+" + string.Format("{0:F2}", oCost - mineCost)
                            : "-" + string.Format("{0:F2}", mineCost - oCost);
                        g5.Rows.Add("差值（对方 − 我）", "—", "—", "—", "—", diff, "—");
                    }
                });
            paintCompare(0, 0, 0, 0, 0, 0, "");

            btnImp.Click += new EventHandler(delegate
            {
                var ofd = new OpenFileDialog();
                ofd.Filter = "CSV 文件 (*.csv)|*.csv";
                if (ofd.ShowDialog(this) == DialogResult.OK) TryImport(ofd.FileName, paintCompare, lblImp);
            });

            if (!string.IsNullOrEmpty(csvPath) && File.Exists(csvPath))
            {
                try { TryImport(csvPath, paintCompare, lblImp); }
                catch { }
            }

            wrapC.Controls.Add(g5);
            wrapC.Controls.Add(barC);

            // --- 花费 / 用量双图（上=每天花费，下=每天 token） ---
            var wrapB = new Panel();
            wrapB.BackColor = Color.White;
            var bar = new BarChartPanel();
            bar.Dock = DockStyle.Fill;
            bar.Margin = new Padding(0);
            bar.Title = "每天花费（闲时 + 高峰）";
            var barTok = new BarChartPanel();
            barTok.Dock = DockStyle.Fill;
            barTok.Margin = new Padding(0);
            barTok.TokMode = true;
            barTok.Title = "每天 token 消耗（闲时 + 高峰）";
            var dayAsc = new List<string>(byDay.Keys);
            dayAsc.Sort(StringComparer.Ordinal);
            foreach (string k in dayAsc)
            {
                Slot v = byDay[k];
                string lb = k.Length >= 10 ? k.Substring(5) : k;
                bar.Labels.Add(lb);
                bar.OffCost.Add(v.OffCost());
                bar.PkCost.Add(v.PkCost());
                barTok.Labels.Add(lb);
                barTok.OffTok.Add((double)v.OffTokens());
                barTok.PkTok.Add((double)v.PkTokens());
            }
            var tblB = new TableLayoutPanel();
            tblB.Dock = DockStyle.Fill;
            tblB.Padding = new Padding(0);
            tblB.ColumnCount = 1;
            tblB.RowCount = 2;
            tblB.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tblB.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            tblB.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            tblB.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            tblB.Controls.Add(bar, 0, 0);
            tblB.Controls.Add(barTok, 0, 1);
            var tipB = new Label();
            tipB.Dock = DockStyle.Bottom;
            tipB.Height = 28;
            tipB.ForeColor = Color.FromArgb(110, 112, 122);
            tipB.Text = "    两张图横坐标对齐，可以直接横向对比：某天 token 涨得猛、花费涨得不多，说明当天命中率高、单位成本低。";
            wrapB.Controls.Add(tblB);
            wrapB.Controls.Add(tipB);

            Tabs.TabPages.Add(Page("按天", g1));
            Tabs.TabPages.Add(Page("花费·用量", wrapB, "上：每天花费（¥），下：每天 token 消耗；浅蓝=闲时、深蓝=高峰，右上为区间合计"));
            Tabs.TabPages.Add(Page("按小时", g2));
            Tabs.TabPages.Add(Page("按项目", g3));
            Tabs.TabPages.Add(Page("按模型", g7, "按每个会话最后使用的模型汇总（会话中途换模型的仍归到最后一个）"));
            Tabs.TabPages.Add(Page("趋势", wrap));
            Tabs.TabPages.Add(Page("诊断", wrapD));
            Tabs.TabPages.Add(Page("主/子", wrapK));
            Tabs.TabPages.Add(Page("对比", wrapC));

            Controls.Add(host);
            Controls.Add(head);
            RightAlignGrids(host);
            Ui.ScaleTree(this);
        }

        static void RightAlignGrids(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                DataGridView dg = c as DataGridView;
                if (dg != null)
                {
                    for (int i = 1; i < dg.Columns.Count; i++)
                        dg.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    continue;
                }
                if (c.Controls.Count > 0) RightAlignGrids(c);
            }
        }

        static double SessCostOf(SessionInfo s)
        {
            return s.Tl == null ? 0 : s.Tl.TotalCost();
        }

        static void TryImport(string path, Action<double, int, long, long, long, int, string> paint, Label lbl)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                var csvRows = ParseCsv(text);
                if (csvRows.Count < 2) { MessageBox.Show("这个文件里没有数据行。", "对比"); return; }
                string[] hdr = new string[csvRows[0].Length];
                for (int i = 0; i < hdr.Length; i++) hdr[i] = csvRows[0][i].Trim();
                int iTotal = -1, iOff = -1, iPk = -1, iCost = -1, iLast = -1;
                for (int i = 0; i < hdr.Length; i++)
                {
                    if (hdr[i] == "总token") iTotal = i;
                    else if (hdr[i] == "闲时token") iOff = i;
                    else if (hdr[i] == "高峰token") iPk = i;
                    else if (hdr[i] == "合计(元)") iCost = i;
                    else if (hdr[i] == "最后活动") iLast = i;
                }
                if (iTotal < 0 || iCost < 0)
                {
                    MessageBox.Show("表头对不上，请让对方用同一版程序导出的 CSV。", "对比");
                    return;
                }
                long oTotal = 0, oOff = 0, oPk = 0;
                double oCost = 0;
                int oN = 0;
                var dayset = new HashSet<string>();
                for (int r = 1; r < csvRows.Count; r++)
                {
                    string[] c = csvRows[r];
                    if (c.Length <= Math.Max(iTotal, iCost)) continue;
                    long v1;
                    if (!long.TryParse(c[iTotal].Trim(), out v1)) continue;
                    oTotal += v1;
                    if (iOff >= 0 && c.Length > iOff) { long v2; if (long.TryParse(c[iOff].Trim(), out v2)) oOff += v2; }
                    if (iPk >= 0 && c.Length > iPk) { long v3; if (long.TryParse(c[iPk].Trim(), out v3)) oPk += v3; }
                    double v4;
                    if (double.TryParse(c[iCost].Trim(), out v4)) oCost += v4;
                    if (iLast >= 0 && c.Length > iLast)
                    {
                        string d = c[iLast].Trim();
                        if (d.Length >= 10) dayset.Add(d.Substring(0, 10));
                    }
                    oN++;
                }
                if (oN == 0) { MessageBox.Show("没有解析到数据行。", "对比"); return; }
                string who = "朋友（" + Path.GetFileNameWithoutExtension(path) + "）";
                paint(oCost, oN, oTotal, oOff, oPk, dayset.Count, who);
                lbl.Text = string.Format("已载入 {0}：{1} 个会话，合计 ¥{2:F2}", Path.GetFileName(path), oN, oCost);
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取失败：" + ex.Message, "对比");
            }
        }

        static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var cur = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (q)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else q = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') q = true;
                    else if (c == ',') { cur.Add(sb.ToString()); sb.Length = 0; }
                    else if (c == '\n') { cur.Add(sb.ToString()); sb.Length = 0; rows.Add(cur.ToArray()); cur.Clear(); }
                    else if (c == '\r') { }
                    else sb.Append(c);
                }
            }
            if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); rows.Add(cur.ToArray()); }
            return rows;
        }
    }

    static class Program
    {
        public static readonly string AppTitle = "DSH记账";

        // 把内嵌的 zstd 解压库（含配套托管库）释放到临时目录再加载，保持单文件分发
        static readonly string[] BundledDlls = new string[]
        {
            "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Buffers.dll",
            "System.Numerics.Vectors.dll",
            "System.Memory.dll",
            "System.Threading.Tasks.Extensions.dll",
            "ZstdSharp.dll"
        };

        static void EnsureZstd()
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "dsh-usage-lib");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(delegate(object s, ResolveEventArgs e)
                {
                    try
                    {
                        string simple = new AssemblyName(e.Name).Name;
                        string p = Path.Combine(dir, simple + ".dll");
                        if (File.Exists(p)) return Assembly.LoadFrom(p);
                    }
                    catch { }
                    return null;
                });

                var asm = typeof(Program).Assembly;
                foreach (string name in BundledDlls)
                {
                    using (var rs = asm.GetManifestResourceStream(name))
                    {
                        if (rs == null) continue;
                        var bytes = new byte[rs.Length];
                        rs.Read(bytes, 0, bytes.Length);
                        string path = Path.Combine(dir, name);
                        bool same = false;
                        try { if (File.Exists(path)) same = new FileInfo(path).Length == bytes.Length; } catch { }
                        if (!same) File.WriteAllBytes(path, bytes);
                    }
                }
                Assembly.LoadFrom(Path.Combine(dir, "ZstdSharp.dll"));
            }
            catch { }
        }

        [STAThread]
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        static extern int SetProcessDpiAwareness(int value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr h);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr h, int c);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);

        static System.Threading.Mutex SingleMutex;

        delegate bool EnumProc(IntPtr h, IntPtr l);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc cb, IntPtr l);

        // 已有实例就把它的窗口叫出来（托盘隐藏状态也能找回来）
        static bool ActivateRunning()
        {
            bool found = false;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
                GetWindowText(h, sb, 256);
                if (sb.ToString() == Program.AppTitle)
                {
                    ShowWindow(h, 9);
                    SetForegroundWindow(h);
                    found = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static void Main(string[] args)
        {
            // 优先按“每个显示器独立”声明 DPI，失败再退回系统级
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch
            {
                try { SetProcessDpiAwareness(1); }
                catch { try { SetProcessDPIAware(); } catch { } }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            EnsureZstd();

            // 正常双击启动时只允许一个实例：先把残留在托盘里的旧实例结束掉，
            // 否则用户看到的可能一直是旧构建的窗口（这正是“改了还是不对”的常见原因）
            if (args.Length == 0)
            {
                try
                {
                    int me = System.Diagnostics.Process.GetCurrentProcess().Id;
                    string selfName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                    foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName(selfName))
                    {
                        if (p.Id == me) continue;
                        try { p.Kill(); p.WaitForExit(1500); } catch { }
                    }
                }
                catch { }
                bool created;
                SingleMutex = new System.Threading.Mutex(true, "Local\\DSHJizhang-single", out created);
                if (!created) { ActivateRunning(); return; }
            }

            string shot = null;
            string checks = null;
            string diag = null;
            string dist = null;
            string csv = null;
            string setshot = null;
            string turnshot = null;
            int tab = 0;
            bool chain = false;
            string ra = null, rb = null;
            string rangeshot = null;
            string comboshot = null;
            string clicksim = null;
            string calshot = null;
            string live = null;
            string hdrsim = null;
            string foldsim = null;
            string cmpshot = null;
            string guideshot = null;
            bool guideDown = false;
            string selsim = null;
            int shotw = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot" && i + 1 < args.Length) shot = args[++i];
                else if (args[i] == "--check" && i + 1 < args.Length) checks = args[++i];
                else if (args[i] == "--diag" && i + 1 < args.Length) diag = args[++i];
                else if (args[i] == "--distshot" && i + 1 < args.Length) dist = args[++i];
                else if (args[i] == "--setshot" && i + 1 < args.Length) setshot = args[++i];
                else if (args[i] == "--turnshot" && i + 1 < args.Length) turnshot = args[++i];
                else if (args[i] == "--csv" && i + 1 < args.Length) csv = args[++i];
                else if (args[i] == "--tab" && i + 1 < args.Length) int.TryParse(args[++i], out tab);
                else if (args[i] == "--chain") chain = true;
                else if (args[i] == "--ra" && i + 1 < args.Length) ra = args[++i];
                else if (args[i] == "--rb" && i + 1 < args.Length) rb = args[++i];
                else if (args[i] == "--rangeshot" && i + 1 < args.Length) rangeshot = args[++i];
                else if (args[i] == "--comboshot" && i + 1 < args.Length) comboshot = args[++i];
                else if (args[i] == "--clicksim" && i + 1 < args.Length) clicksim = args[++i];
                else if (args[i] == "--calshot" && i + 1 < args.Length) calshot = args[++i];
                else if (args[i] == "--shotw" && i + 1 < args.Length) int.TryParse(args[++i], out shotw);
                else if (args[i] == "--live" && i + 1 < args.Length) live = args[++i];
                else if (args[i] == "--dump" && i + 1 < args.Length) MainForm.DumpPath = args[++i];
                else if (args[i] == "--hdrsim" && i + 1 < args.Length) hdrsim = args[++i];
                else if (args[i] == "--foldsim" && i + 1 < args.Length) foldsim = args[++i];
                else if (args[i] == "--cmpshot" && i + 1 < args.Length) cmpshot = args[++i];
                else if (args[i] == "--selsim" && i + 1 < args.Length) selsim = args[++i];
                else if (args[i] == "--cmpn" && i + 1 < args.Length) int.TryParse(args[++i], out MainForm.CmpN);
                else if (args[i] == "--guide" && i + 1 < args.Length) guideshot = args[++i];
                else if (args[i] == "--guidedown") guideDown = true;
            }
            MainForm.IsSelfCheck = args.Length > 0;   // 带任何参数启动都属于自检，不弹教程

            if (setshot != null)
            {
                var sf = new SettingsForm(Config.Load());
                sf.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();
                var b2 = new Bitmap(sf.Width, sf.Height);
                sf.DrawToBitmap(b2, new Rectangle(0, 0, sf.Width, sf.Height));
                b2.Save(setshot, System.Drawing.Imaging.ImageFormat.Png);
                b2.Dispose();
                sf.Close();
                return;
            }

            if (guideshot != null)
            {
                var gf = new GuideForm(Config.Load(), true);
                gf.Show();
                if (guideDown) { Application.DoEvents(); gf.ToBottom(); }
                Application.DoEvents();
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();
                var b5 = new Bitmap(gf.Width, gf.Height);
                gf.DrawToBitmap(b5, new Rectangle(0, 0, gf.Width, gf.Height));
                b5.Save(guideshot, System.Drawing.Imaging.ImageFormat.Png);
                b5.Dispose();
                gf.Close();
                return;
            }

            if (calshot != null)
            {
                var rf2 = new RangeForm(DateTime.Today, DateTime.Now);
                rf2.CalShot(calshot);
                return;
            }

            if (rangeshot != null)            {
                var rf = new RangeForm(DateTime.Today, DateTime.Now);
                rf.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();
                var b3 = new Bitmap(rf.Width, rf.Height);
                rf.DrawToBitmap(b3, new Rectangle(0, 0, rf.Width, rf.Height));
                b3.Save(rangeshot, System.Drawing.Imaging.ImageFormat.Png);
                b3.Dispose();
                rf.Close();
                return;
            }

            var form = new MainForm();
            if (cmpshot != null) { form.CompareShot(cmpshot); return; }
            if (selsim != null) { form.SelSim(selsim); return; }
            if (foldsim != null) { form.FoldSim(foldsim); return; }
            if (hdrsim != null) { form.HdrSim(hdrsim); return; }
            if (live != null) { form.LiveShot(live, shotw); return; }
            if (diag != null) { form.Diag(diag, ra, rb); return; }
            if (comboshot != null) { form.ComboShot(comboshot); return; }
            if (turnshot != null) { form.TurnShot(turnshot, checks); return; }
            if (dist != null) { form.DistShot(dist, checks, tab, csv, ra, rb); return; }
            if (shot != null)
            {
                if (shotw > 0) form.ClientSize = new Size(shotw, form.ClientSize.Height);
                form.Shot(shot, checks, chain, ra, rb);
                return;
            }
            if (clicksim != null) { form.ClickSim(clicksim); return; }
            Application.Run(form);
        }
    }
}

