using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Bubliklyuk
{
    internal static class Q2ReadingChecks
    {
        internal static void Run(Action<bool, string> check, ReadingBank bank)
        {
            Counts(check);
            List<GradedCard> cards = bank.Data.Get(1, 2);
            int[] positions = new int[3];
            for (int index = 0; index < cards.Count; index++)
            {
                GradedCard card = cards[index];
                string[] sentences = card.Text.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                check(sentences.Length == 3, "Every Q2 reading card has three sentences");
                foreach (string sentence in sentences)
                    check(sentence.Trim().Split(' ').Length <= 5, "Q2 sentences stay short");
                check(card.ShuffleReading == (index < 50), "Only independent built-in facts are shuffled");
                if (index >= 50)
                {
                    check(sentences[0].Trim().StartsWith("Сначала ") && sentences[1].Trim().StartsWith("Потом ") &&
                        sentences[2].Trim().StartsWith("Затем "), "Temporal cues retain the timeline");
                    check(Object.ReferenceEquals(card, ReadingSentenceOrder.Shuffle(card, new Random(42))), "Chronology never shuffled");
                    continue;
                }
                string answer = AnswerRules.Normalize(card.Answers.Split('|')[0]);
                int hits = 0;
                for (int i = 0; i < sentences.Length; i++)
                    if (Regex.IsMatch(AnswerRules.Normalize(sentences[i]), @"\b" + Regex.Escape(answer) + @"\b"))
                    { hits++; positions[i]++; }
                check(hits == 1, "Only one sentence contains the answer");
                HashSet<string> permutations = new HashSet<string>();
                Random random = new Random(72 + index);
                string[] expected = (string[])sentences.Clone();
                for (int i = 0; i < expected.Length; i++) expected[i] = expected[i].Trim();
                Array.Sort(expected, StringComparer.Ordinal);
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    GradedCard shuffled = ReadingSentenceOrder.Shuffle(card, random);
                    permutations.Add(shuffled.Text);
                    string[] actual = shuffled.Text.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < actual.Length; i++) actual[i] = actual[i].Trim();
                    Array.Sort(actual, StringComparer.Ordinal);
                    check(String.Join("|", actual) == String.Join("|", expected), "Shuffling preserves all three facts");
                    check(shuffled.Question == card.Question && shuffled.Answers == card.Answers && shuffled.Picture == card.Picture,
                        "Shuffling preserves the question and answer");
                }
                check(permutations.Count == 6, "Every sentence order can appear");
            }
            check(positions[0] == 17 && positions[1] == 17 && positions[2] == 16, "Exported answer positions are balanced too");
            foreach (string row in ReadingCardCompatibility.PreviousSecondQuarter.Split('\n'))
            {
                if (row.Length == 0) continue;
                string[] original = row.Split(';');
                foreach (int field in new int[] { 0, 1, 2, 3, 4, 5, 6, 7 })
                {
                    string[] custom = (string[])original.Clone();
                    custom[field] += "x";
                    check(Object.ReferenceEquals(custom, ReadingCardCompatibility.Upgrade(custom)), "Custom fields prevent standard-card upgrade");
                    check(!ReadingCardCompatibility.CanShuffle(custom), "Custom text is never implicitly shuffled");
                }
            }
            GradedCard own = new GradedCard("reading", "Прочитай", "Кот ел. Потом кот пил. Затем кот спал.", "Кто спал?", "кот", false);
            check(Object.ReferenceEquals(own, ReadingSentenceOrder.Shuffle(own, new Random(1))), "Custom timeline untouched");
            foreach (int count in new int[] { 4, 5, 6 })
            {
                TaskGenerator generator = new TaskGenerator(new Settings { Grade = 1, Level = 2 }, bank);
                for (int attack = 0; attack < 40; attack++)
                {
                    generator.BeginAttack(count);
                    int reading = 0, math = 0, syllables = 0, choices = 0;
                    string previous = "";
                    for (int i = 0; i < count; i++)
                    {
                        Exercise exercise = generator.Next();
                        if (exercise.Kind == "dictation") reading++;
                        check(exercise.Kind != "reading", "Comprehension cards stay outside active attacks");
                        if (exercise.Kind == "math") math++;
                        if (exercise.Kind == "syllables") syllables++;
                        if (exercise.Choice != null) choices++;
                        string kind = exercise.Kind == "syllables" || exercise.Choice != null ? "russian" : exercise.Kind;
                        check(kind != previous, "Subjects alternate within an attack"); previous = kind;
                        check(exercise.Matches(exercise.Answer.Split('|')[0]), "Generated exercise keeps its correct answer");
                    }
                    check(reading == 2 && math == (count == 6 ? 2 : 1), "Every attack has exactly two dictations and mathematics");
                    if (count >= 5) check(syllables == 1 && choices == 1, "Longer attacks include both syllables and choices");
                }
            }
            // Exercise the full generator, not just the shuffle helper, across deck refills.
            // Preserve the archived bank's integrity without putting it into active attacks.
            GradedGenerator draw = new GradedGenerator(1, 2, 10, bank.Data);
            HashSet<string> targetOrders = new HashSet<string>();
            for (int i = 0; i < 180; i++)
            {
                draw.BeginAttack(4);
                for (int j = 0; j < 4; j++)
                {
                    GradedCard card = draw.Next();
                    if (card.Question == "Что взяла Таня?") targetOrders.Add(card.Text);
                }
            }
            check(targetOrders.Count > 1, "The same reading card can return with a different sentence order");
        }

        private sealed class BoundaryRandom : Random
        {
            internal int Min, Max;
            public override int Next(int minValue, int maxValue) { Min = minValue; Max = maxValue; return minValue; }
        }
        private static void Range(Action<bool, string> check, Settings settings, int min, int max)
        {
            BoundaryRandom random = new BoundaryRandom();
            check(settings.NextTaskCount(random) == min && random.Min == min && random.Max == max + 1, "Exact task-count bounds");
        }
        private static void Counts(Action<bool, string> check)
        {
            int[,] ranges = { { 3,4,4,6 }, { 3,3,4,4 }, { 4,4,4,4 }, { 4,5,4,6 }, { 5,5,5,5 }, { 3,5,4,6 }, { 5,4,4,6 }, { 4,3,4,6 }, { 4,6,4,6 }, { 6,6,6,6 }, { 5,6,5,6 } };
            string path = Settings.FilePath("settings.ini");
            byte[] previous = File.ReadAllBytes(path);
            try
            {
                for (int i = 0; i < ranges.GetLength(0); i++)
                {
                    Range(check, new Settings { Grade = 1, Level = 2, TaskMin = ranges[i,0], TaskMax = ranges[i,1] }, ranges[i,2], ranges[i,3]);
                    string text = "Grade=1\nLevel=2\nTaskMin=" + ranges[i,0] + "\nTaskMax=" + ranges[i,1] + "\n";
                    File.WriteAllText(path, text, new UTF8Encoding(false));
                    Range(check, Settings.Load(), ranges[i,2], ranges[i,3]);
                    check(File.ReadAllText(path) == text, "Old settings are interpreted without rewriting");
                }
                for (int grade = 1; grade <= 3; grade++) for (int level = 1; level <= 3; level++)
                {
                    Range(check, new Settings { Grade = grade, Level = level }, 4, 6);
                    File.WriteAllText(path, "Grade=" + grade + "\nLevel=" + level + "\n", new UTF8Encoding(false));
                    Settings loaded = Settings.Load();
                    Range(check, loaded, 4, 6);
                    check(loaded.Grade == 1 && loaded.Level == 2, "Old level settings cannot enable other difficulties");
                }
            }
            finally { File.WriteAllBytes(path, previous); }
        }
    }
}
