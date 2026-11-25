using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Bubliklyuk
{
    internal static class Regression
    {
        private static int checks;
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        private static object Get(object obj, string name) { return obj.GetType().GetField(name, Flags).GetValue(obj); }
        private static void Set(object obj, string name, object value) { obj.GetType().GetField(name, Flags).SetValue(obj, value); }
        private static object Call(object obj, string name, params object[] args) { return obj.GetType().GetMethod(name, Flags).Invoke(obj, args); }
        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                BankChecks(); ChoiceChecks(); NightChecks(); NightStartupChecks.Run(Check); SettingsChecks();
                LearningChecks.Run(Check, args[1]);
                ReadingBank bank = ReadingBank.Load();
                Check(bank.Warning.Length == 0, "Reading bank: " + bank.Warning);
                Q2ReadingChecks.Run(Check, bank);
                AttackLengthChecks(bank);
                MixChecks(bank); RenderChecks(bank, args[0]);
                Console.WriteLine("PASS: " + checks + " assertions; renders: " + args[0]); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void BankChecks()
        {
            Check(ChoiceBank.All.Count == 60, "Exactly 60 new cards");
            foreach (string kind in new string[] { "odd", "describe", "pattern" })
                Check(ChoiceBank.All.FindAll(delegate(ChoiceQuestion c) { return c.Kind == kind; }).Count == (kind == "odd" ? 30 : 15), "Card distribution");
            HashSet<string> ids = new HashSet<string>(), signatures = new HashSet<string>();
            foreach (ChoiceQuestion card in ChoiceBank.All)
            {
                Check(ids.Add(card.Id), "Unique ID");
                Check(card.Options.Length == 4 && new HashSet<string>(card.Options).Count == 4, "Four distinct choices");
                Check(Array.IndexOf(card.Options, card.Correct) >= 0, "Answer exists");
                string[] sorted = (string[])card.Options.Clone(); Array.Sort(sorted);
                Check(signatures.Add(card.Prompt + String.Join(" ", sorted) + String.Join(" ", card.Sequence)), "Duplicate card " + card.Id);
                foreach (string token in card.Options) if (ChoiceArt.IsPicture(token))
                    Check(Assembly.GetExecutingAssembly().GetManifestResourceInfo("pic_" + token + ".png") != null, "Missing image " + token);
            }
        }
        private static void ChoiceChecks()
        {
            Random random = new Random(24);
            foreach (ChoiceQuestion card in ChoiceBank.All) for (int old = 0; old < 4; old++)
            {
                ChoiceRound round = new ChoiceRound(card, random, old, 0);
                Check(round.CorrectIndex != old, "Replacement changes position");
                int wrong = (round.CorrectIndex + 1) % 4;
                Check(round.Answer(wrong, 100) == ChoiceResult.Ignored, "No click-through");
                Check(round.Answer(wrong, 500) == ChoiceResult.Wrong, "First error retains card");
                Check(round.Answer(wrong, 550) == ChoiceResult.Ignored && round.Errors == 1, "Double click ignored");
                Check(round.Answer(wrong, 1400) == ChoiceResult.Replace, "Second error replaces");
                Check(round.Answer(round.CorrectIndex, 2500) == ChoiceResult.Ignored, "Exhausted card cannot score");
                ChoiceRound success = new ChoiceRound(card, random, -1, 0);
                Check(success.Answer(success.CorrectIndex, 500) == ChoiceResult.Correct, "Correct choice accepted");
                Check(success.Answer(success.CorrectIndex, 1500) == ChoiceResult.Ignored, "No duplicate scoring");
            }
            foreach (string kind in new string[] { "odd", "describe", "pattern" })
            {
                ChoiceDeck deck = new ChoiceDeck(new Random(5)); Queue<string> recent = new Queue<string>();
                for (int i = 0; i < 300; i++)
                {
                    string id = deck.Next(kind).Id; Check(!recent.Contains(id), "Recent repeat " + id);
                    recent.Enqueue(id); if (recent.Count > 12) recent.Dequeue();
                }
            }
            Exercise typed = new Exercise("reading", "", "", "ёж|ежа", false, "");
            Check(!typed.Matches("кот") && !typed.Matches("пёс") && typed.Matches(" ЁЖ "), "Typed retry and ё/е");
        }
        private static void NightChecks()
        {
            DateTime date = new DateTime(2026, 11, 16, 21, 0, 0), start, end;
            foreach (bool testMode in new bool[] { false, true })
            {
                Settings settings = new Settings { TestMode = testMode };
                Check(!NightSchedule.PauseAttacks(settings, date.AddMinutes(-15).AddTicks(-1)), "Attack pause starts exactly at 20:45");
                foreach (double minutes in new double[] { -15, -10, -5, -1 })
                    Check(NightSchedule.PauseAttacks(settings, date.AddMinutes(minutes)), "No attacks in the last 15 minutes, including TestMode");
                Check(NightSchedule.PauseAttacks(settings, date.AddTicks(-1)), "Pause includes the last instant before 21:00");
                Check(!NightSchedule.PauseAttacks(settings, date), "At 21:00 night takes precedence");
                Check(NightSchedule.Current(settings, date, out start, out end), "Night still starts at 21:00");
                Check(!NightSchedule.PauseAttacks(settings, date.AddHours(10)), "No stale pause in the morning");
                settings.NightFrom = new TimeSpan(22, 30, 0);
                DateTime custom = date.Date.Add(settings.NightFrom);
                Check(!NightSchedule.PauseAttacks(settings, custom.AddMinutes(-15).AddTicks(-1)), "Custom pause lower boundary");
                Check(NightSchedule.PauseAttacks(settings, custom.AddMinutes(-15)), "Custom night pauses at 22:15");
                Check(!NightSchedule.PauseAttacks(settings, date.AddMinutes(-10)), "Pause follows configured NightFrom");
                settings.NightFrom = new TimeSpan(0, 5, 0);
                custom = date.Date.AddDays(1).Add(settings.NightFrom);
                Check(!NightSchedule.PauseAttacks(settings, custom.AddMinutes(-15).AddTicks(-1)), "Midnight pause lower boundary");
                Check(NightSchedule.PauseAttacks(settings, custom.AddMinutes(-15)), "00:05 night pauses at 23:50");
                Check(NightSchedule.PauseAttacks(settings, custom.Date), "Pause survives midnight");
                Check(!NightSchedule.PauseAttacks(settings, custom), "Midnight pause ends exactly at night start");
            }
            Check(TimeWindow.Current(TimeSpan.FromHours(21), TimeSpan.FromHours(7), date, out start, out end), "21:00 enters night");
            string story = NightStories.Select(NightStories.DefaultModes, start).Id;
            Check(TimeWindow.Current(TimeSpan.FromHours(21), TimeSpan.FromHours(7), date.AddHours(6), out start, out end), "Night crosses midnight");
            Check(story == NightStories.Select(NightStories.DefaultModes, start).Id, "Same story after midnight/restart");
            Check(!TimeWindow.Current(TimeSpan.FromHours(21), TimeSpan.FromHours(7), date.AddHours(10), out start, out end), "07:00 exits night");
            for (int i = 0; i < 366; i++)
            {
                Check(NightStories.Select(NightStories.DefaultModes, date.AddDays(i)).Id != NightStories.Select(NightStories.DefaultModes, date.AddDays(i+1)).Id, "No consecutive repeats");
                Check(NightStories.Select("overheat", date.AddDays(i)).Id == "overheat", "Overheat-only mode");
            }
            Check(NightStories.Normalize("repair,REPAIR,invalid,update") == "repair,update", "Normalize modes");
            Check(NightStories.Normalize("invalid") == NightStories.DefaultModes, "Fallback modes");
        }
        private static void SettingsChecks()
        {
            string path = Settings.FilePath("settings.ini");
            File.WriteAllText(path, "Grade=1\nLevel=2\nAutoUpdate=1\n", Encoding.UTF8);
            Settings old = Settings.Load();
            Check(old.Level == 2 && old.NightModes == NightStories.DefaultModes && old.AnimationLevel == 1, "Legacy INI defaults");
            File.WriteAllText(path, "NightModes=overheat\nAnimationLevel=0\n", Encoding.UTF8);
            Check(Settings.Load().NightModes == "overheat" && Settings.Load().AnimationLevel == 0, "New settings");
            File.WriteAllText(path, "AnimationLevel=9\n", Encoding.UTF8);
            Check(Settings.Load().AnimationLevel == 1, "Invalid animation fallback");
        }
        private static void MixChecks(ReadingBank bank)
        {
            for (int grade = 1; grade <= 3; grade++) for (int level = 1; level <= 3; level++)
            {
                TaskGenerator gen = new TaskGenerator(new Settings { Grade = grade, Level = level }, bank); int choices = 0;
                for (int attack = 0; attack < 30; attack++)
                {
                    gen.BeginAttack(4); int reading = 0, math = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        Exercise card = gen.Next();
                        if (card.Kind == "reading") reading++; if (card.Kind == "math") math++;
                        if (card.Choice != null) choices++;
                        Check(card.Matches(card.Answer.Split('|')[0]), "Generated answer accepted");
                    }
                    Check(reading == 2 && math == 1, "Reading and math retained");
                }
                Check(choices == (grade == 1 && level == 2 ? 15 : 0), "Choices only in grade 1 quarter 2");
            }
        }
        private static void AttackLengthChecks(ReadingBank bank)
        {
            foreach (int count in new int[] { 4, 5 })
            {
                Settings settings = new Settings { Grade = 1, Level = 2 };
                TaskGenerator generator = new TaskGenerator(settings, bank);
                generator.BeginAttack(count);
                using (AttackForm form = new AttackForm(settings, generator, count, delegate(Exception ex) { throw ex; }))
                {
                    form.TopMost = false;
                    Call(form, "NewExercise", new object[] { null });
                    for (int answer = 1; answer <= count; answer++)
                    {
                        Check((int)Get(form, "currentTaskIndex") == answer, "Progress shows the actual task number");
                        Exercise exercise = (Exercise)Get(form, "exercise");
                        if (exercise.Choice == null) Call(form, "SubmitAnswer", exercise.Answer.Split('|')[0]);
                        else
                        {
                            ChoiceRound round = (ChoiceRound)Get(form, "choiceRound");
                            Set(round, "availableAt", 0L); Call(form, "Choose", round.CorrectIndex);
                        }
                        Check((int)Get(form, "completed") == answer, "Exactly one completed task per answer");
                        Set(form, "stageStarted", -2000L);
                        Call(form, "AnimationTick", null, EventArgs.Empty);
                        Check(Get(form, "stage").ToString() == (answer == count ? "Defeated" : "Asking"), "Victory only after all four/five tasks");
                    }
                    Check((float)Get(form, "barTarget") == 0f, "Final health target is zero");
                }
            }
        }
        private static void Save(Form form, string path)
        {
            form.CreateControl();
            using (Bitmap bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                form.DrawToBitmap(bitmap, form.ClientRectangle);
                // The form is never shown (no screen capture, audio mute or focus
                // changes). Render the visible child painters explicitly because
                // WinForms skips children of an invisible top-level form.
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    foreach (Control control in form.Controls)
                    {
                        bool visible = (bool)typeof(Control).GetMethod("GetState", Flags).Invoke(control, new object[] { 2 });
                        if (!visible) continue;
                        using (Bitmap child = new Bitmap(control.Width, control.Height))
                        {
                            control.DrawToBitmap(child, control.ClientRectangle);
                            graphics.DrawImageUnscaled(child, control.Left, control.Top);
                        }
                    }
                bitmap.Save(path, ImageFormat.Png);
            }
            Check(new FileInfo(path).Length > 10000, "Rendered " + path);
        }
        private static void RenderChecks(ReadingBank bank, string output)
        {
            Action<Exception> fail = delegate(Exception ex) { throw new Exception("UI rendering failure", ex); };
            foreach (Size size in new Size[] { new Size(1366,768), new Size(1920,1080), new Size(1024,768) })
            {
                Settings settings = new Settings { Grade = 1, Level = 2 }; TaskGenerator gen = new TaskGenerator(settings, bank);
                using (AttackForm form = new AttackForm(settings, gen, 4, fail))
                {
                    form.TopMost = false; form.Size = size;
                    foreach (string kind in new string[] { "odd", "describe", "pattern" })
                    {
                        Call(form, "NewExercise", gen.NextChoice(kind)); Set(form, "cardShownAt", -1000L); Set(form, "appearanceStarted", -2000L);
                        Save(form, Path.Combine(output, kind + "-" + size.Width + ".png"));
                        Check((int)Get(form, "completed") == 0, "Replacement cannot score");
                        foreach (ChoiceTile tile in (ChoiceTile[])Get(form, "choiceButtons"))
                            Check(form.ClientRectangle.Contains(tile.Bounds), "Choice within viewport");
                    }
                    ChoiceRound round = (ChoiceRound)Get(form, "choiceRound");
                    string oldId = round.Question.Id;
                    int oldIndex = round.CorrectIndex;
                    Set(round, "availableAt", 0L); Call(form, "Choose", (round.CorrectIndex + 1) % 4);
                    Check(Object.ReferenceEquals(round, Get(form, "choiceRound")), "UI retains first-error card");
                    Set(round, "availableAt", 0L); Call(form, "Choose", (round.CorrectIndex + 1) % 4);
                    Call(form, "TickChoices", Int64.MaxValue);
                    ChoiceRound replacement = (ChoiceRound)Get(form, "choiceRound");
                    Check(replacement.Question.Id != oldId && replacement.CorrectIndex != oldIndex, "UI replaces card and position");
                    Check((int)Get(form, "completed") == 0 && (int)Get(form, "currentTaskIndex") == 1, "UI preserves progress after two errors");
                    Set(replacement, "availableAt", 0L); Call(form, "Choose", replacement.CorrectIndex);
                    Check((int)Get(form, "completed") == 1, "UI choice accepted");
                    Call(form, "NewExercise", new Exercise("reading", "", "Кот ел сыр. Пёс спал.", "кот", false, "Кто ел сыр?", "kot"));
                    Exercise typed = (Exercise)Get(form, "exercise");
                    Call(form, "SubmitAnswer", "пёс"); Call(form, "SubmitAnswer", "пёс");
                    Check(Object.ReferenceEquals(typed, Get(form, "exercise")) && (int)Get(form, "completed") == 1, "Two typed errors do not replace or score");
                    Call(form, "SubmitAnswer", "кот");
                    Check((int)Get(form, "completed") == 2, "Typed correction still scores");
                    Call(form, "NewExercise", typed);
                    Set(form, "cardShownAt", -1000L); Save(form, Path.Combine(output, "reading-" + size.Width + ".png"));
                }
                GradedCard storyCard = GradedContent.FirstGradeSecondQuarterStory(6, 3, true, 2);
                Exercise[] updated = {
                    new Exercise("math", storyCard.Instruction, storyCard.Text, storyCard.Answers, true, storyCard.Question),
                    new Exercise("math", "Реши пример", "1 + 2 + 3 = □", "6", true, ""),
                    new Exercise("russian", "Собери слово", "со + ба + ка", "собака", false, ""),
                    new Exercise("reading", "Прочитай", "Оса села на розу. Жук сел на лук.", "розе|роза|на розе", false, "Где оса?", "roza")
                };
                string[] updatedNames = { "story", "addition", "syllables", "phrase" };
                for (int sample = 0; sample < updated.Length; sample++)
                    using (AttackForm form = new AttackForm(settings, gen, 3, fail))
                    {
                        form.TopMost = false; form.Size = size;
                        Call(form, "NewExercise", updated[sample]);
                        Set(form, "cardShownAt", -1000L); Set(form, "appearanceStarted", -2000L);
                        if (sample == 3)
                        {
                            ((TextBox)Get(form, "answer")).Text = "на розе";
                            Check(((TextBox)Get(form, "answer")).Text == "на розе", "Answer field retains the prepositional phrase");
                        }
                        Save(form, Path.Combine(output, updatedNames[sample] + "-" + size.Width + ".png"));
                        if (sample == 3)
                        {
                            Call(form, "SubmitAnswer", "На розе");
                            Check((int)Get(form, "completed") == 1, "UI accepts a prepositional phrase");
                        }
                    }
                foreach (NightStory story in NightStories.All)
                {
                    settings.NightModes = story.Id;
                    using (NightForm night = new NightForm(new DateTime(2026,11,16,21,0,0), new DateTime(2026,11,17,7,0,0), settings, fail))
                    { night.TopMost = false; night.Size = size; Save(night, Path.Combine(output, story.Id + "-" + size.Width + ".png")); }
                }
                List<GradedCard> readingSamples = bank.Data.Get(1, 2);
                readingSamples.Sort(delegate(GradedCard a, GradedCard b) { return b.Text.Length.CompareTo(a.Text.Length); });
                GradedCard[] threePhrases = {
                    readingSamples.Find(delegate(GradedCard c) { return c.Picture != null; }),
                    readingSamples.Find(delegate(GradedCard c) { return c.Picture == null; }),
                    readingSamples.Find(delegate(GradedCard c) { return c.Question == "Что взяла Таня?"; })
                };
                for (int sample = 0; sample < threePhrases.Length; sample++)
                    using (AttackForm form = new AttackForm(settings, gen, 5, fail))
                    {
                        GradedCard card = threePhrases[sample];
                        if (sample == 2)
                        {
                            Random order = new Random(12);
                            do { card = ReadingSentenceOrder.Shuffle(card, order); }
                            while (!card.Text.EndsWith("Таня взяла шапку.", StringComparison.Ordinal));
                        }
                        form.TopMost = false; form.Size = size;
                        Set(form, "completed", 4);
                        Set(form, "displayedBar", 20f); Set(form, "barFrom", 20f); Set(form, "barTarget", 20f);
                        Call(form, "NewExercise", new Exercise("reading", "Прочитай", card.Text, card.Answers, false, card.Question, card.Picture));
                        Set(form, "cardShownAt", -1000L); Set(form, "appearanceStarted", -2000L);
                        Save(form, Path.Combine(output, "three-phrases-" + sample + "-" + size.Width + ".png"));
                    }
                if (size.Width == 1366)
                    for (int variant = 0; variant < 3; variant++)
                        using (AttackForm form = new AttackForm(settings, gen, 3, fail))
                        {
                            form.TopMost = false; form.Size = size;
                            Call(form, "NewExercise", new Exercise("math", "Реши пример", "2 + 3 = □", "5", true, ""));
                            Set(form, "cardShownAt", -1000L);
                            Set(form, "appearanceStarted", -2000L);
                            Set(form, "finaleVariant", variant);
                            Set(form, "completed", 3); Set(form, "currentTaskIndex", 3); Set(form, "displayedBar", 0f);
                            Call(form, "ShowVictory", -1000L);
                            Save(form, Path.Combine(output, "finale-" + variant + ".png"));
                            Set(form, "stage", Enum.Parse(Get(form, "stage").GetType(), "Hit"));
                            Set(form, "virusImage", "hit.png"); Set(form, "hitVariant", variant); Set(form, "stageStarted", -500L);
                            Save(form, Path.Combine(output, "hit-" + variant + ".png"));
                        }
            }
        }
    }
}
