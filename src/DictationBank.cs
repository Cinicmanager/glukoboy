using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Bubliklyuk
{
    internal sealed class DictationCard
    {
        internal readonly string Id, Text, Resource;
        internal DictationCard(int number, string text)
        {
            Id = number.ToString("00", CultureInfo.InvariantCulture);
            Text = text;
            Resource = "dictation-" + Id + ".wav";
        }
        internal Exercise ToExercise()
        {
            return new Exercise("dictation", "Запиши слово", "Слушай и печатай", Text,
                false, "", null, null, Resource);
        }
    }

    internal static class DictationBank
    {
        internal const string IntroResource = "dictation-intro.wav";
        internal const int MaxAnswerLength = 64;
        // One easy four-letter, two-syllable word per dictation, 2026-11-28.
        internal static readonly ReadOnlyCollection<DictationCard> All = Array.AsReadOnly(new DictationCard[] {
            new DictationCard(1, "мама"),
            new DictationCard(2, "папа"),
            new DictationCard(3, "каша"),
            new DictationCard(4, "ваза"),
            new DictationCard(5, "рама"),
            new DictationCard(6, "луна"),
            new DictationCard(7, "рыба"),
            new DictationCard(8, "рука"),
            new DictationCard(9, "нога"),
            new DictationCard(10, "сова"),
            new DictationCard(11, "лиса"),
            new DictationCard(12, "коза"),
            new DictationCard(13, "вода"),
            new DictationCard(14, "роза"),
            new DictationCard(15, "муха"),
            new DictationCard(16, "мыло"),
            new DictationCard(17, "сани"),
            new DictationCard(18, "лето"),
            new DictationCard(19, "небо"),
            new DictationCard(20, "липа")
        });

        internal static bool Matches(string expected, string input)
        {
            if (input == null || input.Length > MaxAnswerLength) return false;
            string value = AnswerRules.Normalize(input);
            if (value.EndsWith(".", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1).TrimEnd();
            return value.Length > 0 && value == AnswerRules.Normalize(expected);
        }
    }

    internal sealed class DictationDeck
    {
        private readonly Random random;
        private readonly List<DictationCard> remaining = new List<DictationCard>();
        private readonly HashSet<string> attackIds = new HashSet<string>(StringComparer.Ordinal);
        private string previous = "";
        internal DictationDeck(Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            this.random = random;
        }
        internal void BeginAttack() { attackIds.Clear(); }
        internal DictationCard Next()
        {
            if (remaining.Count == 0)
            {
                remaining.AddRange(DictationBank.All);
                for (int i = remaining.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    DictationCard saved = remaining[i]; remaining[i] = remaining[j]; remaining[j] = saved;
                }
            }
            // At a cycle boundary, postpone the previous word and any word
            // already used in this attack without discarding it from the new cycle.
            int index = remaining.FindIndex(delegate(DictationCard item) {
                return item.Id != previous && !attackIds.Contains(item.Id);
            });
            if (index < 0) throw new InvalidOperationException("Все доступные диктанты уже были в этой атаке.");
            DictationCard card = remaining[index];
            remaining.RemoveAt(index);
            attackIds.Add(card.Id);
            previous = card.Id;
            return card;
        }
    }
}
