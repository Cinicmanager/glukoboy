using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Bubliklyuk
{
    internal sealed partial class AttackForm
    {
        private readonly int animationLevel, entranceVariant, finaleVariant;
        private static int lastEntrance = -1, lastFinale = -1;
        private int hitVariant;
        private static int NextScene(ref int previous) { previous = (previous + 1) % 3; return previous; }

        private void ShowVictory(long now)
        {
            stage = Stage.Defeated;
            stageStarted = now;
            virusImage = new string[] { "attack.png", "defeated.png", "virus_think.png" }[finaleVariant];
            SetPhrase(new string[] { "Полетел за новым тапком!", "Такси для бублика!", "Вы меня не видели!" }[finaleVariant]);
            foreach (ChoiceTile button in choiceButtons) button.Visible = false;
            answer.Visible = submit.Visible = help.Visible = false;
            resultText = "Атака отбита!";
            SpawnParticles(now, 24, true);
            Invalidate();
        }

        private void ApplyComedyPose(long now, ref float x, ref float y, ref float sx, ref float sy, ref float angle, ref float opacity)
        {
            if (animationLevel == 0) return;
            float amount = animationLevel == 2 ? 1 : .8f;
            float enter = Unit((now - appearanceStarted) / 1000f);
            if (stage == Stage.Asking && enter < 1)
            {
                if (entranceVariant == 1)
                {
                    x = originX + (-220 + 564 * BackOut(enter)) * scale;
                    angle = -14 * (1 - enter);
                }
                else if (entranceVariant == 2)
                {
                    sx = .7f + .3f * enter;
                    sy = .7f + .3f * enter + .15f * (float)Math.Sin(enter * Math.PI * 4);
                    angle = 25 * (1 - enter);
                    y -= 160 * scale * (1 - enter);
                }
            }
            if (stage == Stage.Hit)
            {
                float hit = Unit((now - stageStarted - 300) / 500f);
                float pulse = (float)Math.Sin(hit * Math.PI);
                if (hitVariant == 0) { sx += .18f * pulse * amount; sy -= .2f * pulse * amount; }
                else if (hitVariant == 1) { angle += 45 * pulse * amount; sx -= .12f * pulse; }
                else { y -= 35 * scale * pulse * amount; angle += 12 * pulse; }
            }
            if (stage == Stage.Defeated)
            {
                float t = Unit((now - stageStarted) / 2200f);
                opacity = 1 - Unit((t - .65f) / .35f);
                if (finaleVariant == 0)
                {
                    sx = sy = .7f - .25f * t;
                    y -= 140 * scale * t;
                    angle = 6 * (float)Math.Sin(t * Math.PI * 4);
                }
                else if (finaleVariant == 1)
                {
                    sx = sy = .7f; x -= 600 * scale * t * t; y += 75 * scale;
                    angle = 4 * (float)Math.Sin(t * Math.PI * 10);
                }
                else { sx = sy = Math.Max(.08f, .7f * (1 - t)); y += 80 * scale * t; }
            }
        }
        private void DrawComedyProps(Graphics g, long now, float x, float y)
        {
            if (animationLevel == 0) return;
            GraphicsState saved = g.Save();
            // Keep every prop inside the character's side of the screen.
            g.SetClip(R(0, 178, 674, 602), CombineMode.Intersect);
            try
            {
                if (stage == Stage.Defeated && finaleVariant == 0)
                {
                    using (Pen stringPen = new Pen(Cream, 3 * scale))
                        g.DrawLine(stringPen, x + 20 * scale, y - 40 * scale, x + 45 * scale, y - 180 * scale);
                    using (SolidBrush balloon = new SolidBrush(Mint))
                        g.FillEllipse(balloon, x - 8 * scale, y - 278 * scale, 112 * scale, 130 * scale);
                    using (Pen outline = new Pen(Navy, 4 * scale))
                        g.DrawEllipse(outline, x - 8 * scale, y - 278 * scale, 112 * scale, 130 * scale);
                    using (SolidBrush shine = new SolidBrush(Cream))
                        g.FillEllipse(shine, x + 10 * scale, y - 254 * scale, 19 * scale, 36 * scale);
                }
                else if (stage == Stage.Defeated && finaleVariant == 1)
                {
                    using (SolidBrush cart = new SolidBrush(Mint))
                        g.FillRectangle(cart, x - 170 * scale, y + 115 * scale, 340 * scale, 45 * scale);
                    using (SolidBrush wheel = new SolidBrush(Yellow))
                    {
                        g.FillEllipse(wheel, x - 130 * scale, y + 145 * scale, 44 * scale, 44 * scale);
                        g.FillEllipse(wheel, x + 85 * scale, y + 145 * scale, 44 * scale, 44 * scale);
                    }
                }
                else if (stage == Stage.Defeated && finaleVariant == 2)
                    DrawAsset(g, "shield.png", new RectangleF(x - 100 * scale, y - 100 * scale, 200 * scale, 200 * scale), Mint);
                else if ((stage == Stage.Hit && hitVariant == 2) ||
                    (stage == Stage.Asking && entranceVariant == 1 && now - appearanceStarted < 1000))
                {
                    float t = stage == Stage.Hit ? Unit((now - stageStarted - 300) / 500f) : 0;
                    float px = x + 90 * scale * t;
                    float py = y + 180 * scale - 240 * scale * (float)Math.Sin(t * Math.PI);
                    using (SolidBrush slipper = new SolidBrush(Yellow))
                    using (Pen sole = new Pen(Ink, 4 * scale))
                    using (GraphicsPath shoe = new GraphicsPath())
                    {
                        shoe.AddBezier(px - 70 * scale, py + 20 * scale, px - 75 * scale, py - 25 * scale,
                            px + 10 * scale, py - 30 * scale, px + 20 * scale, py + 5 * scale);
                        shoe.AddLine(px + 20 * scale, py + 5 * scale, px + 68 * scale, py + 15 * scale);
                        shoe.AddBezier(px + 68 * scale, py + 15 * scale, px + 80 * scale, py + 45 * scale,
                            px - 60 * scale, py + 46 * scale, px - 70 * scale, py + 20 * scale);
                        shoe.CloseFigure(); g.FillPath(slipper, shoe); g.DrawPath(sole, shoe);
                        g.DrawLine(sole, px - 55 * scale, py + 28 * scale, px + 58 * scale, py + 28 * scale);
                    }
                }
                else if (stage == Stage.Asking && angryStarted >= 0)
                {
                    using (Pen handle = new Pen(Mint, 9 * scale))
                    {
                        g.DrawEllipse(handle, x + 90 * scale, y - 25 * scale, 85 * scale, 85 * scale);
                        g.DrawLine(handle, x + 160 * scale, y + 48 * scale, x + 200 * scale, y + 90 * scale);
                    }
                }
            }
            finally { g.Restore(saved); }
        }
    }
}
