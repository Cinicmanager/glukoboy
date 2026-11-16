using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Bubliklyuk
{
    internal sealed partial class AttackForm
    {
        private readonly ChoiceTile[] choiceButtons = new ChoiceTile[4];
        private ActionButton help;
        private ChoiceRound choiceRound;
        private int previousChoiceIndex = -1;
        private long replaceChoiceAt = -1;
        private long typedAvailableAt;
        private bool IsChoice { get { return exercise != null && exercise.Choice != null; } }

        private void CreateChoiceControls()
        {
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                ChoiceTile tile = new ChoiceTile();
                tile.TabIndex = i;
                tile.Visible = false;
                tile.Click += delegate { Choose(index); };
                choiceButtons[i] = tile;
                Controls.Add(tile);
            }
            help = new ActionButton { Text = "Помоги", TabIndex = 5, AccessibleName = "Подсказка без штрафа" };
            help.Click += delegate {
                if (stage != Stage.Asking || replaceChoiceAt >= 0) return;
                resultText = IsChoice ? exercise.Choice.Hint : exercise.Kind == "math" ?
                    "Посчитай по шагам. В окошке должно быть число." :
                    exercise.LongText ? "Прочитай вопрос и найди ответ в тексте." : "Прочитай слоги по порядку и соедини их.";
                wrongAnswer = false;
                SetPhrase("Давай подумаем вместе");
                Invalidate();
            };
            Controls.Add(help);
        }
        private void SetUpChoice()
        {
            replaceChoiceAt = -1;
            choiceRound = IsChoice ? new ChoiceRound(exercise.Choice, random, previousChoiceIndex, clock.ElapsedMilliseconds) : null;
            if (choiceRound != null) previousChoiceIndex = choiceRound.CorrectIndex;
            answer.Visible = submit.Visible = !IsChoice;
            AcceptButton = IsChoice ? null : submit;
            for (int i = 0; i < 4; i++)
            {
                ChoiceTile tile = choiceButtons[i];
                tile.Visible = IsChoice;
                tile.Enabled = true;
                if (IsChoice)
                {
                    tile.Token = choiceRound.Options[i];
                    tile.Picture = ChoiceArt.IsPicture(tile.Token) ? GetImage("pic_" + tile.Token + ".png") : null;
                    tile.AccessibleName = ChoiceArt.Label(tile.Token);
                    tile.Invalidate();
                }
            }
            help.Enabled = true;
            LayoutChoiceControls();
        }
        private void LayoutChoiceControls()
        {
            if (help == null) return;
            help.Font = buttonFont;
            help.Bounds = Rectangle.Round(R(1556, 884, 220, 50));
            bool sequence = IsChoice && exercise.Choice.Sequence.Length > 0;
            for (int i = 0; i < 4; i++)
                choiceButtons[i].Bounds = Rectangle.Round(R(744 + i * 264, sequence ? 518 : 434, 246, sequence ? 242 : 300));
        }
        private void Choose(int index)
        {
            if (stopping || stage != Stage.Asking || !IsChoice || choiceRound == null || replaceChoiceAt >= 0) return;
            try
            {
                ChoiceResult result = choiceRound.Answer(index, clock.ElapsedMilliseconds);
                if (result == ChoiceResult.Ignored) return;
                if (result == ChoiceResult.Correct)
                {
                    SubmitAnswer(exercise.Answer);
                    foreach (ChoiceTile button in choiceButtons) button.Enabled = false;
                    help.Enabled = false;
                }
                else
                {
                    correctStreak = 0;
                    angryStarted = clock.ElapsedMilliseconds;
                    virusImage = "virus_think.png";
                    wrongAnswer = true;
                    cardShakeStarted = animationLevel == 0 ? -1 : clock.ElapsedMilliseconds;
                    SetPhrase("Давай посмотрим внимательно");
                    resultText = result == ChoiceResult.Replace ? "Попробуем другую карточку" : "Подумай ещё. Можно нажать «Помоги».";
                    if (result == ChoiceResult.Replace)
                    {
                        replaceChoiceAt = clock.ElapsedMilliseconds + 1000;
                        foreach (ChoiceTile button in choiceButtons) button.Enabled = false;
                        help.Enabled = false;
                    }
                    Invalidate();
                }
            }
            catch (Exception ex) { Diag.Log("AttackForm.Choose", ex); Fail(ex); }
        }
        private void TickChoices(long now)
        {
            if (replaceChoiceAt >= 0 && now >= replaceChoiceAt)
            {
                // Do not dequeue the attack mix and do not increment completed.
                Exercise replacement = generator.NextChoice(exercise.Choice.Kind);
                NewExercise(replacement);
                SetPhrase("Новая карточка. Не спеши!");
            }
        }
        private void DrawChoiceQuestion(Graphics g)
        {
            DrawText(g, exercise.Choice.Prompt, 42, FontStyle.Bold, Ink, R(746, 300, 1050, 108), true);
            string[] sequence = exercise.Choice.Sequence;
            if (sequence.Length > 0)
            {
                float width = Math.Min(140, 1000f / sequence.Length);
                float left = 1271 - sequence.Length * width / 2;
                for (int i = 0; i < sequence.Length; i++)
                    ChoiceArt.DrawSymbol(g, sequence[i], R(left + i * width, 410, width, 92));
            }
        }
    }

    internal sealed class ChoiceTile : Button
    {
        internal string Token = "?";
        internal Image Picture;
        internal ChoiceTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.FromArgb(255, 248, 232);
            Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(BackColor);
            RectangleF box = new RectangleF(2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
            using (GraphicsPath path = AttackForm.Rounded(box, 14))
            using (SolidBrush fill = new SolidBrush(Enabled ? Color.White : Color.FromArgb(230, 232, 229)))
            using (Pen border = new Pen(Focused ? Color.FromArgb(65, 170, 150) : Color.FromArgb(115, 100, 148), Focused ? 4 : 2))
            { g.FillPath(fill, path); g.DrawPath(border, path); }
            RectangleF art = new RectangleF(10, 10, Math.Max(1, Width - 20), Math.Max(1, Height - 54));
            if (Picture != null)
            {
                float ratio = Math.Min(art.Width / Picture.Width, art.Height / Picture.Height);
                g.DrawImage(Picture, art.X + (art.Width - Picture.Width * ratio) / 2,
                    art.Y + (art.Height - Picture.Height * ratio) / 2, Picture.Width * ratio, Picture.Height * ratio);
            }
            else ChoiceArt.DrawSymbol(g, Token, art);
            string label = ChoiceArt.IsPicture(Token) ? ChoiceArt.Label(Token) : "";
            using (Font font = new Font("Segoe UI", Math.Max(17, Math.Min(30, Width / 7f)), FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(32, 36, 58)))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(label, font, brush, new RectangleF(4, Height - 44, Width - 8, 38), format);
        }
    }
}
