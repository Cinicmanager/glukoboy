using System;
using System.Collections.Generic;
using System.IO;

namespace Bubliklyuk
{
    internal static class ReadingCardCompatibility
    {
        // Frozen standard Q2 rows from 1.6.2. Never infer upgrades from ID alone:
        // changing the text, question, answers or picture makes a card custom.
        internal const string PreviousSecondQuarter =
            "g1q2_01;1;2;who;Кот ел сыр. Пёс спал.;Кто ел сыр?;кот;kot\n" +
            "g1q2_02;1;2;who;Пёс нёс мяч. Кот спал.;Кто нёс мяч?;пёс;pes\n" +
            "g1q2_03;1;2;who;Ёж искал гриб. Жук спал.;Кто искал гриб?;ёж;ezh\n" +
            "g1q2_04;1;2;who;Жук сел на мак. Оса спала.;Кто сел на мак?;жук;zhuk\n" +
            "g1q2_05;1;2;who;Кит плыл один. Рак спал.;Кто плыл один?;кит;kit\n" +
            "g1q2_06;1;2;who;Нина мыла пол. Рома спал.;Кто мыл пол?;нина;\n" +
            "g1q2_07;1;2;who;Мила пела. Вера ела суп.;Кто пел?;мила;\n" +
            "g1q2_08;1;2;who;Коля нёс зонт. Дима нёс шар.;Кто нёс зонт?;коля;\n" +
            "g1q2_09;1;2;who;Лиза мыла вазу. Оля мыла миску.;Кто мыл вазу?;лиза;\n" +
            "g1q2_10;1;2;who;Таня дала сок. Петя дал сыр.;Кто дал сок?;таня;\n" +
            "g1q2_11;1;2;what;Рома взял мяч. Нина взяла шар.;Что взял Рома?;мяч;myach\n" +
            "g1q2_12;1;2;what;Мила пила сок. Вера пила чай.;Что пила Мила?;сок;sok\n" +
            "g1q2_13;1;2;what;Дима видит дом. Оля видит сад.;Что видит Дима?;дом;dom\n" +
            "g1q2_14;1;2;what;Петя ел сыр. Коля ел суп.;Что ел Петя?;сыр;syr\n" +
            "g1q2_15;1;2;what;Мама мыла лук. Папа мыл нож.;Что мыла мама?;лук;luk\n" +
            "g1q2_16;1;2;what;Вера несла торт. Нина несла зонт.;Что несла Вера?;торт;\n" +
            "g1q2_17;1;2;what;Папа взял пилу. Мама взяла нож.;Что взял папа?;пилу|пила;\n" +
            "g1q2_18;1;2;what;Оля видит лодку. Дима видит плот.;Что видит Оля?;лодку|лодка;\n" +
            "g1q2_19;1;2;what;Таня взяла шапку. Мила взяла шар.;Что взяла Таня?;шапку|шапка;\n" +
            "g1q2_20;1;2;what;Коля мыл миску. Рома мыл вазу.;Что мыл Рома?;вазу|ваза;\n" +
            "g1q2_21;1;2;where;Оса на розе. Жук на маке.;Где оса?;розе|роза|на розе;osa\n" +
            "g1q2_22;1;2;where;Рак в реке. Жук на листе.;Где рак?;реке|река|в реке;rak\n" +
            "g1q2_23;1;2;where;Лиса у дуба. Коза у дома.;Где лиса?;дуба|дуб|у дуба;lisa\n" +
            "g1q2_24;1;2;where;Сова на сосне. Ёж под дубом.;Где сова?;сосне|сосна|на сосне;sova\n" +
            "g1q2_25;1;2;where;Утка в пруду. Коза у дома.;Где утка?;пруду|пруд|в пруду;utka\n" +
            "g1q2_26;1;2;where;Рома у дома. Нина у реки.;Где Рома?;дома|дом|у дома;\n" +
            "g1q2_27;1;2;where;Дима на горке. Петя у дуба.;Где Дима?;горке|горка|на горке;\n" +
            "g1q2_28;1;2;where;Оля у сосны. Таня у дома.;Где Оля?;сосны|сосна|у сосны;\n" +
            "g1q2_29;1;2;where;Мила на лугу. Вера у реки.;Где Мила?;лугу|луг|на лугу;\n" +
            "g1q2_30;1;2;where;Коля у стола. Лиза у окна.;Где Коля?;стола|стол|у стола;\n" +
            "g1q2_31;1;2;whose;У Ромы рыба. У Нины сок.;У кого рыба?;ромы|рома|у ромы;ryba\n" +
            "g1q2_32;1;2;whose;У Милы гриб. У Веры лук.;У кого гриб?;милы|мила|у милы;grib\n" +
            "g1q2_33;1;2;whose;У Коли роза. У Лизы мак.;У кого роза?;коли|коля|у коли;roza\n" +
            "g1q2_34;1;2;whose;У Димы шар. У Оли мяч.;У кого шар?;димы|дима|у димы;shar\n" +
            "g1q2_35;1;2;whose;У Пети суп. У Тани чай.;У кого суп?;пети|петя|у пети;sup\n" +
            "g1q2_36;1;2;whose;У Нины книга. У Ромы зонт.;У кого книга?;нины|нина|у нины;\n" +
            "g1q2_37;1;2;whose;У Веры кукла. У Милы миска.;У кого кукла?;веры|вера|у веры;\n" +
            "g1q2_38;1;2;whose;У Лизы сани. У Коли лыжи.;У кого лыжи?;коли|коля|у коли;\n" +
            "g1q2_39;1;2;whose;У Оли банан. У Димы груша.;У кого банан?;оли|оля|у оли;\n" +
            "g1q2_40;1;2;whose;У Тани шапка. У Пети сумка.;У кого сумка?;пети|петя|у пети;\n" +
            "g1q2_41;1;2;which;На торте белый крем.;Какой крем на торте?;белый;tort\n" +
            "g1q2_42;1;2;which;Белый зонт упал. Синий зонт висит.;Какой зонт упал?;белый;zont\n" +
            "g1q2_43;1;2;which;Серый слон идёт. Белый слон спит.;Какой слон спит?;белый;slon\n" +
            "g1q2_44;1;2;which;Коза ест белый хлеб.;Какой хлеб ест коза?;белый;koza\n" +
            "g1q2_45;1;2;which;У куклы новый дом.;Какой дом у куклы?;новый;kukla\n" +
            "g1q2_46;1;2;which;Синий мяч у Нины. Белый мяч у Оли.;Какой мяч у Нины?;синий;\n" +
            "g1q2_47;1;2;which;Белый шар у Коли. Синий шар у Пети.;Какой шар у Пети?;синий;\n" +
            "g1q2_48;1;2;which;Рыжий кот ест. Серый кот спит.;Какой кот ест?;рыжий;\n" +
            "g1q2_49;1;2;which;Новый дом у реки. Белый дом у сада.;Какой дом у реки?;новый;\n" +
            "g1q2_50;1;2;which;Тупой нож у папы. Новый нож у мамы.;Какой нож у папы?;тупой;\n" +
            "g1q2_51;1;2;then;Рома ел суп. Потом мыл миску.;Что мыл Рома потом?;миску|миска;miska\n" +
            "g1q2_52;1;2;then;Нина несла розу. Потом взяла вазу.;Что взяла Нина потом?;вазу|ваза;vaza\n" +
            "g1q2_53;1;2;then;Папа взял нож. Потом взял пилу.;Что взял папа потом?;пилу|пила;pila\n" +
            "g1q2_54;1;2;then;Коля взял вёсла. Потом мыл лодку.;Что мыл Коля потом?;лодку|лодка;lodka\n" +
            "g1q2_55;1;2;then;Луна вышла. Рома уснул.;Кто уснул потом?;рома;luna\n" +
            "g1q2_56;1;2;then;Мила ела кашу. Потом пила сок.;Что пила Мила потом?;сок;\n" +
            "g1q2_57;1;2;then;Вера мыла пол. Потом мыла раму.;Что мыла Вера потом?;раму|рама;\n" +
            "g1q2_58;1;2;then;Дима мыл лук. Потом резал сыр.;Что резал Дима потом?;сыр;\n" +
            "g1q2_59;1;2;then;Оля пила чай. Потом ела торт.;Что ела Оля потом?;торт;\n" +
            "g1q2_60;1;2;then;Таня мыла руки. Потом брала хлеб.;Что брала Таня потом?;хлеб;\n";

