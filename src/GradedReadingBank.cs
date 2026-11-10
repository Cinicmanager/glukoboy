using System;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Bubliklyuk
{
    internal sealed class GradedReadingBank
    {
        private readonly List<GradedCard>[,] cards = new List<GradedCard>[3, 3];
        internal string Warning = "";
        // Shared scene geometry for validation and drawing.
        internal const float TextWidth = 1010;
        internal const float PictureSize = 210;
        internal const float PictureGap = 28;

        private GradedReadingBank()
        {
            for (int grade = 0; grade < 3; grade++)
                for (int level = 0; level < 3; level++)
                    cards[grade, level] = new List<GradedCard>();
        }
        internal List<GradedCard> Get(int grade, int level)
        {
            if (grade < 1 || grade > 3 || level < 1 || level > 3)
                throw new ArgumentOutOfRangeException("grade");
            return new List<GradedCard>(cards[grade - 1, level - 1]);
        }

        private const int BankVersion = 2;

        internal static GradedReadingBank Load()
        {
            string defaultText = VersionedText(DefaultReadingData.Text);
            GradedReadingBank builtIn = Parse(defaultText);
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tasks.txt");
            GradedReadingBank external;
            try
            {
                string text;
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, defaultText, new UTF8Encoding(false));
                    text = defaultText;
                }
                else
                {
                    text = File.ReadAllText(path, new UTF8Encoding(false, true));
                    if (!CurrentBank(text))
                    {
                        string oldName = "tasks.old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss",
                            CultureInfo.InvariantCulture) + ".txt";
                        string oldPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, oldName);
                        File.Move(path, oldPath);
                        File.WriteAllText(path, defaultText, new UTF8Encoding(false));
                        Diag.Info("tasks: bank updated, old saved as " + oldName);
                        text = defaultText;
                    }
                }
                external = Parse(text);
            }
            catch
            {
                builtIn.Warning = "tasks.txt недоступен: используется полный встроенный банк.";
                return builtIn;
            }
            for (int grade = 0; grade < 3; grade++)
                for (int level = 0; level < 3; level++)
                    if (external.cards[grade, level].Count == 0)
                        external.cards[grade, level].AddRange(builtIn.cards[grade, level]);
            return external;
        }

        private static bool CurrentBank(string text)
        {
            using (StringReader reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim().TrimStart('\uFEFF');
                    int version;
                    if (line.StartsWith("# bank=", StringComparison.Ordinal) &&
                        Int32.TryParse(line.Substring(7), NumberStyles.None,
                            CultureInfo.InvariantCulture, out version) && version >= BankVersion)
                        return true;
                }
            }
            return false;
        }

        // DefaultReadingData is supplied separately. Ensure its exported bank has
        // exactly one version service line, immediately after the header.
        private static string VersionedText(string text)
        {
            StringBuilder result = new StringBuilder();
            using (StringReader reader = new StringReader(text))
            {
                string header = reader.ReadLine();
                if (header != null) result.AppendLine(header.TrimStart('\uFEFF'));
                result.AppendLine("# bank=" + BankVersion.ToString(CultureInfo.InvariantCulture));
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.Trim().StartsWith("# bank=", StringComparison.Ordinal)) result.AppendLine(line);
                }
            }
            return result.ToString();
        }

        internal static bool ValidPicture(string picture)
        {
            if (String.IsNullOrEmpty(picture)) return true;
            if (picture.Length > 16) return false;
            foreach (char c in picture) if (c < 'a' || c > 'z') return false;
            return true;
        }

        private static GradedReadingBank Parse(string text)
        {
            GradedReadingBank bank = new GradedReadingBank();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            int errors = 0;
            using (StringReader reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] fields = line.Split(';');
                    if (fields.Length != 6 && fields.Length != 7 && fields.Length != 8) { errors++; continue; }
                    for (int i = 0; i < fields.Length; i++) fields[i] = fields[i].Trim();
                    bool legacy = fields.Length == 6;
                    int grade = 1;
                    int level;
                    if ((!legacy && !Int32.TryParse(fields[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out grade)) ||
                        !Int32.TryParse(fields[legacy ? 1 : 2], NumberStyles.None,
                            CultureInfo.InvariantCulture, out level) ||
                        grade < 1 || grade > 3 || level < 1 || level > 3 ||
                        fields[0].Length == 0 || fields[0].Length > 64 || ids.Contains(fields[0]))
                    { errors++; continue; }
                    int offset = legacy ? 0 : 1;
                    string type = fields[2 + offset];
                    string body = fields[3 + offset];
                    string question = fields[4 + offset];
                    string answers = fields[5 + offset];
                    string picture = fields.Length == 8 && fields[7].Length > 0 ? fields[7] : null;
                    if (type.Length == 0 || body.Length == 0 || body.Length > 400 ||
                        question.Length == 0 || question.Length > 100 ||
                        answers.Length == 0 || answers.Length > 250 || !ValidPicture(picture) ||
                        !Fits(body, question, picture != null))
                    { errors++; continue; }
                    string[] variants = answers.Split('|');
                    bool numeric = true, words = true, valid = true;
                    foreach (string raw in variants)
                    {
                        string answer = Normalize(raw);
                        if (answer.Length == 0 || answer.Length > 16) { valid = false; break; }
                        int number;
                        bool isNumber = Int32.TryParse(answer, NumberStyles.None,
                            CultureInfo.InvariantCulture, out number) && number >= 0;
                        numeric = numeric && isNumber;
                        bool isWord = true;
                        foreach (char c in answer)
                            if (c < 'а' || c > 'я') { isWord = false; break; }
                        words = words && isWord;
                    }
                    if (!valid || (!numeric && !words)) { errors++; continue; }
                    bank.cards[grade - 1, level - 1].Add(
                        new GradedCard("reading", "Прочитай", body, question, answers, numeric, picture));
                    ids.Add(fields[0]);
                }
            }
            if (errors > 0)
                bank.Warning = "В tasks.txt пропущены строки с ошибками: " +
                    errors.ToString(CultureInfo.InvariantCulture) + ".";
            return bank;
        }

        internal static string Normalize(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
        }
        internal static float MinimumTextPixels()
        {
            float scale = Math.Min(Screen.PrimaryScreen.Bounds.Width / 1920f,
                Screen.PrimaryScreen.Bounds.Height / 1080f);
            return Math.Max(20, 22 * scale);
        }
        private static bool Fits(string body, string question, bool picture)
        {
            float scale = Math.Min(Screen.PrimaryScreen.Bounds.Width / 1920f,
                Screen.PrimaryScreen.Bounds.Height / 1080f);
            float width = (TextWidth - (picture ? PictureSize + PictureGap : 0)) * scale;
            using (Bitmap bitmap = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = new Font("Segoe UI", MinimumTextPixels(), FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                SizeF textSize = graphics.MeasureString(body, font, new SizeF(width, 10000), format);
                SizeF questionSize = graphics.MeasureString(question, font, new SizeF(width, 10000), format);
                return textSize.Height <= 232 * scale && textSize.Width <= width + 1 &&
                    questionSize.Height <= 96 * scale && questionSize.Width <= width + 1;
            }
        }
        internal static bool Matches(GradedCard card, string input)
        {
            string actual = Normalize(input);
            if (actual.Length == 0 || actual.Length > 16) return false;
            if (card.Numeric)
            {
                int number;
                if (!Int32.TryParse(actual, NumberStyles.None, CultureInfo.InvariantCulture,
                    out number) || number < 0) return false;
                foreach (string answer in card.Answers.Split('|'))
                {
                    int expected;
                    if (Int32.TryParse(Normalize(answer), NumberStyles.None,
                        CultureInfo.InvariantCulture, out expected) && expected == number) return true;
                }
                return false;
            }
            foreach (string raw in card.Answers.Split('|'))
                if (Normalize(raw) == actual) return true;
            return false;
        }
    }

    internal sealed class GradedGenerator
    {
        private readonly int grade, level, mathMax;
        private readonly GradedReadingBank reading;
        private readonly Random random = new Random();
        private readonly Queue<string> recent = new Queue<string>();
        private readonly Queue<string> mix = new Queue<string>();
        private readonly Dictionary<string, Queue<GradedCard>> decks =
            new Dictionary<string, Queue<GradedCard>>(StringComparer.Ordinal);
        private string previous = "";
        private int russianIndex;
        internal GradedGenerator(int grade, int level, int mathMax, GradedReadingBank reading)
        {
            this.grade = grade; this.level = level; this.mathMax = mathMax; this.reading = reading;
        }
        internal void BeginAttack(int count)
        {
            string[] order = count == 4
                ? new string[] { "reading", "russian", "reading", "math" }
                : new string[] { "reading", "russian", "math" };
            List<string[]> choices = new List<string[]>();
            Permutations(order, 0, choices);
            List<string[]> preferred = new List<string[]>();
            foreach (string[] candidate in choices)
                if (candidate[0] != previous) preferred.Add(candidate);
            List<string[]> pool = preferred.Count > 0 ? preferred : choices;
            string[] selected = pool[random.Next(pool.Count)];
            mix.Clear();
            foreach (string kind in selected) mix.Enqueue(kind);
        }
        private static void Permutations(string[] values, int index, List<string[]> results)
        {
            if (index == values.Length)
            {
                for (int i = 1; i < values.Length; i++) if (values[i] == values[i - 1]) return;
                results.Add((string[])values.Clone());
                return;
            }
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = index; i < values.Length; i++)
            {
                if (!seen.Add(values[i])) continue;
                string saved = values[index]; values[index] = values[i]; values[i] = saved;
                Permutations(values, index + 1, results);
                saved = values[index]; values[index] = values[i]; values[i] = saved;
            }
        }
        internal GradedCard Next()
        {
            if (mix.Count == 0) BeginAttack(3);
            string kind = mix.Dequeue();
            GradedCard card;
            if (kind == "reading") card = Draw("reading", reading.Get(grade, level));
            else if (kind == "russian")
            {
                string type;
                if ((grade == 1 || grade == 2) && level == 1) type = "syll";
                else if (grade <= 2) type = russianIndex++ % 2 == 0 ? "syll" : "odd";
                else
                {
                    string[] types = new string[] { "odd", "count", "syll" };
                    type = types[russianIndex++ % types.Length];
                }
                card = Draw(type, GradedContent.Russian(grade, level, type));
            }
            else
            {
                card = null;
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    card = GradedContent.Math(grade, level, mathMax, random);
                    if (!recent.Contains(card.Key)) break;
                }
            }
            previous = kind;
            recent.Enqueue(card.Key);
            while (recent.Count > 12) recent.Dequeue();
            return card;
        }
        private GradedCard Draw(string key, List<GradedCard> source)
        {
            Queue<GradedCard> deck;
            if (!decks.TryGetValue(key, out deck) || deck.Count == 0)
            {
                if (source.Count == 0) throw new InvalidDataException("Пустой банк " + key + ".");
                for (int i = source.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    GradedCard saved = source[i]; source[i] = source[j]; source[j] = saved;
                }
                List<GradedCard> ordered = source.FindAll(
                    delegate(GradedCard item) { return !recent.Contains(item.Key); });
                ordered.AddRange(source.FindAll(delegate(GradedCard item) { return recent.Contains(item.Key); }));
                deck = new Queue<GradedCard>(ordered);
                decks[key] = deck;
            }
            return deck.Dequeue();
        }
    }
}
