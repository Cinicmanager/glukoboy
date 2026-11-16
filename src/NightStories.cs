using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Bubliklyuk
{
    internal sealed class NightStory
    {
        internal readonly string Id, Title, Reason, Work;
        internal readonly string[] Phrases;
        internal NightStory(string id, string title, string reason, string work, params string[] phrases)
        { Id = id; Title = title; Reason = reason; Work = work; Phrases = phrases; }
    }

    internal static class NightStories
    {
        internal const string DefaultModes = "overheat,repair,cleanup,update";
        internal static readonly NightStory[] All = {
            new NightStory("overheat", "Ноутбук перегрелся", "Пока пользоваться нельзя. Нужно охладить ноутбук.", "Охлаждение работает",
                "Я включил охлаждение. Всё под контролем!", "Сам присмотрю за ноутбуком. Можно идти спать.", "Утром снова можно пользоваться."),
            new NightStory("repair", "Идёт ремонт", "В компьютере сбой. Я уже нашёл причину и чиню.", "Проверяю механизмы",
                "Сам справлюсь! Тебе помогать не нужно.", "Так, этот винтик от моего тапка...", "Утром всё будет готово."),
            new NightStory("cleanup", "Убираю глюки", "Глюки перепутали команды. Компьютер занят очисткой.", "Навожу порядок",
                "Всех поймаю! Можешь идти спать.", "Ещё один спрятался под пробелом!", "Я всё сделаю сам. До утра!"),
            new NightStory("update", "Большое обновление", "Устанавливаю обновление. Пока пользоваться нельзя.", "Настраиваю программы",
                "Закончу и всё проверю. Сам справлюсь!", "Для тапка тоже нашлось обновление.", "Утром снова можно пользоваться.")
        };
        internal static string Normalize(string value)
        {
            List<string> selected = new List<string>();
            foreach (string token in (value ?? "").Split(','))
                foreach (NightStory story in All)
                    if (token.Trim().Equals(story.Id, StringComparison.OrdinalIgnoreCase) && !selected.Contains(story.Id))
                        selected.Add(story.Id);
            return selected.Count == 0 ? DefaultModes : String.Join(",", selected.ToArray());
        }
        internal static NightStory Select(string modes, DateTime start)
        {
            string[] selected = Normalize(modes).Split(',');
            // Use the START of the night: midnight and restarts cannot change the story.
            // A date-based rotation also avoids repeating consecutive nights.
            int day = (int)(start.Date - new DateTime(2000, 1, 1)).TotalDays;
            int index = ((day % selected.Length) + selected.Length) % selected.Length;
            foreach (NightStory story in All) if (story.Id == selected[index]) return story;
            return All[0];
        }
    }

    internal sealed partial class NightForm
    {
        private readonly NightStory story;
        private readonly int animationLevel;

        private void DrawMaintenance(Graphics g)
        {
            long time = animationLevel == 0 ? 0 : clock.ElapsedMilliseconds;
            float progress = (float)(time % 8000) / 8000;
            RectangleF zone = R(810, 710, 900, 190);
            if (story.Id == "repair")
            {
                // A slowly turning gear and a spanner: unmistakably a workshop.
                GraphicsState state = g.Save();
                g.TranslateTransform(zone.X + 110 * scale, zone.Y + 88 * scale);
                g.RotateTransform(progress * 90);
                using (Pen pen = new Pen(Lime, 16 * scale))
                {
                    g.DrawEllipse(pen, -45 * scale, -45 * scale, 90 * scale, 90 * scale);
                    for (int i = 0; i < 8; i++)
                    { g.DrawLine(pen, 0, -50 * scale, 0, -68 * scale); g.RotateTransform(45); }
                }
                g.Restore(state);
                using (Pen tool = new Pen(Cream, 16 * scale))
                {
                    tool.StartCap = LineCap.Round; tool.EndCap = LineCap.Round;
                    float dy = 8 * scale * (float)Math.Sin(progress * Math.PI * 2);
                    g.DrawLine(tool, zone.Right - 120 * scale, zone.Top + 140 * scale + dy,
                        zone.Right - 60 * scale, zone.Top + 70 * scale + dy);
                    g.DrawArc(tool, zone.Right - 86 * scale, zone.Top + 18 * scale + dy, 64 * scale, 64 * scale, 20, 270);
                }
            }
            else if (story.Id == "cleanup")
            {
                using (Pen net = new Pen(Cream, 5 * scale))
                using (SolidBrush bug = new SolidBrush(Lime))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float x = zone.Left + (40 + i * 46) * scale;
                        float y = zone.Top + (60 + 12 * (float)Math.Sin(progress * Math.PI * 2 + i)) * scale;
                        g.FillEllipse(bug, x, y, 30 * scale, 30 * scale);
                        g.DrawLine(net, x, y - 8 * scale, x + 6 * scale, y);
                        g.DrawLine(net, x + 24 * scale, y, x + 30 * scale, y - 8 * scale);
                    }
                    float swing = 10 * scale * (float)Math.Sin(progress * Math.PI * 2);
                    g.DrawEllipse(net, zone.Right - 140 * scale + swing, zone.Top + 10 * scale, 100 * scale, 100 * scale);
                    g.DrawLine(net, zone.Right - 90 * scale + swing, zone.Top + 110 * scale, zone.Right - 140 * scale, zone.Bottom);
                }
            }
            else
            {
                using (SolidBrush block = new SolidBrush(Lime))
                using (Pen outline = new Pen(Cream, 4 * scale))
                    for (int i = 0; i < 3; i++)
                    {
                        RectangleF box = R(840 + i * 60, 760 - i * 18, 42, 42);
                        g.DrawRectangle(outline, box.X, box.Y, box.Width, box.Height);
                        if ((time / 1600) % 3 == i || animationLevel == 0) g.FillRectangle(block, box);
                    }
                using (Pen arrow = new Pen(Lime, 10 * scale))
                {
                    float x = zone.Right - 90 * scale, y = zone.Top + 80 * scale;
                    g.DrawArc(arrow, x - 50 * scale, y - 50 * scale, 100 * scale, 100 * scale, progress * 360, 290);
                }
            }
            TextAt(g, story.Work, 28, Cream, R(1040, 745, 440, 80));
        }
    }
}
