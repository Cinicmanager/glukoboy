using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Bubliklyuk
{
    internal sealed class AttackForm : Form
    {
        private enum Stage { Asking, Hit, Defeated }

        private static readonly Color Navy = Color.FromArgb(0x17, 0x1B, 0x35);
        private static readonly Color Cream = Color.FromArgb(0xFF, 0xF8, 0xE8);
        private static readonly Color Ink = Color.FromArgb(0x20, 0x24, 0x3A);
        private static readonly Color Purple = Color.FromArgb(0xA5, 0x6B, 0xFF);
        private static readonly Color Lime = Color.FromArgb(0xA0, 0xF0, 0x00);
        private static readonly Color Mint = Color.FromArgb(0x65, 0xE8, 0xCD);
        private static readonly Color Yellow = Color.FromArgb(0xFF, 0xD4, 0x5C);

        private readonly TaskGenerator generator;
        private readonly int taskCount;
        private readonly Action<Exception> failure;
        private readonly Dictionary<string, Image> images = new Dictionary<string, Image>();
        private readonly TextBox answer;
        private readonly ActionButton submit;
        private readonly System.Windows.Forms.Timer animationTimer;
        private readonly System.Windows.Forms.Timer focusTimer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private readonly int idleLeave;
        private Font idleFont;
        private string idleText = "";
        private Font inputFont;
        private Font buttonFont;
        private Exercise exercise;
        private Stage stage;
        private string virusImage = "attack.png";
        private string phrase = "Компьютер будет моим!";
        private string resultText = "";
        private bool wrongAnswer;
        private bool stopping;
        private int completed;
        private int shakeX;
        private int shakeY;
        private long appearanceStarted;
        private long phraseStarted;
        private long angryStarted = -1;
        private long nextGlitchAt;
        private long glitchUntil;
        private int glitchSeed;
        private int currentTaskIndex;
        private int correctStreak;
        private long cardShownAt;
        private bool impactSpawned;
        private readonly List<BattleParticle> particles = new List<BattleParticle>();
        private long stageStarted;
        private long barStarted;
        private float displayedBar = 100;
        private float barFrom = 100;
        private float barTarget = 100;
        private float scale = 1;
        private float originX;
        private float originY;

        private float AnswerRowY { get { return exercise != null && exercise.LongText ? 664 : 552; } }

        internal AttackForm(Settings settings, TaskGenerator generator, int taskCount, Action<Exception> failure)
        {
            this.generator = generator;
            this.taskCount = taskCount;
            this.failure = failure;
            idleLeave = settings.IdleLeave;
            if (idleLeave > 0) idleFont = new Font("Segoe UI", 24, FontStyle.Regular, GraphicsUnit.Pixel);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = Screen.PrimaryScreen.Bounds;
            TopMost = true;
            ShowInTaskbar = false;
            Text = "Глюкобой";
            BackColor = Navy;

            answer = new TextBox();
            answer.BorderStyle = BorderStyle.None;
            answer.TextAlign = HorizontalAlignment.Center;
            answer.BackColor = Color.White;
            answer.ForeColor = Ink;
            answer.MaxLength = 16;
            answer.AutoSize = true;
            answer.Multiline = false;
            answer.TabIndex = 0;
            answer.AccessibleName = "Ответ";
            answer.KeyDown += AnswerKeyDown;
            answer.Enter += InputFocusChanged;
            answer.Leave += InputFocusChanged;

            submit = new ActionButton();
            submit.Text = "Отбить!";
            submit.TabIndex = 1;
            submit.Click += SubmitClicked;
            Controls.Add(answer);
            Controls.Add(submit);
            AcceptButton = submit;

            animationTimer = new System.Windows.Forms.Timer();
            animationTimer.Interval = 16;
            animationTimer.Tick += AnimationTick;
            focusTimer = new System.Windows.Forms.Timer();
            focusTimer.Interval = 500;
            focusTimer.Tick += FocusTick;
            string[] names = new string[] { "attack.png", "angry.png", "hit.png", "defeated.png", "fon.png", "shield.png", "counter.png" };
            foreach (string name in names) images[name] = LoadImage(name);
            LayoutScene();
            if (idleLeave > 0) UpdateIdleCountdown(Native.IdleTime());
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                Bounds = Screen.PrimaryScreen.Bounds;
                appearanceStarted = clock.ElapsedMilliseconds;
                phraseStarted = appearanceStarted;
                nextGlitchAt = appearanceStarted + random.Next(2000, 4001);
                NewExercise();
                animationTimer.Start();
                focusTimer.Start();
                ForceFocus();
            }
            catch (Exception ex) { Diag.Log("AttackForm.OnShown", ex); Fail(ex); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (answer == null || submit == null) return;
            try { LayoutScene(); }
            catch (Exception ex) { Diag.Log("AttackForm.OnResize", ex); Fail(ex); }
        }

        private void LayoutScene()
        {
            scale = Math.Min(ClientSize.Width / 1920f, ClientSize.Height / 1080f);
            if (scale <= 0) scale = 1;
            originX = (ClientSize.Width - 1920 * scale) / 2;
            originY = (ClientSize.Height - 1080 * scale) / 2;
            float inputSize = Math.Max(24, 48 * scale);
            if (inputFont == null || inputFont.Size != inputSize)
            {
                Font newInput = new Font("Segoe UI", inputSize, FontStyle.Regular, GraphicsUnit.Pixel);
                answer.Font = newInput;
                if (Object.ReferenceEquals(answer.Font, newInput))
                {
                    Font oldInput = inputFont;
                    inputFont = newInput;
                    if (oldInput != null) oldInput.Dispose();
                }
                else
                {
                    newInput.Dispose();
                }
            }
            float buttonSize = Math.Max(20, 34 * scale);
            if (buttonFont == null || buttonFont.Size != buttonSize)
            {
                Font newButton = new Font("Segoe UI", buttonSize, FontStyle.Bold, GraphicsUnit.Pixel);
                submit.Font = newButton;
                if (Object.ReferenceEquals(submit.Font, newButton))
                {
                    Font oldButton = buttonFont;
                    buttonFont = newButton;
                    if (oldButton != null) oldButton.Dispose();
                }
                else
                {
                    newButton.Dispose();
                }
            }
            // Both controls share the same outer 112-unit row. The native single-line
            // editor is vertically centered inside a separately painted rounded frame.
            RectangleF field = R(791, AnswerRowY, 620, 112);
            int textHeight = answer.PreferredHeight;
            answer.Bounds = new Rectangle((int)Math.Round(field.X + 24 * scale),
                (int)Math.Round(field.Y + (field.Height - textHeight) / 2),
                Math.Max(1, (int)Math.Round(field.Width - 48 * scale)), textHeight);
            submit.Bounds = Rectangle.Round(R(1439, AnswerRowY, 312, 112));
            Invalidate();
        }

        private void InputFocusChanged(object sender, EventArgs e)
        {
            if (!stopping) Invalidate(Rectangle.Ceiling(R(785, AnswerRowY - 6, 632, 124)));
        }

        private void SetPhrase(string text)
        {
            phrase = text;
            phraseStarted = clock.ElapsedMilliseconds;
        }

        private void NewExercise()
        {
            exercise = generator.Next();
            LayoutScene();
            currentTaskIndex = completed + 1;
            cardShownAt = clock.ElapsedMilliseconds;
            stage = Stage.Asking;
            virusImage = "attack.png";
            angryStarted = -1;
            wrongAnswer = false;
            resultText = "";
            if (completed > 0) SetPhrase("Я захвачу даже калькулятор!");
            answer.Clear();
            answer.Enabled = true;
            submit.Enabled = true;
            answer.Focus();
            Invalidate();
        }

        private void AnswerKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            e.Handled = true;
            CheckAnswer();
        }

        private void SubmitClicked(object sender, EventArgs e) { CheckAnswer(); }

        private void CheckAnswer()
        {
            if (stopping || stage != Stage.Asking || exercise == null) return;
            try
            {
                if (answer.Text.Trim().Length == 0) return;
                if (!exercise.Matches(answer.Text))
                {
                    virusImage = "counter.png";
                    correctStreak = 0;
                    angryStarted = clock.ElapsedMilliseconds;
                    SetPhrase(exercise.Kind == "math" ? "Хм. Пересчитай ещё раз" : "Хм. Прочитай ещё раз");
                    // Also fits reading exercises: the explicit prompt remains unchanged.
                    resultText = "Попробуй ещё";
                    wrongAnswer = true;
                    answer.Focus();
                    answer.SelectAll();
                    Invalidate();
                    return;
                }
                completed++;
                correctStreak++;
                impactSpawned = false;
                long now = clock.ElapsedMilliseconds;
                UpdateBar(now);
                barFrom = displayedBar;
                barTarget = 100f * (taskCount - completed) / taskCount;
                barStarted = now + 320;
                stageStarted = now;
                stage = Stage.Hit;
                angryStarted = -1;
                glitchSeed = random.Next(10000);
                virusImage = "attack.png";
                string[] phrases = new string[] { "Ай! Прямо в дырку!", "Кто положил цифры в мой суп?", "Мой тапок требует реванша!" };
                SetPhrase(correctStreak >= 2 ? "Два подряд!" : phrases[random.Next(phrases.Length)]);
                resultText = "Верно! Атака ослабла";
                wrongAnswer = false;
                answer.Enabled = false;
                submit.Enabled = false;
                Invalidate();
            }
            catch (Exception ex) { Diag.Log("AttackForm.CheckAnswer", ex); Fail(ex); }
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            if (stopping) return;
            try
            {
                long now = clock.ElapsedMilliseconds;
                bool barChanged = false;
                if (Math.Abs(displayedBar - barTarget) > 0.001f)
                {
                    UpdateBar(now);
                    barChanged = true;
                }
                shakeX = 0;
                shakeY = 0;
                if (stage == Stage.Asking && angryStarted >= 0)
                {
                    if (now - angryStarted < 500)
                    {
                        shakeX = random.Next(-4, 5);
                        shakeY = random.Next(-4, 5);
                    }
                    else
                    {
                        angryStarted = -1;
                        virusImage = "attack.png";
                    }
                }
                if (stage == Stage.Asking && angryStarted < 0 && now >= nextGlitchAt)
                {
                    glitchUntil = now + 120;
                    glitchSeed = random.Next(10000);
                    nextGlitchAt = now + random.Next(2000, 4001);
                }
                if (stage == Stage.Hit)
                {
                    long elapsed = now - stageStarted;
                    if (elapsed >= 320 && !impactSpawned)
                    {
                        impactSpawned = true;
                        virusImage = "hit.png";
                        SpawnParticles(now, completed >= taskCount ? 24 : 12, false);
                    }
                    if (elapsed >= 320 && elapsed < 720)
                    {
                        shakeX = random.Next(-6, 7);
                        shakeY = random.Next(-4, 5);
                    }
                    if (elapsed >= (completed >= taskCount ? 1000 : 800))
                    {
                        if (completed >= taskCount)
                        {
                            stage = Stage.Defeated;
                            stageStarted = now;
                            virusImage = "defeated.png";
                            SetPhrase("Ладно, я за новым тапком");
                            resultText = "Атака отбита!";
                            SpawnParticles(now, 24, true);
                            Invalidate(Rectangle.Ceiling(R(686, 216, 1170, 740)));
                        }
                        else NewExercise();
                    }
                }
                else if (stage == Stage.Defeated && now - stageStarted >= 2000)
                {
                    Close();
                    return;
                }
                // The input controls/card stay still. Windows clips WM_PAINT to these
                // regions, so the idle animation never repaints the whole screen.
                Invalidate(Rectangle.Ceiling(R(0, 168, 686, 620)));
                Invalidate(Rectangle.Ceiling(R(48, 788, 592, 184)));
                particles.RemoveAll(delegate(BattleParticle p) { return now - p.Born >= p.Life; });
                if (stage == Stage.Hit)
                    Invalidate(Rectangle.Ceiling(R(636, 300, 120, 370)));
                if (stage == Stage.Asking && now - cardShownAt < 240)
                    Invalidate(Rectangle.Ceiling(R(750, 240, 1040, 410)));
                if (stage == Stage.Asking && angryStarted >= 0 && now - angryStarted < 380)
                    Invalidate(Rectangle.Ceiling(R(680, 210, 1182, 752)));
                if (barChanged || (stage == Stage.Hit && now - barStarted < 340))
                    Invalidate(Rectangle.Ceiling(R(60, 26, 1800, 140)));
            }
            catch (Exception ex) { Diag.Log("AttackForm.AnimationTick", ex); Fail(ex); }
        }

        internal void UpdateIdleCountdown(TimeSpan idle)
        {
            if (stopping || idleLeave <= 0) return;
            int seconds = (int)Math.Ceiling(Math.Max(0, idleLeave * 60.0 - idle.TotalSeconds));
            string remaining = idleLeave >= 60 && seconds >= 3600 ?
                String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60) :
                String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", seconds / 60, seconds % 60);
            string text = "Не трогай ноутбук " + remaining + " — и вирус уйдёт сам";
            if (idleText == text) return;
            idleText = text;
            Invalidate(Rectangle.Ceiling(R(64, 1000, 1792, 48)));
        }

        private void DrawIdleCountdown(Graphics g)
        {
            if (idleFont == null || idleText.Length == 0) return;
            GraphicsState saved = g.Save();
            try
            {
                g.TranslateTransform(originX, originY);
                g.ScaleTransform(scale, scale);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(160, Cream)))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(idleText, idleFont, brush, new RectangleF(64, 1000, 1792, 48), format);
                }
            }
            finally { g.Restore(saved); }
        }

        private void UpdateBar(long now)
        {
            float fraction = Math.Min(1f, Math.Max(0f, (now - barStarted) / 300f));
            displayedBar = barFrom + (barTarget - barFrom) * fraction;
        }

        private void FocusTick(object sender, EventArgs e)
        {
            try { ForceFocus(); }
            catch (Exception ex) { Diag.Log("AttackForm.FocusTick", ex); Fail(ex); }
        }

        private void ForceFocus()
        {
            if (stopping || IsDisposed || !Visible) return;
            TopMost = true;
            BringToFront();
            Native.SetForegroundWindow(Handle);
            Activate();
            if (stage == Stage.Asking && answer.Enabled) answer.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                DrawCover(g, GetImage("fon.png"));
                long now = clock.ElapsedMilliseconds;
                DrawProgress(g, now);
                RectangleF card = R(686, 216, 1170, 740);
                using (GraphicsPath path = Rounded(card, 30 * scale))
                using (SolidBrush brush = new SolidBrush(Cream)) g.FillPath(brush, path);

                RectangleF field = R(791, AnswerRowY, 620, 112);
                using (GraphicsPath path = Rounded(field, 22 * scale))
                using (SolidBrush brush = new SolidBrush(Color.White))
                using (Pen border = new Pen(answer.Focused && answer.Enabled ? Mint : Ink, Math.Max(2, 3 * scale)))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(border, path);
                }

                DrawExercise(g, now);
                Color resultColor = wrongAnswer ? Color.FromArgb(122, 54, 128) : Color.FromArgb(24, 103, 86);
                bool longCard = exercise != null && exercise.LongText;
                DrawText(g, resultText, 32, FontStyle.Bold, resultColor,
                    R(766, longCard ? 800 : 712, 1010, 64), true);
                DrawText(g, "Введи ответ → Enter", 28, FontStyle.Regular, Ink,
                    R(766, longCard ? 884 : 808, 1010, longCard ? 48 : 64), true);
                DrawWave(g, now);
                DrawVirus(g, now);
                DrawCombat(g, now);
                DrawSpeech(g, now);
                DrawIdleCountdown(g);
                base.OnPaint(e);
            }
            catch (Exception ex)
            {
                Diag.Log("AttackForm.OnPaint", ex);
                // Do not destroy a native window while its WM_PAINT is being dispatched.
                if (!stopping)
                {
                    StopTimers();
                    BeginInvoke(new Action(delegate { Fail(ex); }));
                }
            }
        }

        private void DrawProgress(Graphics g, long now)
        {
            DrawText(g, "БублиГлюк", 36, FontStyle.Bold, Cream, R(148, 32, 700, 52), false);
            int percent = (int)Math.Round(displayedBar, MidpointRounding.AwayFromZero);
            DrawTextAligned(g, "Заражение: " + percent.ToString() + "%", 30, FontStyle.Bold, Lime,
                R(900, 32, 956, 52), StringAlignment.Far);
            DrawAsset(g, "shield.png", R(64, 96, 64, 64), Mint);
            RectangleF track = R(148, 112, 1708, 34);
            using (GraphicsPath path = Rounded(track, track.Height / 2))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(48, 53, 80)))
                g.FillPath(brush, path);
            if (displayedBar <= 0) return;
            RectangleF fill = track;
            fill.Width *= displayedBar / 100f;
            using (GraphicsPath path = Rounded(fill, track.Height / 2))
            using (SolidBrush brush = new SolidBrush(Lime))
            {
                g.FillPath(brush, path);
                if (stage == Stage.Hit && now >= barStarted)
                {
                    float flash = 1 - Unit((now - barStarted) / 200f);
                    if (flash > 0)
                        using (SolidBrush white = new SolidBrush(Color.FromArgb((int)(185 * flash), Color.White)))
                            g.FillPath(white, path);
                }
            }
        }

        private void DrawExercise(Graphics g, long now)
        {
            DrawText(g, "Задание " + currentTaskIndex.ToString() + " из " + taskCount.ToString(),
                24, FontStyle.Regular, Ink, R(766, 242, 1010, 42), true);
            float reveal = stage == Stage.Asking ? Unit((now - cardShownAt) / 220f) : 1;
            Color ink = Color.FromArgb((int)(255 * reveal), Ink);
            GraphicsState saved = g.Save();
            try
            {
                g.TranslateTransform(0, 12 * scale * (1 - reveal));
                if (stage == Stage.Defeated)
                {
                    DrawText(g, "Компьютер защищён!", 34, FontStyle.Bold, Ink, R(766, 300, 1010, 80), true);
                    DrawText(g, "Ты отбил атаку", 68, FontStyle.Bold, Ink, R(766, 396, 1010, 120), true, 84);
                }
                else if (exercise != null && exercise.LongText)
                {
                    DrawParagraph(g, exercise.Text, Math.Max(24, 36 * scale), ink, R(766, 300, 1010, 232));
                    DrawParagraph(g, exercise.Question, Math.Max(24, 34 * scale), ink, R(766, 548, 1010, 96));
                }
                else if (exercise != null)
                {
                    DrawText(g, exercise.Instruction, 34, FontStyle.Bold, ink, R(766, 294, 1010, 72), true);
                    // Four syllables must also fit on a 1366x768 laptop.
                    float size = exercise.Kind == "syllables" ? 56 : 78;
                    DrawText(g, exercise.Text, size, FontStyle.Bold, ink, R(766, 384, 1010, 128), true, 84);
                }
            }
            finally { g.Restore(saved); }
        }

        private static void DrawParagraph(Graphics g, string text, float pixels, Color color, RectangleF box)
        {
            // Fit the entire paragraph by words; never hide part of the question.
            using (StringFormat format = new StringFormat())
            using (SolidBrush brush = new SolidBrush(color))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.None;
                float minimum = GradedReadingBank.MinimumTextPixels();
                for (float size = pixels; ; size = Math.Max(minimum, size - 1))
                {
                    using (Font font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        SizeF measured = g.MeasureString(text, font, new SizeF(box.Width, 10000), format);
                        if ((measured.Height <= box.Height && measured.Width <= box.Width + 1) || size <= minimum)
                        {
                            g.DrawString(text, font, brush, box, format);
                            break;
                        }
                    }
                }
            }
        }

        private void DrawVirus(Graphics g, long now)
        {
            RectangleF box = R(84, 216, 520, 520);
            float centerX = box.X + box.Width / 2;
            float centerY = box.Y + box.Height / 2;
            float sx = 1;
            float sy = 1;
            float rotation = 0;
            float opacity = 1;
            float whiteFlash = 0;
            bool glitch = false;
            float idle = (float)Math.Sin(now * Math.PI * 2 / 1600.0);
            if (stage == Stage.Asking)
            {
                sx += 0.03f * idle;
                sy -= 0.03f * idle;
                centerY += 6 * scale * idle;
                if (angryStarted >= 0)
                {
                    float angry = Unit((now - angryStarted) / 500f);
                    rotation = 9 * (float)Math.Sin(Math.PI * angry);
                    centerX += shakeX;
                    centerY += shakeY;
                }
                else glitch = now < glitchUntil;
            }
            else if (stage == Stage.Hit)
            {
                long elapsed = Math.Max(0, now - stageStarted - 320);
                float recoil = Unit(elapsed / 520f);
                centerX -= 40 * scale * (float)Math.Sin(Math.PI * recoil);
                centerX += shakeX;
                centerY += shakeY;
                rotation = -7 * (float)Math.Sin(Math.PI * recoil);
                whiteFlash = now - stageStarted >= 320 ? 0.95f * (1 - Unit(elapsed / 120f)) : 0;
                glitch = now - stageStarted >= 320 && elapsed < 240;
            }
            else
            {
                float defeat = Unit((now - stageStarted) / 1200f);
                float eased = defeat * defeat * (3 - 2 * defeat);
                sx = 1 + 0.16f * eased;
                sy = 1 - 0.72f * eased;
                // Squash around the feet, with a short fall; even the final frame
                // stays above the speech bubble before becoming transparent.
                centerY += box.Height / 2 * (1 - sy) + 20 * scale * eased;
                opacity = 1 - eased;
            }

            float entrance = Unit((now - appearanceStarted) / 600f);
            if (entrance < 1)
            {
                float pop = BackOut(entrance);
                float startX = originX - 60 * scale;
                float startY = originY + 410 * scale;
                centerX = startX + (centerX - startX) * pop;
                centerY = startY + (centerY - startY) * pop;
                float grow = 0.2f + 0.8f * pop;
                sx *= grow;
                sy *= grow;
            }

            // Bound even the rotated/glitched image before drawing. Entrance may
            // start beyond the left edge, but never crosses the card or the bubble.
            double radians = rotation * Math.PI / 180;
            float cosine = (float)Math.Abs(Math.Cos(radians));
            float sine = (float)Math.Abs(Math.Sin(radians));
            float extra = glitch ? 22 * scale : 0;
            float extentX = (box.Width / 2 + extra) * sx * cosine + box.Height / 2 * sy * sine;
            float extentY = (box.Width / 2 + extra) * sx * sine + box.Height / 2 * sy * cosine;
            float fit = Math.Min(1, 600 * scale / (2 * extentY));
            sx *= fit;
            sy *= fit;
            extentX *= fit;
            extentY *= fit;
            centerX = Math.Min(centerX, originX + 670 * scale - extentX);
            centerY = Math.Max(centerY, originY + 178 * scale + extentY);
            centerY = Math.Min(centerY, originY + 778 * scale - extentY);

            GraphicsState saved = g.Save();
            try
            {
                g.TranslateTransform(centerX, centerY);
                g.RotateTransform(rotation);
                g.ScaleTransform(sx, sy);
                RectangleF local = new RectangleF(-box.Width / 2, -box.Height / 2, box.Width, box.Height);
                if (stage != Stage.Defeated)
                {
                    int pulse = (int)(36 + 20 * Math.Sin(now * Math.PI * 2 / 1600.0));
                    DrawHalo(g, local, pulse);
                }
                DrawCharacter(g, local, opacity, whiteFlash, glitch, now);
            }
            finally { g.Restore(saved); }
        }

        private void DrawHalo(Graphics g, RectangleF box, int alpha)
        {
            RectangleF halo = new RectangleF(box.X + box.Width * 0.08f, box.Y + box.Height * 0.12f,
                box.Width * 0.84f, box.Height * 0.80f);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(halo);
                using (PathGradientBrush brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(alpha, Lime);
                    brush.SurroundColors = new Color[] { Color.FromArgb(0, Lime) };
                    g.FillPath(brush, path);
                }
            }
        }

        private void DrawCharacter(Graphics g, RectangleF box, float opacity, float flash, bool glitch, long now)
        {
            Image image = GetImage(virusImage);
            if (image == null)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(255 * opacity), Purple)))
                    g.FillRectangle(brush, box);
                return;
            }
            float ratio = Math.Min(box.Width / image.Width, box.Height / image.Height);
            RectangleF dest = new RectangleF(box.X + (box.Width - image.Width * ratio) / 2,
                box.Y + (box.Height - image.Height * ratio) / 2, image.Width * ratio, image.Height * ratio);
            using (ImageAttributes normal = CharacterAttributes(opacity, flash, 0))
            {
                if (!glitch)
                {
                    g.DrawImage(image, Rectangle.Round(dest), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, normal);
                    return;
                }
                // Source strips retain the original alpha. Color echoes are also
                // alpha-masked, so there are no opaque rectangular glitch patches.
                using (ImageAttributes green = CharacterAttributes(opacity * 0.35f, 0, 1))
                using (ImageAttributes purple = CharacterAttributes(opacity * 0.30f, 0, 2))
                {
                    const int strips = 12;
                    int phase = (int)(now / 32) + glitchSeed;
                    for (int i = 0; i < strips; i++)
                    {
                        int sourceTop = image.Height * i / strips;
                        int sourceBottom = image.Height * (i + 1) / strips;
                        float offset = (8 + (phase + i * 3) % 8) * scale;
                        if ((phase + i) % 2 == 0) offset = -offset;
                        RectangleF slice = new RectangleF(dest.X + offset,
                            dest.Y + dest.Height * sourceTop / image.Height, dest.Width,
                            dest.Height * (sourceBottom - sourceTop) / image.Height);
                        RectangleF echo = slice;
                        echo.Offset(6 * scale, 0);
                        g.DrawImage(image, Rectangle.Round(echo), 0, sourceTop, image.Width,
                            sourceBottom - sourceTop, GraphicsUnit.Pixel, green);
                        echo = slice;
                        echo.Offset(-6 * scale, 0);
                        g.DrawImage(image, Rectangle.Round(echo), 0, sourceTop, image.Width,
                            sourceBottom - sourceTop, GraphicsUnit.Pixel, purple);
                        g.DrawImage(image, Rectangle.Round(slice), 0, sourceTop, image.Width,
                            sourceBottom - sourceTop, GraphicsUnit.Pixel, normal);
                    }
                }
            }
        }

        private static ImageAttributes CharacterAttributes(float opacity, float flash, int tint)
        {
            ColorMatrix matrix = new ColorMatrix();
            matrix.Matrix33 = Unit(opacity);
            matrix.Matrix00 = 1 - flash;
            matrix.Matrix11 = 1 - flash;
            matrix.Matrix22 = 1 - flash;
            matrix.Matrix40 = flash;
            matrix.Matrix41 = flash;
            matrix.Matrix42 = flash;
            if (tint != 0)
            {
                matrix.Matrix00 = 0;
                matrix.Matrix11 = 0;
                matrix.Matrix22 = 0;
                matrix.Matrix40 = tint == 1 ? 0.63f : 0.65f;
                matrix.Matrix41 = tint == 1 ? 0.94f : 0.42f;
                matrix.Matrix42 = tint == 1 ? 0 : 1;
            }
            ImageAttributes attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            return attributes;
        }

        private void DrawWave(Graphics g, long now)
        {
            if (stage != Stage.Hit) return;
            float t = Unit((now - stageStarted) / 600f);
            if (t >= 1) return;
            float x = 686 - 360 * t;
            int alpha = (int)(210 * Math.Sin(Math.PI * t));
            GraphicsState saved = g.Save();
            try
            {
                g.SetClip(R(0, 168, 686, 620), CombineMode.Intersect);
                using (Pen pen = new Pen(Color.FromArgb(alpha, Mint), Math.Max(2, 7 * scale)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    // Left edge of each arc moves from the card towards the virus.
                    g.DrawArc(pen, R(x, 320, 220, 340), 100, 160);
                    using (Pen echo = new Pen(Color.FromArgb(alpha / 2, Mint), Math.Max(1, 3 * scale)))
                        g.DrawArc(echo, R(x + 38, 348, 184, 284), 100, 160);
                }
            }
            finally { g.Restore(saved); }
        }

        private void SpawnParticles(long now, int count, bool victory)
        {
            for (int i = 0; i < count; i++)
            {
                BattleParticle p = new BattleParticle();
                p.X = 344; p.Y = victory ? 570 : 476;
                p.VX = random.Next(-170, 171);
                p.VY = random.Next(-180, 31);
                p.Born = now;
                p.Life = victory ? 1100 : 700;
                p.Size = random.Next(4, 10);
                p.Color = i % 3 == 0 ? Yellow : i % 3 == 1 ? Mint : Purple;
                particles.Add(p);
            }
            if (particles.Count > 60) particles.RemoveRange(0, particles.Count - 60);
        }

        private void DrawCombat(Graphics g, long now)
        {
            if (stage == Stage.Hit)
            {
                long elapsed = now - stageStarted;
                float t = Unit(elapsed / 320f);
                float x = 686 + (344 - 686) * t;
                float y = 450 + (476 - 450) * t - 85 * (float)Math.Sin(Math.PI * t);
                if (elapsed < 350)
                {
                    int rays = correctStreak >= 2 ? 2 : 1;
                    for (int ray = 0; ray < rays; ray++)
                    {
                        float offset = ray == 0 ? 0 : 25;
                        float previous = Math.Max(0, t - 0.18f);
                        float tailX = 686 + (344 - 686) * previous;
                        float tailY = 450 + (476 - 450) * previous - 85 * (float)Math.Sin(Math.PI * previous);
                        using (Pen tail = new Pen(Color.FromArgb(170, Mint), Math.Max(2, 5 * scale)))
                            g.DrawLine(tail, R(tailX, tailY + offset, 0, 0).Location, R(x, y + offset, 0, 0).Location);
                        using (SolidBrush glow = new SolidBrush(Color.FromArgb(65, Mint)))
                            g.FillEllipse(glow, R(x - 22, y - 22 + offset, 44, 44));
                        using (SolidBrush bolt = new SolidBrush(Mint))
                            g.FillEllipse(bolt, R(x - 8, y - 8 + offset, 16, 16));
                    }
                }
                if (completed == taskCount && elapsed >= 320)
                {
                    float strength = 1 - Unit((elapsed - 320) / 680f);
                    using (Pen beam = new Pen(Color.FromArgb((int)(190 * strength), Mint), Math.Max(3, 12 * scale)))
                        g.DrawBezier(beam, R(686, 450, 0, 0).Location, R(570, 340, 0, 0).Location,
                            R(430, 360, 0, 0).Location, R(344, 476, 0, 0).Location);
                    float radius = 120 + 100 * (1 - strength);
                    using (Pen ring = new Pen(Color.FromArgb((int)(210 * strength), Mint), Math.Max(2, 5 * scale)))
                        g.DrawEllipse(ring, R(344 - radius, 476 - radius, radius * 2, radius * 2));
                }
            }
            if (stage == Stage.Asking && angryStarted >= 0)
            {
                float t = Unit((now - angryStarted) / 350f);
                if (t < 1)
                {
                    int alpha = (int)(210 * Math.Sin(Math.PI * t));
                    float reach = 440 + 246 * Math.Min(1, t * 2);
                    float jitter = (float)Math.Sin(now / 15.0) * 4;
                    using (Pen cable = new Pen(Color.FromArgb(alpha, Purple), Math.Max(2, 9 * scale)))
                        g.DrawBezier(cable, R(440, 430, 0, 0).Location, R(520, 390 + jitter, 0, 0).Location,
                            R(590, 490 - jitter, 0, 0).Location, R(reach, 450, 0, 0).Location);
                    using (Pen shield = new Pen(Color.FromArgb(alpha, Mint), Math.Max(2, 5 * scale)))
                    using (GraphicsPath frame = Rounded(R(686, 216, 1170, 740), 30 * scale))
                        g.DrawPath(shield, frame);
                    DrawAsset(g, "shield.png", R(650, 414, 72, 72), Mint);
                }
            }
            foreach (BattleParticle p in particles)
            {
                float elapsed = (now - p.Born) / 1000f;
                float alpha = 1 - Unit((now - p.Born) / (float)p.Life);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(255 * alpha), p.Color)))
                    g.FillEllipse(brush, R(p.X + p.VX * elapsed, p.Y + p.VY * elapsed + 90 * elapsed * elapsed,
                        p.Size, p.Size));
            }
        }

        private sealed class BattleParticle
        {
            internal float X, Y, VX, VY, Size;
            internal long Born, Life;
            internal Color Color;
        }

        private void DrawSpeech(Graphics g, long now)
        {
            RectangleF bubble = R(64, 816, 560, 140);
            float pop = 0.8f + 0.2f * BackOut(Unit((now - phraseStarted) / 200f));
            float cx = bubble.X + bubble.Width / 2;
            float cy = bubble.Y + bubble.Height / 2;
            GraphicsState saved = g.Save();
            try
            {
                g.TranslateTransform(cx, cy);
                g.ScaleTransform(pop, pop);
                g.TranslateTransform(-cx, -cy);
                using (GraphicsPath path = SpeechPath(bubble))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(35, 40, 68)))
                using (Pen border = new Pen(Purple, Math.Max(1.5f, 2 * scale)))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }
                DrawText(g, phrase, 30, FontStyle.Bold, Cream, R(88, 834, 512, 104), true);
            }
            finally { g.Restore(saved); }
        }

        private GraphicsPath SpeechPath(RectangleF box)
        {
            float diameter = 36 * scale;
            float middle = box.X + box.Width / 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddLine(box.Left + diameter / 2, box.Top, middle - 20 * scale, box.Top);
            path.AddLine(middle - 20 * scale, box.Top, middle, box.Top - 20 * scale);
            path.AddLine(middle, box.Top - 20 * scale, middle + 20 * scale, box.Top);
            path.AddLine(middle + 20 * scale, box.Top, box.Right - diameter / 2, box.Top);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static float Unit(float value) { return Math.Max(0, Math.Min(1, value)); }

        private static float BackOut(float t)
        {
            float p = Unit(t) - 1;
            return 1 + 2.70158f * p * p * p + 1.70158f * p * p;
        }

        private RectangleF R(float x, float y, float width, float height)
        {
            return new RectangleF(originX + x * scale, originY + y * scale, width * scale, height * scale);
        }

        private void DrawText(Graphics g, string text, float size, FontStyle style, Color color, RectangleF bounds, bool centered)
        {
            DrawText(g, text, size, style, color, bounds, centered, 0);
        }

        private void DrawText(Graphics g, string text, float size, FontStyle style, Color color,
            RectangleF bounds, bool centered, float maxSize)
        {
            float pixels = Math.Max(16, size * scale);
            if (maxSize > 0) pixels = Math.Min(pixels, maxSize);
            using (Font font = new Font("Segoe UI", pixels, style, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = centered ? StringAlignment.Center : StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.Word;
                g.DrawString(text, font, brush, bounds, format);
            }
        }

        private void DrawTextAligned(Graphics g, string text, float size, FontStyle style,
            Color color, RectangleF bounds, StringAlignment alignment)
        {
            using (Font font = new Font("Segoe UI", Math.Max(16, size * scale), style, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = alignment;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.Word;
                g.DrawString(text, font, brush, bounds, format);
            }
        }

        private void DrawCover(Graphics g, Image image)
        {
            // FillRectangle respects the invalid region, unlike Graphics.Clear.
            using (SolidBrush background = new SolidBrush(Navy)) g.FillRectangle(background, ClientRectangle);
            if (image == null) return; // The navy screen is the background placeholder.
            float ratio = Math.Max(ClientSize.Width / (float)image.Width, ClientSize.Height / (float)image.Height);
            float w = image.Width * ratio;
            float h = image.Height * ratio;
            g.DrawImage(image, (ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
        }

        private void DrawAsset(Graphics g, string name, RectangleF box, Color placeholder)
        {
            Image image = GetImage(name);
            if (image == null)
            {
                using (SolidBrush brush = new SolidBrush(placeholder)) g.FillRectangle(brush, box);
                return;
            }
            float ratio = Math.Min(box.Width / image.Width, box.Height / image.Height);
            float w = image.Width * ratio;
            float h = image.Height * ratio;
            g.DrawImage(image, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }

        private Image GetImage(string name)
        {
            Image image;
            return images.TryGetValue(name, out image) ? image : null;
        }

        private static Image LoadImage(string name)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    using (Image source = Image.FromStream(stream)) return new Bitmap(source);
                }
            }
            catch (Exception ex) { Diag.Log("AttackForm.LoadImage " + name, ex); return null; }
        }

        internal static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void StopTimers()
        {
            if (animationTimer != null) animationTimer.Stop();
            if (focusTimer != null) focusTimer.Stop();
        }

        private void Fail(Exception ex)
        {
            if (stopping) return;
            failure(ex);
        }

        internal void Abort()
        {
            stopping = true;
            StopTimers();
            try { Close(); }
            finally { Dispose(); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            stopping = true;
            StopTimers();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stopping = true;
                StopTimers();
                if (animationTimer != null) animationTimer.Dispose();
                if (focusTimer != null) focusTimer.Dispose();
                foreach (Image image in images.Values) if (image != null) image.Dispose();
                images.Clear();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                if (inputFont != null) { inputFont.Dispose(); inputFont = null; }
                if (buttonFont != null) { buttonFont.Dispose(); buttonFont = null; }
                if (idleFont != null) { idleFont.Dispose(); idleFont = null; }
            }
        }
    }

    internal sealed class ActionButton : Button
    {
        internal ActionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.FromArgb(0xFF, 0xF8, 0xE8);
            ForeColor = Color.FromArgb(0x17, 0x1B, 0x35);
            UseVisualStyleBackColor = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rect = new RectangleF(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
            using (GraphicsPath path = AttackForm.Rounded(rect, Math.Max(6, Height / 5f)))
            using (SolidBrush brush = new SolidBrush(Enabled ? Color.FromArgb(0x65, 0xE8, 0xCD) : Color.FromArgb(189, 209, 200)))
                e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && Enabled) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -6, -6));
        }
    }
}
