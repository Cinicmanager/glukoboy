using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Bubliklyuk
{
    internal static class LearningChecks
    {
        internal static void Run(Action<bool, string> check, string sourceBank)
        {
            check(File.ReadAllText(sourceBank, Encoding.UTF8).Replace("\r\n", "\n").TrimEnd() ==
                DefaultReadingData.Text.Replace("\r\n", "\n").TrimEnd(), "Exported and embedded reading banks match");
            Answers(check);
            Syllables(check);
            Math(check);
        }

        private static GradedReadingBank Parse(string text)
        {
            return (GradedReadingBank)typeof(GradedReadingBank).GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { text });
        }

        private static void Answers(Action<bool, string> check)
        {
            string[] expected = { "на розе", "в реке", "у дуба", "на сосне", "в пруду", "у дома", "на горке", "у сосны", "на лугу", "у стола",
                "у ромы", "у милы", "у коли", "у димы", "у пети", "у нины", "у веры", "у коли", "у оли", "у пети" };
            List<GradedCard> cards = Parse(DefaultReadingData.Text).Get(1, 2);
            check(cards.Count == 60, "Reading count stays 60");
            for (int i = 0; i < expected.Length; i++)
            {
                GradedCard card = cards[20 + i];
                Exercise exercise = new Exercise("reading", "Прочитай", card.Text, card.Answers, false, card.Question);
                foreach (string variant in card.Answers.Split('|'))
                    check(exercise.Matches(variant) && GradedReadingBank.Matches(card, variant), "Old and new answers accepted by both matchers");
                string natural = "  " + expected[i].ToUpperInvariant().Replace(" ", "  ") + " ";
                check(exercise.Matches(natural) && GradedReadingBank.Matches(card, natural), "Natural phrase accepted: " + expected[i]);
                string noun = expected[i].Substring(expected[i].IndexOf(' ') + 1);
                string wrong = expected[i].StartsWith("на ") ? "под " + noun : "на " + noun;
                check(!exercise.Matches(wrong) && !GradedReadingBank.Matches(card, wrong), "Wrong preposition rejected: " + wrong);
            }
            GradedCard rose = cards[20], skis = cards[37];
            foreach (string wrong in new string[] { "под розе", "в розе", "на роза", "на маке", "не роза", "розе 1" })
                check(!GradedReadingBank.Matches(rose, wrong), "Reject false rose answer");
            foreach (string wrong in new string[] { "на Коли", "у коля", "у Лизы" })
                check(!GradedReadingBank.Matches(skis, wrong), "Reject false owner answer");
            string explicitRow = "custom;1;2;where;Жук на листе.;Где жук?;листе|на листе\n";
            GradedReadingBank explicitBank = Parse(explicitRow);
            check(explicitBank.Warning == "" && explicitBank.Get(1,2).Count == 1, "Custom explicit phrase parses");
            foreach (string bad in new string[] { "кот|", "кот|3", "на розе!", "на 3", new string('а',17) })
                check(Parse("bad;1;2;who;Кот спит.;Кто спит?;" + bad).Get(1,2).Count == 0, "Invalid bank answer rejected");

            StringBuilder old = new StringBuilder();
            foreach (string line in DefaultReadingData.Text.Split('\n'))
            {
                string[] fields = line.Split(';');
                if (line.StartsWith("g1q2_") && (fields[3] == "where" || fields[3] == "whose"))
                    fields[6] = String.Join("|", Array.FindAll(fields[6].Split('|'), delegate(string s) { return s.IndexOf(' ') < 0; }));
                old.AppendLine(String.Join(";", fields));
            }
            old.AppendLine(explicitRow.TrimEnd());
            // Same ID as a built-in card, but the custom question/body/answers differ.
            string custom = old.ToString().Replace("g1q2_21;1;2;where;Оса на розе. Жук на маке.;Где оса?;розе|роза;osa",
                "g1q2_21;1;2;where;Оса под розой. Жук на маке.;Где оса?;розой|под розой;osa");
            GradedReadingBank customBank = Parse(custom);
            GradedCard changed = customBank.Get(1,2).Find(delegate(GradedCard c) { return c.Question == "Где оса?"; });
            check(GradedReadingBank.Matches(changed,"под розой") && !GradedReadingBank.Matches(changed,"на розе"), "Custom card with original ID is preserved");
            string changedAnswers = old.ToString().Replace("Где оса?;розе|роза;osa", "Где оса?;роза;osa");
            changed = Parse(changedAnswers).Get(1,2).Find(delegate(GradedCard c) { return c.Question == "Где оса?"; });
            check(changed.Answers == "роза", "Custom answer list is preserved");

            string path = Settings.FilePath("tasks.txt");
            string previous = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : DefaultReadingData.Text;
            try
            {
                File.WriteAllText(path, old.ToString(), new UTF8Encoding(false));
                GradedReadingBank loaded = GradedReadingBank.Load();
                check(loaded.Warning == "" && loaded.Get(1,2).Count == 61, "Legacy bank and custom card retained");
                for (int i=0; i<20; i++) check(GradedReadingBank.Matches(loaded.Get(1,2)[20+i], expected[i]), "Legacy phrase upgraded in memory");
                check(File.ReadAllText(path,Encoding.UTF8) == old.ToString(), "Bank=2 file is not rewritten during compatibility upgrade");
            }
            finally { File.WriteAllText(path, previous, new UTF8Encoding(false)); }
        }

        private static void Syllables(Action<bool, string> check)
        {
            List<GradedCard> cards = GradedContent.Russian(1,2,"syll");
            check(cards.Count == 30, "Thirty syllable cards");
            HashSet<string> words = new HashSet<string>();
            foreach (GradedCard card in cards)
            {
                string[] parts = card.Text.Split('+');
                check(parts.Length == 3 && card.Answers.Length <= 6 && words.Add(card.Answers), "Unique short three-syllable word");
                StringBuilder joined = new StringBuilder();
                foreach (string part in parts)
                {
                    int vowels = 0;
                    foreach (char ch in part.Trim()) if ("аеёиоуыэюя".IndexOf(ch)>=0) vowels++;
                    check(vowels == 1, "Each syllable has one vowel"); joined.Append(part.Trim());
                }
                check(joined.ToString() == card.Answers, "Syllables reconstruct the answer");
            }
            check(words.Contains("собака"), "Requested example included");
            check(GradedContent.Russian(1,1,"syll").Find(delegate(GradedCard c) { return c.Answers == "мама"; }).Text == "ма + ма", "First quarter unchanged");
            check(GradedContent.Russian(1,3,"syll").Exists(delegate(GradedCard c) { return c.Answers == "барабан"; }), "Other levels retain longer words");
        }

        private static void Math(Action<bool, string> check)
        {
            int triple = 0, subtract = 0, missing = 0, story = 0;
            Regex formula = new Regex(@"^(\d+|□) ([+−]) (\d+|□)(?: \+ (\d+))? = (\d+|□)$");
            for (int maximum=3; maximum<=10; maximum++)
            {
                Random random = new Random(150 + maximum);
                for (int i=0; i<4000; i++)
                {
                    GradedCard card = GradedContent.Math(1,2,maximum,random);
                    int answer = Int32.Parse(card.Answers,CultureInfo.InvariantCulture);
                    check(answer >= 0 && answer <= maximum, "Every Q2 answer in MathMax");
                    if (card.Question.Length > 0) { story++; continue; }
                    Match match = formula.Match(card.Text);
                    check(match.Success, "Valid arithmetic form: " + card.Text);
                    int a = match.Groups[1].Value == "□" ? answer : Int32.Parse(match.Groups[1].Value);
                    int b = match.Groups[3].Value == "□" ? answer : Int32.Parse(match.Groups[3].Value);
                    int c = match.Groups[4].Success ? Int32.Parse(match.Groups[4].Value) : 0;
                    int result = match.Groups[5].Value == "□" ? answer : Int32.Parse(match.Groups[5].Value);
                    bool plus = match.Groups[2].Value == "+";
                    check((plus ? a+b+c : a-b) == result && a<=maximum && result<=maximum, "Arithmetic is correct");
                    if (card.Instruction == "Какое число пропущено?")
                    { missing++; check(!match.Groups[4].Success && b>=1 && b<=3, "Missing-number structure unchanged"); }
                    else if (plus)
                    { triple++; check(match.Groups[4].Success && a>=1 && a<=3 && b>=1 && b<=3 && c>=1 && c<=3, "Three small positive addends"); }
                    else { subtract++; check(!match.Groups[4].Success && b>=1 && b<=3 && a>=b, "Subtraction structure unchanged"); }
                }
            }
            check(triple>1000 && subtract>1000 && missing>1000 && story>1000, "All math formats exercised");
            HashSet<string> questions = new HashSet<string>(), texts = new HashSet<string>();
            for (int kind=0; kind<GradedContent.SecondQuarterStoryCount; kind++)
                foreach (bool plus in new bool[] { false,true })
                    for (int step=1; step<=3; step++)
                        for (int a=plus?0:step; a<=(plus?10-step:10); a++)
                        {
                            GradedCard card = GradedContent.FirstGradeSecondQuarterStory(a,step,plus,kind);
                            check(card.Answers == (plus?a+step:a-step).ToString(CultureInfo.InvariantCulture), "Story arithmetic");
                            check(card.Text.Contains(a.ToString()) && card.Text.Contains(step.ToString()), "Story includes both quantities");
                            check(!card.Text.Contains("Веры") && !card.Text.Contains("мяч"), "New stories replace repeated Vera/balls");
                            questions.Add(card.Question); texts.Add(card.Text);
                        }
            check(questions.Count>=8 && texts.Count>=500, "Varied story settings and texts");
        }
    }
}
