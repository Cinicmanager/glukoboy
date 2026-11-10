using System;
using System.Globalization;

namespace Bubliklyuk
{
    internal sealed class Exercise
    {
        internal readonly string Kind, Instruction, Text, Answer, Question, Key, Picture;
        internal readonly bool Numeric;
        internal bool LongText { get { return Question.Length > 0; } }

        internal Exercise(string kind, string instruction, string text, string answer, bool numeric,
            string question, string picture = null)
        {
            Kind = kind; Instruction = instruction; Text = text; Answer = answer;
            Question = question ?? ""; Numeric = numeric;
            Picture = String.IsNullOrEmpty(picture) ? null : picture;
            Key = kind + "|" + text + "|" + Question;
        }

        internal bool Matches(string input)
        {
            string value = Normalize(input);
            if (value.Length == 0 || value.Length > 16) return false;
            if (Numeric)
            {
                int actual;
                if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out actual) || actual < 0)
                    return false;
                foreach (string variant in Answer.Split('|'))
                {
                    int expected;
                    if (Int32.TryParse(Normalize(variant), NumberStyles.None,
                        CultureInfo.InvariantCulture, out expected) && expected == actual) return true;
                }
                return false;
            }
            foreach (string key in Answer.Split('|')) if (value == Normalize(key)) return true;
            return false;
        }
        internal static string Normalize(string text)
        {
            return (text ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
        }
    }

    internal sealed class TaskGenerator
    {
        private readonly GradedGenerator generator;
        internal TaskGenerator(Settings settings, ReadingBank bank)
        {
            generator = new GradedGenerator(settings.Grade, settings.Level, settings.MathMax, bank.Data);
        }
        internal void BeginAttack(int count) { generator.BeginAttack(count); }
        internal Exercise Next()
        {
            GradedCard card = generator.Next();
            string kind = card.Type == "syll" ? "syllables" : card.Type;
            return new Exercise(kind, card.Instruction, card.Text, card.Answers, card.Numeric, card.Question, card.Picture);
        }
    }
}
