using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Bubliklyuk
{
    internal static class DictationChecks
    {
        private static readonly string[] Expected = {
            "мама", "папа", "каша", "ваза", "рама",
            "луна", "рыба", "рука", "нога", "сова",
            "лиса", "коза", "вода", "роза", "муха",
            "мыло", "сани", "лето", "небо", "липа"
        };
        // SHA-256 values from the 2026-11-28 whole-word audio manifest. These also
        // detect swapped recordings even when their duration and pauses look valid.
        private static readonly string[] AudioHashes = {
            "33f2eca7b699800659e4db57925ae6651eff3e8fd246f9f5a14e7ea84a97892f",
            "5f06998c46b6b69c549d52016ddf824570081a87fe913eb22a4c491f35d5d21a",
            "fc601a559040e4586410dc497a74ce494d88d361f6625765f8eeffb8815bbb8a",
            "e5ee39887e5979a0dc017f7a53ef9d23ac66aef27aa4efb6f9af63185c462f44",
            "acb85e1eefe946e1ae7add31b1df493429b72b20ce59d2e13479abe80e555617",
            "cd9373f77d32066e73f4a22523084a6ebb6670a06f16c454ba38317f75e80e42",
            "95908f744ea8ddd81b8254895f328660245f6a07eb04e1dbc0f25f2c39578987",
            "6df4e23e08761e782b3abea2cf1fd72836c771ed5354f48d81e134cf94695f7b",
            "df2fabf0de24f5d83db6d30aef7d988f860ac848067f2de46f5785485980e275",
            "cc5d71c6281c46f705b64883150a5c78e41f84bc4ba4c4e32ae10c3c3c31ca2a",
            "56df23294b490a04fbcf19fcf9414d3a578ce190cb53c67f9e08720b9ec4eef5",
            "bc6dccbbd7fd8444d41001da3fe48f286fe6e8aa20ce4fd32da45675bdb95a56",
            "cf9699bd5477eeed2ab279d6711ac012d62912c19d4fa3f8c05edfe1e1609b60",
            "c85b0e8d182665f17be60fae9852394a96c73a74cb08705b06a8312786eef245",
            "b17e5c782df145dcbcdd21965b03b3f2c155440a3fb0ac5a1888ce48b481b503",
            "205ba28dc016b87e1bc23fb6b04ce46dd8db799e3e2cf0cabdd792ed03a49878",
            "c9dc87493900f23a59181fbc6e2248656ecb60cdc754fe417ee51683e8cc6a34",
            "ced44201a01f72c4051272aab3cd9b2c81aafdc9b6d23d9c16c0ca0f4edf7a55",
            "b3c9657b7d8e5f6f55b10b4dc21878780ec7ccbebf191bbe3f4acb916150ae45",
            "0d218dfbba20bab4451cd2cf858fbe7adf4a653d48ce70230684972067d9532f"
        };

        internal static void Run(Action<bool, string> check)
        {
            ContentAndAnswers(check);
            Audio(check);
            DeckCycles(check);
            CurrentMix(check);
        }

        private static void ContentAndAnswers(Action<bool, string> check)
        {
            check(DictationBank.All.Count == 20, "Exactly twenty approved dictations");
            HashSet<string> texts = new HashSet<string>(), keys = new HashSet<string>();
            for (int i = 0; i < Expected.Length; i++)
            {
                DictationCard card = DictationBank.All[i];
                string id = (i + 1).ToString("00", CultureInfo.InvariantCulture);
                check(card.Id == id && card.Text == Expected[i] && texts.Add(card.Text), "Manifest text and ID " + id);
                check(card.Resource == "dictation-" + id + ".wav", "Matching recording name " + id);
                check(Regex.IsMatch(card.Text, @"^[а-яё]{4}$"), "Exactly one four-letter word per dictation");
                check(Regex.Matches(card.Text, "[аеёиоуыэюя]").Count == 2, "Exactly two vowel nuclei per dictation");
                Exercise exercise = card.ToExercise();
                check(exercise.Kind == "dictation" && !exercise.Numeric && exercise.Choice == null &&
                    exercise.DictationResource == card.Resource && keys.Add(exercise.Key), "Distinct typed dictation exercise");
                check(exercise.Text.IndexOf(card.Text, StringComparison.OrdinalIgnoreCase) < 0 &&
                    exercise.Instruction.IndexOf(card.Text, StringComparison.OrdinalIgnoreCase) < 0 &&
                    exercise.Question.Length == 0 && exercise.Picture == null, "No visible answer or answer picture");
                check(exercise.Matches(card.Text) && exercise.Matches(card.Text.ToLowerInvariant()) &&
                    exercise.Matches("  " + card.Text.ToUpperInvariant() + " .  "),
                    "Case, spaces and optional final period accepted");
                foreach (string wrong in new string[] { "", card.Text.Substring(0, 2), card.Text.Substring(2),
                    card.Text + " суп", card.Text + "..", card.Text + "!", card.Text.Insert(2, " "),
                    card.Text + card.Text, card.Text.Substring(0, card.Text.Length - 1),
                    Expected[(i + 1) % Expected.Length] })
                    check(!exercise.Matches(wrong), "Incomplete, changed or punctuated word rejected");
                check(exercise.Matches(card.Text), "Errors do not change the expected dictation");
            }
            check(DictationBank.Matches("ёлка", " ЕЛКА. "), "Dictation keeps existing е/ё equivalence");
            check(!DictationBank.Matches(Expected[0], null) &&
                !DictationBank.Matches(Expected[0], new string(' ', DictationBank.MaxAnswerLength) + Expected[0]),
                "Empty and excessive dictation input rejected");
            check(AnswerRules.Matches("ёж", false, " ЕЖ ") && !AnswerRules.Matches("ёж", false, "ёж."),
                "Ordinary text answer rules remain unchanged");
            check(AnswerRules.Matches("6", true, "06") && !AnswerRules.Matches("6", true, "6.") &&
                !AnswerRules.Matches("6", true, "-6"), "Numeric answer rules remain unchanged");
        }

        private static byte[] Resource(string name, Action<bool, string> check)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                check(stream != null, "Embedded audio exists: " + name);
                using (MemoryStream bytes = new MemoryStream()) { stream.CopyTo(bytes); return bytes.ToArray(); }
            }
        }
        private static short[] PcmSamples(byte[] wav, Action<bool, string> check)
        {
            check(wav.Length >= 44 && Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" &&
                Encoding.ASCII.GetString(wav, 8, 4) == "WAVE", "Valid WAV container");
            check(BitConverter.ToInt32(wav, 4) + 8 == wav.Length, "WAV length is intact");
            bool format = false;
            short[] samples = null;
            for (int offset = 12; offset + 8 <= wav.Length; )
            {
                string chunk = Encoding.ASCII.GetString(wav, offset, 4);
                int length = BitConverter.ToInt32(wav, offset + 4), data = offset + 8;
                check(length >= 0 && length <= wav.Length - data, "WAV chunk within resource");
                if (chunk == "fmt ")
                {
                    check(length >= 16 && BitConverter.ToInt16(wav, data) == 1 &&
                        BitConverter.ToInt16(wav, data + 2) == 1 && BitConverter.ToInt32(wav, data + 4) == 22050 &&
                        BitConverter.ToInt32(wav, data + 8) == 44100 && BitConverter.ToInt16(wav, data + 12) == 2 &&
                        BitConverter.ToInt16(wav, data + 14) == 16, "Uncompressed mono 22050 Hz 16-bit audio");
                    format = true;
                }
                if (chunk == "data")
                {
                    check(length > 0 && length % 2 == 0, "Nonempty complete audio samples");
                    samples = new short[length / 2];
                    Buffer.BlockCopy(wav, data, samples, 0, length);
                }
                offset = data + length + length % 2;
            }
            check(format && samples != null, "WAV has format and samples");
            return samples;
        }
        private static void Audio(Action<bool, string> check)
        {
            byte[] introWav = Resource(DictationBank.IntroResource, check);
            using (SHA256 sha = SHA256.Create())
                check(BitConverter.ToString(sha.ComputeHash(introWav)).Replace("-", "").ToLowerInvariant() ==
                    "fead5d659e35d24c69cddb89aed239074693b0e396c8f0ea33621a2138e0b34c",
                    "Approved introduction remains byte-for-byte unchanged");
            short[] intro = PcmSamples(introWav, check);
            check(intro.Length > 22050 && Array.Exists(intro, delegate(short value) { return value != 0; }),
                "Separate introduction contains speech");
            for (int i = 0; i < Expected.Length; i++)
            {
                byte[] wav = Resource(DictationBank.All[i].Resource, check);
                using (SHA256 sha = SHA256.Create())
                    check(BitConverter.ToString(sha.ComputeHash(wav)).Replace("-", "").ToLowerInvariant() == AudioHashes[i],
                        "Audio exactly matches whole-word manifest: " + DictationBank.All[i].Id);
                short[] samples = PcmSamples(wav, check);
                check(samples.Length > 5 * 22050, "Each loop contains speech plus its repeat pause");
                List<int> silence = new List<int>();
                int run = 0;
                for (int sample = 0; sample <= samples.Length; sample++)
                {
                    if (sample < samples.Length && samples[sample] == 0) run++;
                    else { if (run >= 22050) silence.Add(run); run = 0; }
                }
                check(silence.Count == 1 && silence[0] == 5 * 22050,
                    "Only one five-second pause in each whole-word loop, without introduction");
                check(samples[0] != 0 && samples[samples.Length - 5 * 22050 - 1] != 0 && samples[samples.Length - 1] == 0,
                    "Loop starts with speech and ends with precisely five seconds of silence");
            }
        }

        private static void DeckCycles(Action<bool, string> check)
        {
            // Offset the attack boundaries so a pair straddles each 20-card refill.
            for (int seed = 0; seed < 20; seed++)
            {
                DictationDeck deck = new DictationDeck(new Random(seed));
                HashSet<string> cycle = new HashSet<string>(), attack = new HashSet<string>();
                string previous = "";
                for (int draw = 0; draw < 200; draw++)
                {
                    if (draw % 20 == 0) cycle.Clear();
                    if (draw == 0 || draw % 2 == 1) { deck.BeginAttack(); attack.Clear(); }
                    DictationCard card = deck.Next();
                    check(cycle.Add(card.Id), "No repeat before all twenty dictations are used");
                    check(attack.Add(card.Id) && previous != card.Id, "Distinct pair even across a cycle boundary");
                    previous = card.Id;
                    if (draw % 20 == 19) check(cycle.Count == 20, "Every cycle covers the complete bank");
                }
            }
        }

        private static void CurrentMix(Action<bool, string> check)
        {
            Regex arithmetic = new Regex(@"^(\d+|□) ([+−]) (\d+|□)(?: \+ (\d+))? = (\d+|□)$");
            for (int grade = 1; grade <= 3; grade++) for (int level = 1; level <= 3; level++)
            {
                TaskGenerator generator = new TaskGenerator(new Settings { Grade = grade, Level = level }, new Random(100 * grade + level));
                string previous = "";
                HashSet<string> cycle = new HashSet<string>();
                for (int attack = 0; attack < 60; attack++)
                {
                    int count = 4 + attack % 3, dictations = 0, math = 0, syllables = 0, choices = 0;
                    generator.BeginAttack(count);
                    HashSet<string> ids = new HashSet<string>();
                    for (int task = 0; task < count; task++)
                    {
                        Exercise exercise = generator.Next();
                        string subject = exercise.Kind == "syllables" || exercise.Kind == "choice" ? "russian" : exercise.Kind;
                        check(subject != previous, "Subjects alternate within and across complete attacks");
                        previous = subject;
                        check(exercise.Matches(exercise.Answer), "Every generated answer is accepted");
                        switch (exercise.Kind)
                        {
                            case "dictation":
                                dictations++;
                                if (cycle.Count == 20) cycle.Clear();
                                check(ids.Add(exercise.DictationResource) && cycle.Add(exercise.DictationResource), "Actual attacks use a full distinct dictation deck");
                                break;
                            case "syllables":
                                syllables++;
                                check(exercise.Text.Split('+').Length == 3 && exercise.Answer.Length <= 6, "Only current short three-syllable words");
                                break;
                            case "choice": choices++; check(exercise.Choice != null, "Current multiple-choice task"); break;
                            case "math":
                                math++;
                                int answer = Int32.Parse(exercise.Answer, CultureInfo.InvariantCulture);
                                check(answer >= 0 && answer <= 10, "Only current first-grade math range");
                                if (exercise.Question.Length == 0)
                                {
                                    Match formula = arithmetic.Match(exercise.Text);
                                    check(formula.Success, "Only current addition, subtraction and missing-number forms");
                                    if (exercise.Instruction == "Реши пример" && formula.Groups[2].Value == "+")
                                        check(formula.Groups[4].Success && Int32.Parse(formula.Groups[1].Value) <= 3 &&
                                            Int32.Parse(formula.Groups[3].Value) <= 3 && Int32.Parse(formula.Groups[4].Value) <= 3,
                                            "Addition keeps three small addends");
                                }
                                break;
                            default: check(false, "Inactive exercise reached current runtime: " + exercise.Kind); break;
                        }
                    }
                    check(dictations == 2 && math == (count == 6 ? 2 : 1) && syllables + choices == (count == 4 ? 1 : 2),
                        "Exact four/five/six task mix");
                    if (count >= 5) check(syllables == 1 && choices == 1, "Five/six tasks include both Russian formats");
                }
            }
            foreach (int count in new int[] { 0, 3, 7 })
            {
                bool rejected = false;
                try { new TaskGenerator(new Settings()).BeginAttack(count); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                check(rejected, "Only four to six tasks are accepted");
            }
        }
    }
}
