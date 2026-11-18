using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Bubliklyuk
{
    internal static class AnswerRules
    {
        internal const int MaxLength = 16;

        internal static string Normalize(string text)
        {
            StringBuilder result = new StringBuilder();
            bool space = false;
            foreach (char ch in (text ?? "").Trim().ToLowerInvariant().Replace('ё', 'е'))
            {
                if (Char.IsWhiteSpace(ch)) { space = result.Length > 0; continue; }
                if (space) result.Append(' ');
                result.Append(ch);
                space = false;
            }
            return result.ToString();
        }

        internal static bool IsTextAnswer(string normalized)
        {
            if (normalized.Length == 0 || normalized.Length > MaxLength) return false;
            foreach (char ch in normalized)
                if (ch != ' ' && (ch < 'а' || ch > 'я')) return false;
            return true;
        }

        internal static bool Matches(string answers, bool numeric, string input)
        {
            string value = Normalize(input);
            if (value.Length == 0 || value.Length > MaxLength) return false;
            if (numeric)
            {
                int actual;
                if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out actual) || actual < 0) return false;
                foreach (string variant in answers.Split('|'))
                {
                    int expected;
                    if (Int32.TryParse(Normalize(variant), NumberStyles.None, CultureInfo.InvariantCulture, out expected) && expected == actual)
                        return true;
                }
                return false;
            }
            foreach (string variant in answers.Split('|'))
                if (value == Normalize(variant)) return true;
            return false;
        }
    }

    internal static class ReadingAnswerCompatibility
    {
        private static readonly Dictionary<string, string> upgrades = CreateUpgrades();

        // Update only exact old built-in cards, in memory. A custom card with the
        // same ID but different text, question, answers or picture is untouched.
        internal static string Expand(string[] fields)
        {
            string expanded;
            return upgrades.TryGetValue(Key(fields, fields[6]), out expanded) ? expanded : fields[6];
        }

        private static Dictionary<string, string> CreateUpgrades()
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            using (StringReader reader = new StringReader(DefaultReadingData.Text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] fields = line.Split(';');
                    if (fields.Length < 7 || fields[1] != "1" || fields[2] != "2" ||
                        (fields[3] != "where" && fields[3] != "whose")) continue;
                    List<string> words = new List<string>();
                    bool phrase = false;
                    foreach (string answer in fields[6].Split('|'))
                    {
                        if (answer.IndexOf(' ') >= 0) phrase = true;
                        else words.Add(answer);
                    }
                    if (phrase && words.Count > 0)
                        result.Add(Key(fields, String.Join("|", words.ToArray())), fields[6]);
                }
            }
            return result;
        }

        private static string Key(string[] fields, string answers)
        {
            return String.Join("\u001f", new string[] {
                fields[0].Trim(), fields[1].Trim(), fields[2].Trim(), fields[3].Trim(),
                fields[4].Trim(), fields[5].Trim(), answers.Trim(), fields.Length == 8 ? fields[7].Trim() : "" });
        }
    }
}
