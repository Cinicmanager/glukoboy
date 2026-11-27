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
            "Паша ел суп", "Маша ела торт", "Мама мыла пол", "Папа пил сок", "Даня ел сыр",
            "Нина мыла руки", "Лена мыла раму", "Рома мыл пол", "Мила пила сок", "Лиза ела рис",
            "Вова ел суп", "Саша пил воду", "Тома мыла вазу", "Юра мыл руки", "Коля ел кашу",
            "Дима пил воду", "Ваня ел торт", "У мамы шар", "У папы дом", "Кот пил воду"
        };
        // SHA-256 values from the approved separated-audio manifest. These also
        // detect swapped recordings even when their duration and pauses look valid.
        private static readonly string[] AudioHashes = {
            "21005889b8f5f7ac1da557ae2a806d3b83a089f7dfe120054c8b1c3390d33c86",
            "70bc39dbe9d1b4ec195971ad50fee99b54c6f387083ab39930e7fbe0b4f13eab",
            "e4717b642008fb070c91ee348b0a427e77e1b55930bd080611b795e144bd59f4",
            "b9a3b71c39546071ebdaaa603bba6f984204dcb8fbe26ec1fb06e8f86369fe0c",
            "92bf40a2a1d8f7b147cf17293382f512de3959cf1e75eefea6b07d2d8880a5f4",
            "787aa1432362ab21338e93fafa6d2bacbada0ab5bd0106cc06dd4531bd85c350",
            "7e5dbb5b0de181041d06dce23a0b93fc18f0058e42d2dae042da01c6e46feeb1",
            "3cedd9ffa884bbd362fbd5dd7c506a7c95abd86d8d0154786ca975e6b189a0c2",
            "d9662d8bc5a91e6b1dd4bfbc239201072ed72c1c5e3efa8323d6ecec65068d89",
            "6b90e5936ee60cdfa0b124c55e43f8730859e28d433835943e1b1786e6abf5fd",
            "bf7b8b4c08d5d186590157d0e288398b50b0dbba89f7091d928f6d88c042f139",
            "9e7194c9d3a2baf14c90652fa0427d9a8010e00d87119f67cf2589a1372d2d5a",
            "3bb75f77996a367beead660c411d8fa8d78b79cd424cf52fc9ff786f0767e85d",
            "e68974761aaf67b123429e6f140a81e3b62ff417e5a406e55d653d8b0a69a6a3",
            "4d2711bdd6298842cc592255f50f743afc7b23af9378a14e697b8e03adca3bfe",
            "8113e5eb7507da6b857bf8147b66a869424f4a145ffb7fc47fa799d7bb91f8d6",
            "4e9e96475930f28eae5458a0a515fc64605eb3e888e5fc76ebd8ba80d1f34d8b",
            "33cb4d85cd3f376f72ae61a2914cf3fb57ee314f42afb9e2b44b84c071d6a2e7",
            "f34428449ecebe4113b2431bc3d8639c8ecb7c1f08283ade2dcd2d666d9ea60a",
            "de382e9c7ea52452c0c46627db00d9989091fda07c57c4dbe991e55a963f2c91"
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
                string[] words = card.Text.Split(' ');
                check(words.Length == 3, "Three words per dictation");
                foreach (string word in words) check(word.Length <= 4, "Short dictation word");
                Exercise exercise = card.ToExercise();
                check(exercise.Kind == "dictation" && !exercise.Numeric && exercise.Choice == null &&
                    exercise.DictationResource == card.Resource && keys.Add(exercise.Key), "Distinct typed dictation exercise");
                check(exercise.Text.IndexOf(card.Text, StringComparison.OrdinalIgnoreCase) < 0 &&
                    exercise.Instruction.IndexOf(card.Text, StringComparison.OrdinalIgnoreCase) < 0 &&
                    exercise.Question.Length == 0 && exercise.Picture == null, "No visible answer or answer picture");
                check(exercise.Matches(card.Text) && exercise.Matches(card.Text.ToLowerInvariant()) &&
                    exercise.Matches("  " + card.Text.ToUpperInvariant().Replace(" ", "   ") + " .  "),
                    "Case, spaces and optional final period accepted");
                foreach (string wrong in new string[] { "", words[0], words[1] + " " + words[2],
                    card.Text + " суп", card.Text + "..", card.Text + "!", card.Text.Replace(" ", ""),
                    words[2] + " " + words[1] + " " + words[0], card.Text.Substring(0, card.Text.Length - 1),
                    Expected[(i + 1) % Expected.Length] })
                    check(!exercise.Matches(wrong), "Incomplete, changed or punctuated phrase rejected");
                check(exercise.Matches(card.Text), "Errors do not change the expected dictation");
            }
            check(DictationBank.Matches("Ёж пил воду", " ЕЖ  ПИЛ ВОДУ. "), "Dictation keeps existing е/ё equivalence");
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
            short[] intro = PcmSamples(Resource(DictationBank.IntroResource, check), check);
            check(intro.Length > 22050 && Array.Exists(intro, delegate(short value) { return value != 0; }),
                "Separate introduction contains speech");
            for (int i = 0; i < Expected.Length; i++)
            {
                byte[] wav = Resource(DictationBank.All[i].Resource, check);
                using (SHA256 sha = SHA256.Create())
                    check(BitConverter.ToString(sha.ComputeHash(wav)).Replace("-", "").ToLowerInvariant() == AudioHashes[i],
                        "Audio exactly matches approved manifest: " + DictationBank.All[i].Id);
                short[] samples = PcmSamples(wav, check);
                List<int> silence = new List<int>();
                int run = 0;
                for (int sample = 0; sample <= samples.Length; sample++)
                {
                    if (sample < samples.Length && samples[sample] == 0) run++;
                    else { if (run >= 22050) silence.Add(run); run = 0; }
                }
                check(silence.Count == 3 && silence[0] == 2 * 22050 && silence[1] == 2 * 22050 &&
                    silence[2] == 5 * 22050, "Exactly 2/2/5 second pauses in each loop, without introduction");
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
