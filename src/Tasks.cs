using System;
using System.Globalization;

namespace Bubliklyuk
{
    internal sealed class Exercise
    {
        internal readonly string Kind, Instruction, Text, Answer, Question, Key, Picture;
        internal readonly bool Numeric;
        internal readonly ChoiceQuestion Choice;
        internal bool LongText { get { return Question.Length > 0; } }

        internal Exercise(string kind, string instruction, string text, string answer, bool numeric,
            string question, string picture = null, ChoiceQuestion choice = null)
        {
            Kind = kind; Instruction = instruction; Text = text; Answer = answer;
            Question = question ?? ""; Numeric = numeric;
            Picture = String.IsNullOrEmpty(picture) ? null : picture;
            Choice = choice;
            Key = kind + "|" + text + "|" + Question;
        }

        internal bool Matches(string input)
        {
            return AnswerRules.Matches(Answer, Numeric, input);
        }
        internal static string Normalize(string text)
        {
            return AnswerRules.Normalize(text);
        }
    }

    internal sealed class TaskGenerator
    {
        private readonly GradedGenerator generator;
        private readonly ChoiceDeck choices = new ChoiceDeck(new Random());
        private readonly bool choiceLevel;
        internal TaskGenerator(Settings settings, ReadingBank bank)
        {
            generator = new GradedGenerator(settings.Grade, settings.Level, settings.MathMax, bank.Data);
            choiceLevel = settings.Grade == 1 && settings.Level == 2;
        }
        internal void BeginAttack(int count) { generator.BeginAttack(count); }
        internal Exercise Next()
        {
            GradedCard card = generator.Next();
            if (choiceLevel && card.Type == "odd") return NextChoice(null);
            string kind = card.Type == "syll" ? "syllables" : card.Type;
            return new Exercise(kind, card.Instruction, card.Text, card.Answers, card.Numeric, card.Question, card.Picture);
        }
        internal Exercise NextChoice(string kind)
        {
            ChoiceQuestion card = choices.Next(kind);
            return new Exercise("choice", "Выбери ответ", card.Prompt, card.Correct, false, "", null, card);
        }
    }
}
