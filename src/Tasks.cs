using System;
using System.Collections.Generic;

namespace Bubliklyuk
{
    internal sealed class Exercise
    {
        internal readonly string Kind, Instruction, Text, Answer, Question, Key, Picture, DictationResource;
        internal readonly bool Numeric;
        internal readonly ChoiceQuestion Choice;
        internal bool LongText { get { return Question.Length > 0; } }

        internal Exercise(string kind, string instruction, string text, string answer, bool numeric,
            string question, string picture = null, ChoiceQuestion choice = null, string dictationResource = null)
        {
            Kind = kind; Instruction = instruction; Text = text; Answer = answer;
            Question = question ?? ""; Numeric = numeric;
            Picture = String.IsNullOrEmpty(picture) ? null : picture;
            Choice = choice;
            DictationResource = dictationResource;
            Key = kind + "|" + (dictationResource ?? text) + "|" + Question;
        }

        internal bool Matches(string input)
        {
            return Kind == "dictation" ? DictationBank.Matches(Answer, input) :
                AnswerRules.Matches(Answer, Numeric, input);
        }
        internal static string Normalize(string text)
        {
            return AnswerRules.Normalize(text);
        }
    }

    internal sealed class TaskGenerator
    {
        private readonly Random random;
        private readonly ChoiceDeck choices;
        private readonly DictationDeck dictations;
        private readonly int mathMax;
        private readonly Queue<string> mix = new Queue<string>();
        private readonly Queue<string> recent = new Queue<string>();
        private Queue<GradedCard> syllables = new Queue<GradedCard>();
        private string previous = "";
        private int russianIndex;

        internal TaskGenerator(Settings settings) : this(settings, new Random()) { }
        // Kept for callers that still inspect the archived reading bank. The active
        // difficulty and exercises do not depend on a bank or old Grade/Level values.
        internal TaskGenerator(Settings settings, ReadingBank bank) : this(settings) { }
        internal TaskGenerator(Settings settings, Random random)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (random == null) throw new ArgumentNullException("random");
            this.random = random;
            choices = new ChoiceDeck(random);
            dictations = new DictationDeck(random);
            mathMax = Math.Max(3, Math.Min(10, settings.MathMax));
        }
        internal void BeginAttack(int count)
        {
            if (count < 4 || count > 6) throw new ArgumentOutOfRangeException("count");
            string[] order = count == 6
                ? new string[] { "dictation", "russian", "math", "dictation", "russian", "math" }
                : count == 5
                ? new string[] { "dictation", "russian", "dictation", "russian", "math" }
                : new string[] { "dictation", "russian", "dictation", "math" };
            List<string[]> permutations = new List<string[]>();
            Permutations(order, 0, permutations);
            List<string[]> preferred = permutations.FindAll(
                delegate(string[] candidate) { return candidate[0] != previous; });
            List<string[]> pool = preferred.Count > 0 ? preferred : permutations;
            string[] selected = pool[random.Next(pool.Count)];
            mix.Clear();
            foreach (string kind in selected) mix.Enqueue(kind);
            dictations.BeginAttack();
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
        internal Exercise Next()
        {
            if (mix.Count == 0) BeginAttack(4);
            string subject = mix.Dequeue();
            previous = subject;
            if (subject == "dictation") return dictations.Next().ToExercise();
            if (subject == "russian" && russianIndex++ % 2 != 0) return NextChoice(null);
            GradedCard card;
            if (subject == "russian") card = NextSyllables();
            else
            {
                card = null;
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    card = GradedContent.Math(1, 2, mathMax, random);
                    if (!recent.Contains(card.Key)) break;
                }
            }
            recent.Enqueue(card.Key);
            while (recent.Count > 12) recent.Dequeue();
            string kind = card.Type == "syll" ? "syllables" : card.Type;
            return new Exercise(kind, card.Instruction, card.Text, card.Answers, card.Numeric, card.Question, card.Picture);
        }
        private GradedCard NextSyllables()
        {
            if (syllables.Count == 0)
            {
                List<GradedCard> cards = GradedContent.Russian(1, 2, "syll");
                for (int i = cards.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    GradedCard saved = cards[i]; cards[i] = cards[j]; cards[j] = saved;
                }
                List<GradedCard> ordered = cards.FindAll(
                    delegate(GradedCard item) { return !recent.Contains(item.Key); });
                ordered.AddRange(cards.FindAll(delegate(GradedCard item) { return recent.Contains(item.Key); }));
                syllables = new Queue<GradedCard>(ordered);
            }
            return syllables.Dequeue();
        }
        internal Exercise NextChoice(string kind)
        {
            ChoiceQuestion card = choices.Next(kind);
            return new Exercise("choice", "Выбери ответ", card.Prompt, card.Correct, false, "", null, card);
        }
    }
}
