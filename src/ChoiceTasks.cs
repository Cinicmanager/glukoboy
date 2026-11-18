using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Bubliklyuk
{
    internal sealed class ChoiceQuestion
    {
        internal readonly string Id, Kind, Prompt, Correct, Hint;
        internal readonly string[] Options, Sequence;
        internal ChoiceQuestion(string id, string kind, string prompt, string correct,
            string options, string hint, string sequence = null)
        {
            Id = id; Kind = kind; Prompt = prompt; Correct = correct; Hint = hint;
            Options = options.Split(' ');
            Sequence = sequence == null ? new string[0] : sequence.Split(' ');
        }
    }

    // Exactly 60 authored cards. Pictures are embedded resources, not network downloads.
    internal static class ChoiceBank
    {
        internal static readonly List<ChoiceQuestion> All = Create();
        private static List<ChoiceQuestion> Create()
        {
            List<ChoiceQuestion> cards = new List<ChoiceQuestion>();
            // Each row has three matches for its explicit rule, then one exception.
            // Different rules and matching trios keep the same pictures useful.
            string[] food = {
                "syr sup tort zont", "sok syr luk myach", "tort sok sup vaza",
                "luk sup syr kukla", "sok tort luk pila", "syr luk tort lodka" };
            foreach (string row in food) AddOdd(cards, "Что нельзя есть или пить?", row, "Найди то, что не относится к еде и напиткам.");
            string[] animals = {
                "kot pes lisa myach", "ezh slon koza syr", "kit utka rak vaza",
                "sova koza osa tort", "zhuk kot pes zont", "lisa ezh koza kukla" };
            foreach (string row in animals) AddOdd(cards, "Что не животное?", row, "Птицы, рыбы и насекомые — тоже животные.");
            AddOdd(cards, "Что не живое?", "kot roza grib shar", "Живые существа питаются и растут.");
            AddOdd(cards, "Что не живое?", "koza luk zhuk miska", "Вещи сами не растут и не питаются.");
            AddOdd(cards, "Что не живое?", "osa ezh roza kukla", "Живое растёт. Игрушка сама не вырастет.");
            AddOdd(cards, "Что не растёт само?", "roza grib luk pila", "Растения и грибы становятся больше. Вещи делают люди.");
            AddOdd(cards, "Что не растёт само?", "kot slon koza dom", "Детёныши растут и становятся взрослыми.");
            AddOdd(cards, "Что не сделано человеком?", "dom lodka kukla luna", "Найди то, что появилось в природе.");
            AddOdd(cards, "Что не сделано человеком?", "zont pila miska grib", "Три вещи изготовили люди. А что выросло само?");
            AddOdd(cards, "Кто не живёт в воде?", "kit ryba rak lisa", "Для трёх животных вода — родной дом.");
            AddOdd(cards, "У кого нет крыльев?", "sova utka osa kot", "Крылья бывают и у птиц, и у насекомых.");
            AddOdd(cards, "У кого нет крыльев?", "zhuk sova utka rak", "У жука крылья скрыты под твёрдыми надкрыльями.");
            AddOdd(cards, "У кого нет крыльев?", "osa zhuk utka ryba", "У птиц и насекомых бывают крылья, а у рыб — плавники.");
            AddOdd(cards, "Кто не ходит на четырёх ногах?", "pes lisa koza sova", "Вспомни, сколько ног у зверей и сколько у птиц.");
            AddOdd(cards, "Кто не ходит на четырёх ногах?", "slon ezh kot osa", "У насекомых шесть ног.");
            AddOdd(cards, "Кто не покрыт шерстью?", "kot pes koza ryba", "Шерсть отличается от чешуи.");
            AddOdd(cards, "Кто не покрыт шерстью?", "lisa kot koza utka", "У птиц перья, а у этих зверей шерсть.");
            AddOdd(cards, "Что не еда и не посуда?", "sup syr miska pila", "К этой группе подходят и еда, и посуда для неё.");
            AddOdd(cards, "Что не еда и не посуда?", "sup tort miska zont", "Еда и посуда для неё подходят. Найди лишнее.");
            AddOdd(cards, "Кто не живёт на суше?", "ezh slon pes kit", "Найди животное, которое живёт в море.");
            AddDescription(cards, "Кто мяукает?", "kot", "kot pes kit zhuk", "Вспомни: мяу-мяу.");
            AddDescription(cards, "Кто лает?", "pes", "pes kot sova rak", "Вспомни: гав-гав.");
            AddDescription(cards, "У кого иглы?", "ezh", "ezh lisa koza utka", "На спине у него колючки.");
            AddDescription(cards, "У кого хобот?", "slon", "slon kot pes kit", "Он может брать хоботом еду.");
            AddDescription(cards, "Кто живёт в воде?", "ryba", "ryba kot lisa koza", "У неё плавники.");
            AddDescription(cards, "Кто крякает?", "utka", "utka sova pes zhuk", "Вспомни: кря-кря.");
            AddDescription(cards, "Кто летает ночью?", "sova", "sova rak kot slon", "У этой птицы большие глаза.");
            AddDescription(cards, "Что укроет от дождя?", "zont", "zont pila myach miska", "Его раскрывают над головой.");
            AddDescription(cards, "Что можно пить?", "sok", "sok syr tort luk", "Его наливают в стакан.");
            AddDescription(cards, "Что едят ложкой?", "sup", "sup syr luk zont", "Его наливают в тарелку.");
            AddDescription(cards, "Что пинают в игре?", "myach", "myach vaza pila syr", "Он круглый и может прыгать.");
            AddDescription(cards, "Во что ставят розы?", "vaza", "vaza tort zont kukla", "В неё наливают воду для цветов.");
            AddDescription(cards, "На чём плывут по реке?", "lodka", "lodka dom shar pila", "Это транспорт, который ходит по воде.");
            AddDescription(cards, "Чем пилят доску?", "pila", "pila zont miska kukla", "У этого инструмента зубцы.");
            AddDescription(cards, "У кого клешни?", "rak", "rak kit koza lisa", "Он живёт в воде и ходит по дну.");
            AddPattern(cards, "circle square circle square ?", "circle", "circle square triangle star");
            AddPattern(cards, "triangle circle triangle circle ?", "triangle", "triangle circle square star");
            AddPattern(cards, "star square star square ?", "star", "star square circle triangle");
            AddPattern(cards, "square triangle square triangle ?", "square", "square triangle star circle");
            AddPattern(cards, "circle circle square circle circle ?", "square", "square circle triangle star");
            AddPattern(cards, "triangle triangle star triangle triangle ?", "star", "star triangle circle square");
            AddPattern(cards, "circle square triangle circle square ?", "triangle", "triangle circle square star");
            AddPattern(cards, "star circle square star circle ?", "square", "square star circle triangle");
            AddPattern(cards, "0 1 2 ?", "3", "3 1 4 5");
            AddPattern(cards, "2 3 4 ?", "5", "5 3 6 7");
            AddPattern(cards, "5 6 7 ?", "8", "8 6 9 10");
            AddPattern(cards, "7 8 9 ?", "10", "10 7 8 6");
            AddPattern(cards, "5 4 3 ?", "2", "2 1 4 6");
            AddPattern(cards, "8 7 6 ?", "5", "5 4 7 9");
            AddPattern(cards, "10 9 8 ?", "7", "7 6 9 10");
            return cards;
        }
        private static void AddOdd(List<ChoiceQuestion> cards, string prompt, string options, string hint)
        {
            string[] tokens = options.Split(' ');
            cards.Add(new ChoiceQuestion("odd" + cards.Count, "odd", prompt, tokens[3], options, hint));
        }
        private static void AddDescription(List<ChoiceQuestion> cards, string prompt, string answer, string options, string hint)
        {
            cards.Add(new ChoiceQuestion("describe" + cards.Count, "describe", prompt, answer, options, hint));
        }
        private static void AddPattern(List<ChoiceQuestion> cards, string sequence, string answer, string options)
        {
            cards.Add(new ChoiceQuestion("pattern" + cards.Count, "pattern", "Что дальше?", answer, options,
                Char.IsDigit(answer[0]) ? "Посмотри, как меняется число: на один больше или меньше." :
                "Найди кусочек ряда, который повторяется.", sequence));
        }
    }

    internal sealed class ChoiceDeck
    {
        private readonly Random random;
        private readonly Dictionary<string, Queue<ChoiceQuestion>> decks = new Dictionary<string, Queue<ChoiceQuestion>>();
        private readonly Queue<string> recent = new Queue<string>();
        private int turn;
        internal ChoiceDeck(Random random) { this.random = random; }
        internal ChoiceQuestion Next(string kind)
        {
            if (kind == null) kind = new string[] { "odd", "describe", "odd", "pattern" }[turn++ % 4];
            Queue<ChoiceQuestion> deck;
            if (!decks.TryGetValue(kind, out deck) || deck.Count == 0)
            {
                List<ChoiceQuestion> pool = ChoiceBank.All.FindAll(delegate(ChoiceQuestion c) { return c.Kind == kind; });
                for (int i = pool.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1); ChoiceQuestion temp = pool[i]; pool[i] = pool[j]; pool[j] = temp;
                }
                List<ChoiceQuestion> ordered = pool.FindAll(delegate(ChoiceQuestion c) { return !recent.Contains(c.Id); });
                // Reintroduce recent cards oldest first, so a reshuffle cannot
                // immediately repeat a card at a deck boundary.
                foreach (string oldId in recent)
                    ordered.AddRange(pool.FindAll(delegate(ChoiceQuestion c) { return c.Id == oldId; }));
                deck = new Queue<ChoiceQuestion>(ordered); decks[kind] = deck;
            }
            ChoiceQuestion card = deck.Dequeue();
            recent.Enqueue(card.Id); while (recent.Count > 12) recent.Dequeue();
            return card;
        }
    }

    internal enum ChoiceResult { Ignored, Wrong, Replace, Correct }
    internal sealed class ChoiceRound
    {
        internal readonly ChoiceQuestion Question;
        internal readonly string[] Options;
        internal readonly int CorrectIndex;
        internal int Errors { get; private set; }
        private long availableAt;
        private bool finished;
        internal ChoiceRound(ChoiceQuestion question, Random random, int previousIndex, long now)
        {
            Question = question;
            List<string> wrong = new List<string>(question.Options);
            wrong.Remove(question.Correct);
            for (int i = wrong.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1); string temp = wrong[i]; wrong[i] = wrong[j]; wrong[j] = temp;
            }
            // On replacement the correct position always changes, not just probably.
            CorrectIndex = previousIndex >= 0 ? (previousIndex + 1 + random.Next(3)) % 4 : random.Next(4);
            Options = new string[4];
            int at = 0;
            for (int i = 0; i < 4; i++) Options[i] = i == CorrectIndex ? question.Correct : wrong[at++];
            availableAt = now + 450;
        }
        internal ChoiceResult Answer(int index, long now)
        {
            if (finished || index < 0 || index >= 4 || now < availableAt) return ChoiceResult.Ignored;
            availableAt = now + 800;
            if (index == CorrectIndex) { finished = true; return ChoiceResult.Correct; }
            Errors++;
            if (Errors >= 2) { finished = true; return ChoiceResult.Replace; }
            return ChoiceResult.Wrong;
        }
    }

    internal static class ChoiceArt
    {
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string> {
            {"kot","кот"},{"pes","пёс"},{"myach","мяч"},{"sok","сок"},{"dom","дом"},
            {"ezh","ёж"},{"zhuk","жук"},{"kit","кит"},{"syr","сыр"},{"luk","лук"},
            {"osa","оса"},{"rak","рак"},{"lisa","лиса"},{"sova","сова"},{"utka","утка"},
            {"ryba","рыба"},{"grib","гриб"},{"roza","роза"},{"luna","луна"},{"shar","шар"},
            {"sup","суп"},{"tort","торт"},{"zont","зонт"},{"slon","слон"},{"koza","коза"},
            {"kukla","кукла"},{"miska","миска"},{"vaza","ваза"},{"pila","пила"},{"lodka","лодка"},
            {"circle","круг"},{"square","квадрат"},{"triangle","треугольник"},{"star","звезда"}
        };
        internal static string Label(string token) { string text; return Labels.TryGetValue(token, out text) ? text : token; }
        internal static bool IsPicture(string token) { return Labels.ContainsKey(token) && token != "circle" && token != "square" && token != "triangle" && token != "star"; }
        internal static void DrawSymbol(Graphics g, string token, RectangleF box)
        {
            float side = Math.Min(box.Width, box.Height) * .72f;
            RectangleF r = new RectangleF(box.X + (box.Width - side) / 2, box.Y + (box.Height - side) / 2, side, side);
            // Shapes share a colour; the rule can be read without colour perception.
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(109, 69, 185)))
            {
                if (token == "circle") g.FillEllipse(brush, r);
                else if (token == "square") g.FillRectangle(brush, r);
                else if (token == "triangle") g.FillPolygon(brush, new PointF[] {
                    new PointF(r.X + r.Width / 2,r.Top), new PointF(r.Right,r.Bottom), new PointF(r.Left,r.Bottom) });
                else if (token == "star")
                {
                    PointF[] points = new PointF[10];
                    for (int i = 0; i < 10; i++)
                    {
                        double a = i * Math.PI / 5 - Math.PI / 2;
                        float radius = side * (i % 2 == 0 ? .5f : .22f);
                        points[i] = new PointF(r.X + side / 2 + radius * (float)Math.Cos(a), r.Y + side / 2 + radius * (float)Math.Sin(a));
                    }
                    g.FillPolygon(brush, points);
                }
                else
                {
                    using (Font font = new Font("Segoe UI", Math.Max(16, side * .7f), FontStyle.Bold, GraphicsUnit.Pixel))
                    using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(token, font, brush, box, format);
                }
            }
        }
    }
}
