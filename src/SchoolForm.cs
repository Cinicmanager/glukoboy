using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Bubliklyuk
{
    internal sealed class SchoolForm : Form
    {
        private static readonly Color Navy = Color.FromArgb(23, 27, 53);
        private static readonly Color Lime = Color.FromArgb(160, 240, 0);
        private static readonly Color Cream = Color.FromArgb(255, 248, 232);
        internal readonly DateTime EndTime;
        private readonly Action parentUnlock;
        private readonly Action<Exception> failure;
        private readonly Dictionary<string, Image> images = new Dictionary<string, Image>();
        private readonly System.Windows.Forms.Timer frames;
        private readonly System.Windows.Forms.Timer focus;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private readonly Font titleFont;
        private readonly Font bodyFont;
        private readonly Font countdownFont;
        private readonly Font controlFont;
        private readonly Font parentTitleFont;
        private readonly Font exampleFont;
        private readonly Button unlock;
        private readonly Panel parentPanel;
        private readonly Label parentTitle;
        private readonly Label example;
        private readonly TextBox answer;
        private readonly Button check;
        private readonly Button cancel;
        private readonly Label result;
        private bool allowedClose;
        private bool stopping;
        private bool unlocking;
        private bool failurePending;
        private bool resourcesDisposed;
        private float scale = 1, ox, oy;
        private int expected;
        private string remaining = "";

        internal SchoolForm(DateTime start, DateTime end, Action parentUnlock, Action<Exception> failure)
        {
            EndTime = end;
            this.parentUnlock = parentUnlock;
            this.failure = failure;
            try
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                DoubleBuffered = true;
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Bounds = Screen.PrimaryScreen.Bounds;
                ShowInTaskbar = false;
                TopMost = true;
                KeyPreview = true;
                BackColor = Navy;
                Text = "Глюкобой — пора в школу";
                float fontScale = Math.Min(Screen.PrimaryScreen.Bounds.Width / 1920f,
                    Screen.PrimaryScreen.Bounds.Height / 1080f);
                // Create and assign fonts once. LayoutScene must only change bounds.
                titleFont = new Font("Segoe UI", Math.Max(18, 68 * fontScale), FontStyle.Bold, GraphicsUnit.Pixel);
                bodyFont = new Font("Segoe UI", Math.Max(14, 34 * fontScale), FontStyle.Regular, GraphicsUnit.Pixel);
                countdownFont = new Font("Segoe UI", Math.Max(18, 76 * fontScale), FontStyle.Bold, GraphicsUnit.Pixel);
                controlFont = new Font("Segoe UI", Math.Max(12, 26 * fontScale), FontStyle.Regular, GraphicsUnit.Pixel);
                parentTitleFont = new Font("Segoe UI", Math.Max(14, 32 * fontScale), FontStyle.Bold, GraphicsUnit.Pixel);
                exampleFont = new Font("Segoe UI", Math.Max(16, 44 * fontScale), FontStyle.Bold, GraphicsUnit.Pixel);
                images["fon.png"] = Load("fon.png");
                images["school_virus.png"] = Load("school_virus.png");

                unlock = MakeButton("Сегодня не надо в школу");
                unlock.ForeColor = Color.FromArgb(180, 185, 196);
                unlock.Click += ShowParent;
                Controls.Add(unlock);

                parentPanel = new Panel();
                parentPanel.BackColor = Navy;
                parentPanel.BorderStyle = BorderStyle.FixedSingle;
                parentPanel.Visible = false;
                Controls.Add(parentPanel);
                parentTitle = MakeLabel("Для взрослых: решите пример", parentTitleFont);
                example = MakeLabel("", exampleFont);
                result = MakeLabel("", controlFont);
                result.ForeColor = Lime;
                answer = new TextBox();
                answer.Font = controlFont;
                answer.Multiline = true;
                answer.AcceptsReturn = false;
                answer.WordWrap = false;
                answer.MaxLength = 4;
                answer.TextAlign = HorizontalAlignment.Center;
                answer.BackColor = Cream;
                answer.ForeColor = Navy;
                answer.KeyPress += DigitsOnly;
                answer.KeyDown += AnswerKeyDown;
                answer.TextChanged += AnswerChanged;
                check = MakeButton("Проверить");
                check.Click += CheckAnswer;
                cancel = MakeButton("Отмена");
                cancel.Click += HideParent;
                parentPanel.Controls.Add(parentTitle);
                parentPanel.Controls.Add(example);
                parentPanel.Controls.Add(answer);
                parentPanel.Controls.Add(check);
                parentPanel.Controls.Add(cancel);
                parentPanel.Controls.Add(result);

                frames = new System.Windows.Forms.Timer();
                frames.Interval = 40;
                frames.Tick += Frame;
                focus = new System.Windows.Forms.Timer();
                focus.Interval = 500;
                focus.Tick += FocusTick;
                UpdateRemaining();
                LayoutScene();
            }
            catch (Exception ex)
            {
                Diag.Log("SchoolForm.ctor", ex);
                try { Fail(ex); }
                finally { Dispose(); }
                throw;
            }
        }
        private Button MakeButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.Font = controlFont;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = Navy;
            button.ForeColor = Cream;
            button.UseVisualStyleBackColor = false;
            button.UseMnemonic = false;
            return button;
        }
        private static Label MakeLabel(string text, Font font)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = Cream;
            label.BackColor = Navy;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.UseMnemonic = false;
            return label;
        }
        protected override void OnResize(EventArgs e)
        {
            try
            {
                base.OnResize(e);
                LayoutScene();
            }
            catch (Exception ex) { Diag.Log("SchoolForm.OnResize", ex); Fail(ex); }
        }
        private void LayoutScene()
        {
            scale = Math.Min(ClientSize.Width / 1920f, ClientSize.Height / 1080f);
            if (scale <= 0) scale = 1;
            ox = (ClientSize.Width - 1920 * scale) / 2;
            oy = (ClientSize.Height - 1080 * scale) / 2;
            if (unlock == null || parentPanel == null || cancel == null) return;
            unlock.Bounds = Rectangle.Round(R(1030, 835, 760, 76));
            parentPanel.Bounds = Rectangle.Round(R(835, 650, 940, 330));
            parentTitle.Bounds = Local(20, 12, 900, 50);
            example.Bounds = Local(20, 68, 900, 64);
            answer.Bounds = Local(38, 145, 370, 58);
            check.Bounds = Local(430, 145, 240, 58);
            cancel.Bounds = Local(690, 145, 210, 58);
            result.Bounds = Local(20, 235, 900, 55);
        }
        private Rectangle Local(float x, float y, float w, float h)
        {
            return Rectangle.Round(new RectangleF(x * scale, y * scale, w * scale, h * scale));
        }
        protected override void OnShown(EventArgs e)
        {
            try
            {
                base.OnShown(e);
                frames.Start();
                focus.Start();
                FocusTick(this, EventArgs.Empty);
            }
            catch (Exception ex) { Diag.Log("SchoolForm.OnShown", ex); Fail(ex); }
        }
        private void UpdateRemaining()
        {
            int total = (int)Math.Min(86400, Math.Max(0, Math.Ceiling((EndTime - DateTime.Now).TotalSeconds)));
            remaining = (total / 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }
        private void Frame(object sender, EventArgs e)
        {
            if (stopping) return;
            try
            {
                string previous = remaining;
                UpdateRemaining();
                if (previous != remaining) Invalidate(Rectangle.Ceiling(R(800, 520, 1000, 110)));
                Invalidate(Rectangle.Ceiling(R(40, 200, 720, 720)));
            }
            catch (Exception ex) { Diag.Log("SchoolForm.Frame", ex); Fail(ex); }
        }
        private void FocusTick(object sender, EventArgs e)
        {
            if (stopping || IsDisposed || !Visible) return;
            try { TopMost = true; BringToFront(); Native.SetForegroundWindow(Handle); Activate(); }
            catch (Exception ex) { Diag.Log("SchoolForm.FocusTick", ex); Fail(ex); }
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
                Image background = images["fon.png"];
                if (background != null)
                {
                    float ratio = Math.Max(ClientSize.Width / (float)background.Width, ClientSize.Height / (float)background.Height);
                    float w = background.Width * ratio, h = background.Height * ratio;
                    g.DrawImage(background, (ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
                }
                using (GraphicsPath path = AttackForm.Rounded(R(770, 180, 1090, 810), 30 * scale))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(235, Navy))) g.FillPath(brush, path);
                float breathe = 3 * scale * (float)Math.Sin(clock.ElapsedMilliseconds * Math.PI / 2200.0);
                RectangleF virus = R(50, 210, 700, 700);
                virus.Offset(0, breathe);
                DrawAsset(g, images["school_virus.png"], virus, Color.FromArgb(165, 107, 255));
                TextAt(g, "Пора в школу!", titleFont, Lime, R(800, 230, 1000, 110));
                TextAt(g, "Собирайся: портфель, форма, завтрак.", bodyFont, Cream, R(800, 350, 1000, 70));
                TextAt(g, "Компьютер отдыхает до " + EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    bodyFont, Cream, R(800, 430, 1000, 70));
                TextAt(g, remaining, countdownFont, Lime, R(800, 520, 1000, 110));
                base.OnPaint(e);
            }
            catch (Exception ex)
            {
                Diag.Log("SchoolForm.OnPaint", ex);
                if (!stopping && !failurePending)
                {
                    failurePending = true;
                    StopTimers();
                    try { BeginInvoke(new Action(delegate { Fail(ex); })); }
                    catch (Exception dispatchError) { Diag.Log("SchoolForm.OnPaint.Dispatch", dispatchError); Fail(dispatchError); }
                }
            }
        }
        private void TextAt(Graphics g, string text, Font font, Color color, RectangleF box)
        {
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
        private Image Load(string name)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    using (Image image = Image.FromStream(stream)) return new Bitmap(image);
                }
            }
            catch (Exception ex) { Diag.Log("SchoolForm.Load " + name, ex); Fail(ex); return null; }
        }
        private void ShowParent(object sender, EventArgs e)
        {
            if (stopping) return;
            try
            {
                NewExample();
                result.Text = "";
                unlock.Visible = false;
                parentPanel.Visible = true;
                parentPanel.BringToFront();
                answer.Focus();
            }
            catch (Exception ex) { Diag.Log("SchoolForm.ShowParent", ex); Fail(ex); }
        }
        private void NewExample()
        {
            int a, b;
            do
            {
                a = random.Next(23, 99);
                b = random.Next(23, 99);
            }
            while (a % 10 == 0 || b % 10 == 0 || a * b < 1000 || a * b == expected);
            expected = a * b;
            example.Text = a.ToString(CultureInfo.InvariantCulture) + " × " +
                b.ToString(CultureInfo.InvariantCulture) + " = ?";
            answer.Clear();
        }
        private void CheckAnswer(object sender, EventArgs e)
        {
            if (stopping || unlocking || !parentPanel.Visible) return;
            try
            {
                string text = answer.Text;
                bool digits = text.Length > 0 && text.Length <= 4;
                foreach (char ch in text)
                    if (ch < '0' || ch > '9') { digits = false; break; }
                int value;
                if (digits && Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value == expected)
                {
                    unlocking = true;
                    parentPanel.Visible = false;
                    BeginInvoke(new Action(delegate
                    {
                        try { if (!stopping) parentUnlock(); }
                        catch (Exception ex) { Diag.Log("SchoolForm.Unlock", ex); Fail(ex); }
                    }));
                    return;
                }
                result.Text = "Неверно, новый пример";
                NewExample();
                answer.Focus();
            }
            catch (Exception ex) { Diag.Log("SchoolForm.CheckAnswer", ex); Fail(ex); }
        }
        private void HideParent(object sender, EventArgs e)
        {
            if (stopping) return;
            try
            {
                answer.Clear();
                result.Text = "";
                parentPanel.Visible = false;
                unlock.Visible = true;
                unlock.Focus();
            }
            catch (Exception ex) { Diag.Log("SchoolForm.HideParent", ex); Fail(ex); }
        }
        private void DigitsOnly(object sender, KeyPressEventArgs e)
        {
            if (!Char.IsControl(e.KeyChar) && (e.KeyChar < '0' || e.KeyChar > '9')) e.Handled = true;
        }
        private void AnswerChanged(object sender, EventArgs e)
        {
            if (stopping) return;
            try
            {
                string text = answer.Text;
                string digits = "";
                int caret = 0;
                int selection = answer.SelectionStart;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] < '0' || text[i] > '9') continue;
                    digits += text[i];
                    if (i < selection) caret++;
                }
                if (digits == text) return;
                answer.Text = digits;
                answer.SelectionStart = Math.Min(caret, digits.Length);
            }
            catch (Exception ex) { Diag.Log("SchoolForm.AnswerChanged", ex); Fail(ex); }
        }
        private void AnswerKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CheckAnswer(sender, EventArgs.Empty);
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            try
            {
                if (parentPanel != null && parentPanel.Visible && keyData == Keys.Escape)
                {
                    HideParent(this, EventArgs.Empty);
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            catch (Exception ex) { Diag.Log("SchoolForm.ProcessCmdKey", ex); Fail(ex); return true; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (!allowedClose && e.CloseReason != CloseReason.WindowsShutDown) e.Cancel = true;
                base.OnFormClosing(e);
            }
            catch (Exception ex) { Diag.Log("SchoolForm.OnFormClosing", ex); Fail(ex); }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                stopping = true;
                StopTimers();
                base.OnFormClosed(e);
            }
            catch (Exception ex) { Diag.Log("SchoolForm.OnFormClosed", ex); failure(ex); }
        }
        private void Fail(Exception ex) { if (!stopping) failure(ex); }
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
            if (disposing && !resourcesDisposed)
            {
                stopping = true;
                StopTimers();
                if (frames != null) frames.Dispose();
                if (focus != null) focus.Dispose();
                clock.Stop();
                foreach (Image image in images.Values) if (image != null) image.Dispose();
                images.Clear();
            }
            // Dispose controls before disposing the fonts they still reference.
            base.Dispose(disposing);
            if (disposing && !resourcesDisposed)
            {
                resourcesDisposed = true;
                if (titleFont != null) titleFont.Dispose();
                if (bodyFont != null) bodyFont.Dispose();
                if (countdownFont != null) countdownFont.Dispose();
                if (controlFont != null) controlFont.Dispose();
                if (parentTitleFont != null) parentTitleFont.Dispose();
                if (exampleFont != null) exampleFont.Dispose();
            }
        }
    }
}
