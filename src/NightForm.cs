using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Bubliklyuk
{
    internal sealed class NightForm : Form
    {
        private static readonly Color Navy = Color.FromArgb(23, 27, 53);
        private static readonly Color Lime = Color.FromArgb(160, 240, 0);
        private static readonly Color Cream = Color.FromArgb(255, 248, 232);
        private readonly DateTime startTime;
        internal readonly DateTime EndTime;
        private readonly Action failure;
        private readonly Dictionary<string, Image> images = new Dictionary<string, Image>();
        private readonly System.Windows.Forms.Timer frames;
        private readonly System.Windows.Forms.Timer focus;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private bool allowedClose;
        private bool stopping;
        private float scale = 1, ox, oy;
        private long nextPhrase = 24000;
        private int phraseIndex;
        private string remaining = "";
        private readonly string[] phrases = new string[] {
            "У меня даже тапок перегрелся", "Вентилятор дует. Я отдыхаю",
            "Сегодня захвачу только подушку", "До утра работаю бубликом" };

        internal NightForm(DateTime start, DateTime end, Action failure)
        {
            startTime = start;
            EndTime = end;
            this.failure = failure;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Navy;
            Text = "Глюкобой — пора остывать";
            images["night_bg.png"] = Load("night_bg.png");
            images["night_virus.png"] = Load("night_virus.png");
            frames = new System.Windows.Forms.Timer();
            frames.Interval = 40;
            frames.Tick += Frame;
            focus = new System.Windows.Forms.Timer();
            focus.Interval = 500;
            focus.Tick += FocusTick;
            LayoutScene();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutScene();
        }
        private void LayoutScene()
        {
            scale = Math.Min(ClientSize.Width / 1920f, ClientSize.Height / 1080f);
            if (scale <= 0) scale = 1;
            ox = (ClientSize.Width - 1920 * scale) / 2;
            oy = (ClientSize.Height - 1080 * scale) / 2;
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            frames.Start();
            focus.Start();
            FocusTick(this, EventArgs.Empty);
        }
        private void Frame(object sender, EventArgs e)
        {
            if (stopping) return;
            try
            {
                if (clock.ElapsedMilliseconds >= nextPhrase)
                {
                    phraseIndex = (phraseIndex + 1) % phrases.Length;
                    nextPhrase = clock.ElapsedMilliseconds + random.Next(20000, 30001);
                    Invalidate(Rectangle.Ceiling(R(60, 830, 590, 150)));
                }
                double minutes = Math.Max(0, Math.Ceiling((EndTime - DateTime.Now).TotalMinutes));
                int total = (int)Math.Min(minutes, 1440);
                string text = (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
                if (text != remaining)
                {
                    remaining = text;
                    Invalidate(Rectangle.Ceiling(R(730, 510, 1100, 130)));
                }
                Invalidate(Rectangle.Ceiling(R(60, 210, 590, 610)));
                Invalidate(Rectangle.Ceiling(R(780, 700, 930, 220)));
            }
            catch { Fail(); }
        }
        private void FocusTick(object sender, EventArgs e)
        {
            if (stopping || IsDisposed || !Visible) return;
            try { TopMost = true; BringToFront(); Native.SetForegroundWindow(Handle); Activate(); }
            catch { Fail(); }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowedClose && e.CloseReason != CloseReason.WindowsShutDown)
                e.Cancel = true;
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            stopping = true;
            StopTimers();
            base.OnFormClosed(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (SolidBrush brush = new SolidBrush(Navy)) g.FillRectangle(brush, ClientRectangle);
                Image background = images["night_bg.png"];
                if (background != null)
                {
                    float ratio = Math.Max(ClientSize.Width / (float)background.Width, ClientSize.Height / (float)background.Height);
                    float w = background.Width * ratio, h = background.Height * ratio;
                    g.DrawImage(background, (ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
                }
                // Quiet opaque panel ensures legibility regardless of the supplied background.
                using (GraphicsPath path = AttackForm.Rounded(R(700, 180, 1156, 770), 30 * scale))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(235, Navy))) g.FillPath(brush, path);
                float breathe = 3 * scale * (float)Math.Sin(clock.ElapsedMilliseconds * Math.PI / 2200.0);
                RectangleF virus = R(84, 270, 520, 520);
                virus.Offset(0, breathe);
                DrawAsset(g, images["night_virus.png"], virus, Color.FromArgb(165, 107, 255));
                TextAt(g, "Пора остывать", 60, Cream, R(750, 240, 1056, 100));
                TextAt(g, "Ноутбук отдыхает до " + EndTime.ToString("HH:mm"), 36, Cream, R(750, 360, 1056, 80));
                TextAt(g, "До утра: " + remaining, 58, Lime, R(750, 520, 1056, 100));
                TextAt(g, "Режим остывания", 28, Cream, R(1000, 730, 380, 65));
                DrawThermometer(g);
                DrawFan(g);
                using (GraphicsPath path = AttackForm.Rounded(R(64, 850, 560, 110), 22 * scale))
                using (SolidBrush brush = new SolidBrush(Navy)) g.FillPath(brush, path);
                TextAt(g, phrases[phraseIndex], 30, Cream, R(85, 860, 518, 90));
                base.OnPaint(e);
            }
            catch
            {
                if (!stopping) { StopTimers(); BeginInvoke(new Action(Fail)); }
            }
        }
        private void DrawThermometer(Graphics g)
        {
            float cooled = (float)Math.Max(0, Math.Min(1, (DateTime.Now - startTime).TotalMinutes / 3));
            RectangleF tube = R(850, 720, 52, 150);
            using (GraphicsPath path = AttackForm.Rounded(tube, 26 * scale))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(62, 67, 91))) g.FillPath(brush, path);
            float amount = 1 - 0.72f * cooled;
            RectangleF fill = R(864, 736 + 118 * (1 - amount), 24, 118 * amount);
            Color color = cooled < 0.5f ? Color.FromArgb(255, 212, 92) : Lime;
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, fill);
                g.FillEllipse(brush, R(844, 844, 64, 64));
            }
        }
        private void DrawFan(Graphics g)
        {
            RectangleF box = R(1450, 728, 170, 170);
            float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
            using (Pen rim = new Pen(Color.FromArgb(90, 100, 118), Math.Max(2, 4 * scale))) g.DrawEllipse(rim, box);
            GraphicsState saved = g.Save();
            try
            {
                g.TranslateTransform(x, y);
                g.RotateTransform((clock.ElapsedMilliseconds % 6000) * 360f / 6000);
                using (SolidBrush blade = new SolidBrush(Color.FromArgb(150, Lime)))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        g.FillEllipse(blade, new RectangleF(-15 * scale, -70 * scale, 42 * scale, 74 * scale));
                        g.RotateTransform(120);
                    }
                }
                using (SolidBrush hub = new SolidBrush(Cream)) g.FillEllipse(hub, -13 * scale, -13 * scale, 26 * scale, 26 * scale);
            }
            finally { g.Restore(saved); }
        }
        private void TextAt(Graphics g, string text, float size, Color color, RectangleF box)
        {
            using (Font font = new Font("Segoe UI", Math.Max(18, size * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.DrawString(text, font, brush, box, format);
            }
        }
        private RectangleF R(float x, float y, float w, float h)
        {
            return new RectangleF(ox + x * scale, oy + y * scale, w * scale, h * scale);
        }
        private static void DrawAsset(Graphics g, Image image, RectangleF box, Color placeholder)
        {
            if (image == null) { using (SolidBrush brush = new SolidBrush(placeholder)) g.FillRectangle(brush, box); return; }
            float ratio = Math.Min(box.Width / image.Width, box.Height / image.Height);
            float w = image.Width * ratio, h = image.Height * ratio;
            g.DrawImage(image, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }
        private static Image Load(string name)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    using (Image image = Image.FromStream(stream)) return new Bitmap(image);
                }
            }
            catch { return null; }
        }
        private void Fail() { if (!stopping) failure(); }
        private void StopTimers()
        {
            if (frames != null) frames.Stop();
            if (focus != null) focus.Stop();
        }
        internal void Abort()
        {
            allowedClose = true;
            stopping = true;
            StopTimers();
            try { Close(); } finally { Dispose(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stopping = true;
                StopTimers();
                if (frames != null) frames.Dispose();
                if (focus != null) focus.Dispose();
                foreach (Image image in images.Values) if (image != null) image.Dispose();
                images.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
