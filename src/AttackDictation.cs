using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Bubliklyuk
{
    internal sealed partial class AttackForm
    {
        private const int DictationAnswerMaxLength = 64;
        private ActionButton repeatDictation;
        private DictationPlayer dictationPlayer;
        private bool dictationPlaybackEnabled;
        private bool dictationIntroPlayed;

        private bool IsDictation { get { return exercise != null && exercise.Kind == "dictation"; } }

        private void CreateDictationControls()
        {
            repeatDictation = new ActionButton();
            repeatDictation.Text = "Повторить";
            repeatDictation.AccessibleName = "Повторить слово";
            repeatDictation.TabIndex = 2;
            repeatDictation.Visible = false;
            repeatDictation.Click += RepeatDictationClicked;
            Controls.Add(repeatDictation);
        }

        private void SetUpDictation()
        {
            answer.MaxLength = IsDictation ? DictationAnswerMaxLength : AnswerRules.MaxLength;
            answer.AccessibleName = IsDictation ? "Введи услышанное слово" : "Ответ";
            repeatDictation.Visible = IsDictation;
            repeatDictation.Enabled = IsDictation;
            help.Visible = !IsDictation;
        }

        private RectangleF AnswerFieldBounds()
        {
            return IsDictation ? R(766, AnswerRowY, 1010, 112) : R(791, AnswerRowY, 620, 112);
        }

        private float DictationInputSize()
        {
            if (!IsDictation) return Math.Max(24, 48 * scale);
            float size = Math.Max(24, 40 * scale);
            float minimum = Math.Max(18, 26 * scale);
            int availableWidth = Math.Max(1, (int)Math.Floor(AnswerFieldBounds().Width - 48 * scale));
            // Keep the whole word visible as it is entered, including a final period.
            for (; ; size = Math.Max(minimum, size - 1))
            {
                using (Font candidate = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    Size measured = TextRenderer.MeasureText(answer.Text, candidate, Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    if (measured.Width <= availableWidth || size <= minimum) return size;
                }
            }
        }

        private void AnswerTextChanged(object sender, EventArgs e)
        {
            if (stopping || !IsDictation) return;
            try { LayoutScene(); }
            catch (Exception ex) { Diag.Log("AttackForm.AnswerTextChanged", ex); Fail(ex); }
        }

        private void LayoutDictationControls()
        {
            if (repeatDictation == null) return;
            repeatDictation.Font = buttonFont;
            repeatDictation.Bounds = Rectangle.Round(R(791, 744, 400, 100));
        }

        private void StartDictation()
        {
            // Render and regression tests call NewExercise on hidden forms. Only OnShown
            // may enable audio; creating controls, laying them out and painting stay silent.
            if (!dictationPlaybackEnabled || stopping || !IsDictation || stage != Stage.Asking) return;
            if (dictationPlayer == null) dictationPlayer = new DictationPlayer(DictationFailed);
            dictationPlayer.Start(exercise.DictationResource, !dictationIntroPlayed);
            dictationIntroPlayed = true;
        }

        private void RepeatDictationClicked(object sender, EventArgs e)
        {
            if (!dictationPlaybackEnabled || stopping || !IsDictation || stage != Stage.Asking || dictationPlayer == null) return;
            try
            {
                dictationPlayer.Repeat();
                answer.Focus();
            }
            catch (Exception ex) { Diag.Log("AttackForm.RepeatDictation", ex); Fail(ex); }
        }

        private void StopDictation()
        {
            if (repeatDictation != null)
            {
                repeatDictation.Enabled = false;
                repeatDictation.Visible = false;
            }
            if (dictationPlayer != null) dictationPlayer.Stop();
        }

        private void DisposeDictation()
        {
            StopDictation();
            if (dictationPlayer == null) return;
            dictationPlayer.Dispose();
            dictationPlayer = null;
        }

        private void DictationFailed(Exception ex)
        {
            // The audio worker must not touch Windows Forms controls directly.
            if (stopping || IsDisposed || Disposing || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(delegate {
                    if (stopping || IsDisposed || Disposing || !dictationPlaybackEnabled) return;
                    Diag.Log("AttackForm.Dictation", ex);
                    Fail(ex);
                }));
            }
            catch (InvalidOperationException) { /* The form closed while the callback was queued. */ }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            dictationPlaybackEnabled = false;
            StopDictation();
            base.OnFormClosing(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (!Visible)
            {
                dictationPlaybackEnabled = false;
                StopDictation();
            }
            base.OnVisibleChanged(e);
        }

        private void DrawDictationQuestion(Graphics g, long now, Color ink)
        {
            DrawText(g, "Послушай и введи слово", 34, FontStyle.Bold, ink,
                R(766, 294, 1010, 72), true);
            using (SolidBrush circle = new SolidBrush(Color.FromArgb(ink.A, 228, 238, 226)))
                g.FillEllipse(circle, R(1199, 384, 144, 144));
            PointF[] speaker = new PointF[] {
                R(1230, 440, 0, 0).Location, R(1248, 440, 0, 0).Location,
                R(1270, 422, 0, 0).Location, R(1270, 490, 0, 0).Location,
                R(1248, 472, 0, 0).Location, R(1230, 472, 0, 0).Location
            };
            using (SolidBrush body = new SolidBrush(ink)) g.FillPolygon(body, speaker);
            float pulse = animationLevel == 0 || stage != Stage.Asking ? 1 :
                0.65f + 0.35f * (float)((Math.Sin(now / 350.0) + 1) / 2);
            using (Pen sound = new Pen(Color.FromArgb((int)(ink.A * pulse), Ink), Math.Max(2, 5 * scale)))
            {
                sound.StartCap = sound.EndCap = LineCap.Round;
                g.DrawArc(sound, R(1260, 434, 40, 44), -60, 120);
                g.DrawArc(sound, R(1258, 420, 64, 72), -60, 120);
            }
        }
    }
}