        private static readonly Dictionary<string, string[]> current = Rows(DefaultReadingData.Text);
        private static readonly Dictionary<string, string[]> upgrades = CreateUpgrades();
        private static readonly HashSet<string> shuffleable = CreateShuffleable();

        internal static string[] Upgrade(string[] fields)
        {
            string[] replacement;
            return upgrades.TryGetValue(Key(fields), out replacement) ? (string[])replacement.Clone() : fields;
        }

        internal static bool CanShuffle(string[] fields)
        {
            return shuffleable.Contains(Key(fields));
        }

        private static Dictionary<string, string[]> Rows(string text)
        {
            Dictionary<string, string[]> result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            using (StringReader reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] fields = line.Split(';');
                    if (fields.Length >= 7 && fields[0].StartsWith("g1q2_", StringComparison.Ordinal) &&
                        fields[1] == "1" && fields[2] == "2") result.Add(fields[0], fields);
                }
            }
            return result;
        }

        private static Dictionary<string, string[]> CreateUpgrades()
        {
            Dictionary<string, string[]> result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> old in Rows(PreviousSecondQuarter))
            {
                string[] replacement;
                if (!current.TryGetValue(old.Key, out replacement)) continue;
                result[Key(old.Value)] = replacement;
                // 1.6.0/1.6.1 shipped the same cards without phrase answers.
                result[Key(WithoutPhrases(old.Value))] = replacement;
            }
            foreach (string[] row in current.Values) result[Key(WithoutPhrases(row))] = row;
            return result;
        }

        private static string[] WithoutPhrases(string[] fields)
        {
            string[] result = (string[])fields.Clone();
            if (result[3] == "where" || result[3] == "whose")
                result[6] = String.Join("|", Array.FindAll(result[6].Split('|'),
                    delegate(string answer) { return answer.IndexOf(' ') < 0; }));
            return result;
        }

        private static HashSet<string> CreateShuffleable()
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string[] row in current.Values)
                if (row[3] == "who" || row[3] == "what" || row[3] == "where" ||
                    row[3] == "whose" || row[3] == "which") result.Add(Key(row));
            return result;
        }

        private static string Key(string[] fields)
        {
            return String.Join("\u001f", new string[] {
                fields[0].Trim(), fields[1].Trim(), fields[2].Trim(), fields[3].Trim(),
                fields[4].Trim(), fields[5].Trim(), fields[6].Trim(), fields.Length == 8 ? fields[7].Trim() : "" });
        }
    }

    internal static class ReadingSentenceOrder
    {
        internal static GradedCard Shuffle(GradedCard card, Random random)
        {
            if (!card.ShuffleReading) return card;
            string[] sentences = card.Text.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (sentences.Length != 3) return card;
            for (int i = 0; i < sentences.Length; i++) sentences[i] = sentences[i].Trim() + ".";
            for (int i = sentences.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                string saved = sentences[i]; sentences[i] = sentences[j]; sentences[j] = saved;
            }
            return new GradedCard(card.Type, card.Instruction, String.Join(" ", sentences), card.Question,
                card.Answers, card.Numeric, card.Picture, true);
        }
    }
}
