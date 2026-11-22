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
}
