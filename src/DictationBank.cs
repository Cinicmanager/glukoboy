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
            return new Exercise("dictation", "Запиши предложение", "Слушай и печатай", Text,
                false, "", null, null, Resource);
        }
    }

    internal static class DictationBank
    {
        internal const string IntroResource = "dictation-intro.wav";
        internal const int MaxAnswerLength = 64;
        // Matches the approved separated-audio manifest, 2026-11-27.
        internal static readonly ReadOnlyCollection<DictationCard> All = Array.AsReadOnly(new DictationCard[] {
            new DictationCard(1, "Паша ел суп"),
            new DictationCard(2, "Маша ела торт"),
            new DictationCard(3, "Мама мыла пол"),
            new DictationCard(4, "Папа пил сок"),
            new DictationCard(5, "Даня ел сыр"),
            new DictationCard(6, "Нина мыла руки"),
            new DictationCard(7, "Лена мыла раму"),
            new DictationCard(8, "Рома мыл пол"),
            new DictationCard(9, "Мила пила сок"),
            new DictationCard(10, "Лиза ела рис"),
            new DictationCard(11, "Вова ел суп"),
            new DictationCard(12, "Саша пил воду"),
            new DictationCard(13, "Тома мыла вазу"),
            new DictationCard(14, "Юра мыл руки"),
            new DictationCard(15, "Коля ел кашу"),
            new DictationCard(16, "Дима пил воду"),
            new DictationCard(17, "Ваня ел торт"),
            new DictationCard(18, "У мамы шар"),
            new DictationCard(19, "У папы дом"),
            new DictationCard(20, "Кот пил воду")
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
            // At a cycle boundary, postpone the previous phrase and any phrase
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
