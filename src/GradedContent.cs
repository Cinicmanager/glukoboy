using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Bubliklyuk
{
    // Каталог второго задания по русскому и математические шаблоны v5.
    // Чтение хранится в DefaultReadingData; эти задания формируются локально.
    internal sealed class GradedCard
    {
        internal readonly string Type;
        internal readonly string Instruction;
        internal readonly string Text;
        internal readonly string Question;
        internal readonly string Answers;
        internal readonly bool Numeric;

        internal GradedCard(string type, string instruction, string text, string question,
            string answers, bool numeric)
        {
            Type = type; Instruction = instruction; Text = text;
            Question = question; Answers = answers; Numeric = numeric;
        }

        internal string Key { get { return Type + "|" + Text + "|" + Question; } }
    }

    internal static class GradedContent
    {
        // grade;level;слово;слоги. Каждый уровень имеет свой список.
        private const string SyllableData =
            "1;1;мама;ма + ма\n1;1;папа;па + па\n1;1;рыба;ры + ба\n" +
            "1;1;коза;ко + за\n1;1;лиса;ли + са\n1;1;рука;ру + ка\n" +
            "1;1;луна;лу + на\n1;1;рама;ра + ма\n1;1;сани;са + ни\n" +
            "1;1;нора;но + ра\n1;1;мыло;мы + ло\n1;1;каша;ка + ша\n" +
            "1;1;роза;ро + за\n1;1;зима;зи + ма\n1;1;лето;ле + то\n1;1;пила;пи + ла\n" +
            "1;2;молодец;мо + ло + дец\n1;2;собака;со + ба + ка\n" +
            "1;2;барабан;ба + ра + бан\n1;2;молоко;мо + ло + ко\n" +
            "1;2;ворона;во + ро + на\n1;2;корова;ко + ро + ва\n" +
            "1;2;сорока;со + ро + ка\n1;2;машина;ма + ши + на\n" +
            "1;2;берёза;бе + рё + за\n1;2;малина;ма + ли + на\n" +
            "1;2;ракета;ра + ке + та\n1;2;лопата;ло + па + та\n" +
            "1;2;осина;о + си + на\n1;2;самолёт;са + мо + лёт\n" +
            "1;2;петушок;пе + ту + шок\n1;2;пироги;пи + ро + ги\n" +
            "1;3;молодец;мо + ло + дец\n1;3;собака;со + ба + ка\n" +
            "1;3;барабан;ба + ра + бан\n1;3;молоко;мо + ло + ко\n" +
            "1;3;ворона;во + ро + на\n1;3;корова;ко + ро + ва\n" +
            "1;3;сорока;со + ро + ка\n1;3;машина;ма + ши + на\n" +
            "1;3;берёза;бе + рё + за\n1;3;малина;ма + ли + на\n" +
            "1;3;ракета;ра + ке + та\n1;3;лопата;ло + па + та\n" +
            "1;3;осина;о + си + на\n1;3;самолёт;са + мо + лёт\n" +
            "1;3;петушок;пе + ту + шок\n1;3;пироги;пи + ро + ги\n" +
            "1;3;черепаха;че + ре + па + ха\n1;3;паутина;па + у + ти + на\n" +
            "1;3;кукуруза;ку + ку + ру + за\n1;3;балерина;ба + ле + ри + на\n" +
            "1;3;пирамида;пи + ра + ми + да\n1;3;рукавица;ру + ка + ви + ца\n" +
            "1;3;гусеница;гу + се + ни + ца\n1;3;карусели;ка + ру + се + ли\n" +
            "2;1;берёза;бе + рё + за\n2;1;машина;ма + ши + на\n" +
            "2;1;самолёт;са + мо + лёт\n2;1;молодец;мо + ло + дец\n" +
            "2;1;черепаха;че + ре + па + ха\n2;1;кукуруза;ку + ку + ру + за\n" +
            "2;1;пирамида;пи + ра + ми + да\n2;1;рукавица;ру + ка + ви + ца\n" +
            "2;1;гусеница;гу + се + ни + ца\n2;1;балерина;ба + ле + ри + на\n" +
            "2;2;черепаха;че + ре + па + ха\n2;2;кукуруза;ку + ку + ру + за\n" +
            "2;2;пирамида;пи + ра + ми + да\n2;2;рукавица;ру + ка + ви + ца\n" +
            "2;2;гусеница;гу + се + ни + ца\n2;2;балерина;ба + ле + ри + на\n" +
            "2;2;карусели;ка + ру + се + ли\n2;2;паутина;па + у + ти + на\n" +
            "2;2;велосипед;ве + ло + си + пед\n2;2;батарея;ба + та + ре + я\n" +
            "2;3;черепаха;че + ре + па + ха\n2;3;кукуруза;ку + ку + ру + за\n" +
            "2;3;пирамида;пи + ра + ми + да\n2;3;рукавица;ру + ка + ви + ца\n" +
            "2;3;гусеница;гу + се + ни + ца\n2;3;балерина;ба + ле + ри + на\n" +
            "2;3;карусели;ка + ру + се + ли\n2;3;паутина;па + у + ти + на\n" +
            "2;3;велосипед;ве + ло + си + пед\n2;3;батарея;ба + та + ре + я\n" +
            "3;1;велосипед;ве + ло + си + пед\n3;1;учительница;у + чи + тель + ни + ца\n" +
            "3;1;математика;ма + те + ма + ти + ка\n3;1;аквариум;а + ква + ри + ум\n" +
            "3;1;библиотека;би + бли + о + те + ка\n3;1;мороженое;мо + ро + же + но + е\n" +
            "3;1;телевизор;те + ле + ви + зор\n3;1;огородник;о + го + род + ник\n" +
            "3;2;математика;ма + те + ма + ти + ка\n3;2;библиотека;би + бли + о + те + ка\n" +
            "3;2;учительница;у + чи + тель + ни + ца\n3;2;мороженое;мо + ро + же + но + е\n" +
            "3;2;телевизор;те + ле + ви + зор\n3;2;путешествие;пу + те + ше + стви + е\n" +
            "3;2;объявление;объ + яв + ле + ни + е\n3;2;удивление;у + ди + вле + ни + е\n" +
            "3;3;математика;ма + те + ма + ти + ка\n3;3;библиотека;би + бли + о + те + ка\n" +
            "3;3;учительница;у + чи + тель + ни + ца\n3;3;мороженое;мо + ро + же + но + е\n" +
            "3;3;путешествие;пу + те + ше + стви + е\n3;3;объявление;объ + яв + ле + ни + е\n" +
            "3;3;удивление;у + ди + вле + ни + е\n3;3;водопроводчик;во + до + про + вод + чик\n";

        // Семантическая категория названа явно: ответ остаётся однозначным.
        private const string SemanticOdd =
            "Найди слово, которое не называет животное.;кот, пёс, стол, мышь;стол\n" +
            "Найди слово, которое не называет птицу.;утка, гусь, щука, сова;щука\n" +
            "Найди слово, которое не называет фрукт.;груша, яблоко, морковь, слива;морковь\n" +
            "Найди слово, которое не называет овощ.;капуста, огурец, яблоко, свёкла;яблоко\n" +
            "Найди слово, которое не называет мебель.;шкаф, диван, ложка, стол;ложка\n" +
            "Найди слово, которое не называет посуду.;чашка, тарелка, кастрюля, шапка;шапка\n" +
            "Найди слово, которое не называет одежду.;куртка, платье, рубашка, мяч;мяч\n" +
            "Найди слово, которое не называет дерево.;берёза, дуб, сосна, ромашка;ромашка\n" +
            "Найди слово, которое не называет транспорт.;автобус, поезд, велосипед, карандаш;карандаш\n" +
            "Найди слово, которое не называет инструмент.;пила, молоток, дрель, тарелка;тарелка\n";
        private const string GrammarOdd =
            "Найди слово с другим корнем.;лес, лесной, лесник, река;река\n" +
            "Найди слово с другим корнем.;сад, садик, садовый, стол;стол\n" +
            "Найди слово с другим корнем.;зима, зимний, зимовать, лето;лето\n" +
            "Найди слово с другим корнем.;снег, снежок, снежный, дождь;дождь\n" +
            "Найди слово с другим корнем.;море, морской, моряк, гора;гора\n" +
            "Найди слово с другим корнем.;вода, водичка, водный, огонь;огонь\n" +
            "Найди слово, которое не называет действие.;бежит, читает, рисует, зелёный;зелёный|зеленый\n" +
            "Найди слово, которое не называет предмет.;стол, книга, река, весёлый;весёлый|веселый\n" +
            "Найди слово, которое не называет признак.;красный, добрый, быстрый, бежит;бежит\n" +
            "Найди слово, которое не называет действие.;играет, плывёт, поёт, песня;песня\n" +
            "Найди слово, которое не называет предмет.;школа, окно, дерево, читает;читает\n" +
            "Найди слово, которое не называет признак.;синий, тёплый, мягкий, прыгает;прыгает\n";
        private const string CountWords =
            "кошка|собака|берёза|машина|молоко|пальто|чайка|подъезд|ёлка|солнце|" +
            "черепаха|велосипед|паутина|кукуруза|библиотека|математика|" +
            "учительница|мороженое|объявление|путешествие";

        internal static List<GradedCard> Russian(int grade, int level, string type)
        {
            List<GradedCard> result = new List<GradedCard>();
            if (type == "syll")
            {
                foreach (string line in SyllableData.Split('\n'))
                {
                    if (line.Length == 0) continue;
                    string[] p = line.Split(';');
                    if (Int32.Parse(p[0], CultureInfo.InvariantCulture) != grade ||
                        Int32.Parse(p[1], CultureInfo.InvariantCulture) != level) continue;
                    result.Add(new GradedCard("syll", "Собери слово", p[3], "", p[2], false));
                }
            }
            else if (type == "odd" && grade >= 2)
            {
                string data = grade == 2 ? SemanticOdd : GrammarOdd;
                foreach (string line in data.Split('\n'))
                {
                    if (line.Length == 0) continue;
                    string[] p = line.Split(';');
                    result.Add(new GradedCard("odd", "Какое слово лишнее?", p[1], p[0], p[2], false));
                }
            }
            else if (type == "count" && grade == 3)
            {
                foreach (string word in CountWords.Split('|'))
                {
                    int count = 0;
                    foreach (char letter in word)
                        if ("аеёиоуыэюя".IndexOf(letter) >= 0) count++;
                    result.Add(new GradedCard("count", "Сколько слогов?", word, "",
                        count.ToString(CultureInfo.InvariantCulture), true));
                }
            }
            return result;
        }

        internal static GradedCard Math(int grade, int level, int mathMax, Random random)
        {
            if (grade == 1) return FirstGradeMath(level, mathMax, random);
            if (grade == 2)
            {
                if (level == 1)
                {
                    int a, b, answer;
                    bool plus = random.Next(2) == 0;
                    if (plus)
                    {
                        a = random.Next(3, 10);
                        b = random.Next(11 - a, 10);
                        answer = a + b;
                    }
                    else
                    {
                        a = random.Next(11, 19);
                        b = random.Next(a - 9, 10);
                        answer = a - b;
                    }
                    return Arithmetic(a, b, plus ? "+" : "−", answer);
                }
                if (level == 2)
                {
                    int a, b;
                    bool plus = random.Next(2) == 0;
                    if (random.Next(2) == 0)
                    {
                        a = 10 * random.Next(1, 10);
                        b = 10 * random.Next(1, plus ? (100 - a) / 10 + 1 : a / 10 + 1);
                    }
                    else
                    {
                        a = random.Next(11, 100);
                        b = random.Next(1, plus ? MathMin(9, 100 - a) + 1 : 10);
                    }
                    return Arithmetic(a, b, plus ? "+" : "−", plus ? a + b : a - b);
                }
                if (random.Next(3) == 0)
                {
                    int a = random.Next(2, 10), b = random.Next(2, 4);
                    if (random.Next(3) == 0) return GroupsStory(a, b);
                    return Arithmetic(a, b, "×", a * b);
                }
                else
                {
                    bool plus = random.Next(2) == 0;
                    int a = random.Next(20, plus ? 90 : 100);
                    int b = random.Next(10, plus ? 101 - a : a + 1);
                    int answer = plus ? a + b : a - b;
                    if (random.Next(3) == 0)
                        return NumericStory("У Лены " + (a % 10 == 1 && a % 100 != 11 ? "была " : "было ") +
                            a + " " + Form(a, "наклейка", "наклейки", "наклеек") + ". " +
                            (plus ? "Ей подарили ещё " : "Она подарила подруге ") + b + ".",
                            "Сколько наклеек стало у Лены?", answer);
                    return Arithmetic(a, b, plus ? "+" : "−", answer);
                }
            }
            if (level <= 2)
            {
                if (level == 2 && random.Next(3) == 0)
                {
                    bool plus = random.Next(2) == 0;
                    int a = random.Next(10, 91);
                    int b = random.Next(1, plus ? 101 - a : a + 1);
                    return Arithmetic(a, b, plus ? "+" : "−", plus ? a + b : a - b);
                }
                int factor = random.Next(2, level == 1 ? 6 : 10);
                int other = random.Next(2, 10);
                if (random.Next(2) == 0)
                    return Arithmetic(factor * other, factor, ":", other);
                return Arithmetic(other, factor, "×", other * factor);
            }
            int kind = random.Next(4);
            if (kind == 0)
            {
                int a = random.Next(11, 100), b = random.Next(2, 10);
                return Arithmetic(a, b, "×", a * b);
            }
            if (kind == 1)
            {
                int groups = random.Next(2, 10);
                int each = random.Next(3, 13);
                int taken = random.Next(1, each);
                return NumericStory("В " + groups + " коробках по " + each +
                    " " + Form(each, "мелок", "мелка", "мелков") + ". Дети взяли " + taken +
                    " " + Form(taken, "мелок", "мелка", "мелков") + ".",
                    "Сколько мелков осталось?", groups * each - taken);
            }
            if (kind == 2)
            {
                int first = 10 * random.Next(11, 40);
                int second = 10 * random.Next(2, 20);
                int spent = 10 * random.Next(1, 11);
                return NumericStory("В магазин привезли " + first + " " +
                    Form(first, "тетрадь", "тетради", "тетрадей") + ", затем ещё " +
                    second + ". Продали " + spent + " " + Form(spent, "тетрадь", "тетради", "тетрадей") + ".",
                    "Сколько тетрадей осталось?", first + second - spent);
            }
            int unit = random.Next(2) == 0 ? 10 : 100;
            bool add = random.Next(2) == 0;
            int left = unit * random.Next(2, 1000 / unit);
            int right = unit * random.Next(1, add ? (1000 - left) / unit + 1 : left / unit + 1);
            return Arithmetic(left, right, add ? "+" : "−", add ? left + right : left - right);
        }

        private static int MathMin(int a, int b) { return a < b ? a : b; }

        private static GradedCard FirstGradeMath(int level, int maximum, Random random)
        {
            maximum = System.Math.Max(3, System.Math.Min(10, maximum));
            int step = random.Next(1, 4);
            bool plus = random.Next(2) == 0;
            int a = plus ? random.Next(0, maximum - step + 1) : random.Next(step, maximum + 1);
            int answer = plus ? a + step : a - step;
            if (level >= 2 && random.Next(4) == 0)
            {
                // Числительные с окончаниями позволяют читать задачу без догадок.
                if (plus)
                    return NumericStory("У Веры " + a + " " + Form(a, "мяч", "мяча", "мячей") +
                        ". Ей дали ещё " + step + ".", "Сколько мячей стало?", answer);
                return NumericStory("У Веры " + a + " " + Form(a, "мяч", "мяча", "мячей") +
                    ". Она отдала " + step + ".", "Сколько мячей осталось?", answer);
            }
            string sign = plus ? "+" : "−";
            int variant = random.Next(3);
            if (variant == 1)
                return Number("Какое число пропущено?", a + " " + sign + " □ = " + answer, step);
            if (variant == 2)
                return Number("Какое число пропущено?", "□ " + sign + " " + step + " = " + answer, a);
            return Arithmetic(a, step, sign, answer);
        }

        private static GradedCard GroupsStory(int each, int count)
        {
            return NumericStory("На " + count + " тарелках по " + each + " " +
                Form(each, "яблоко", "яблока", "яблок") + ".",
                "Сколько всего яблок?", each * count);
        }

        private static GradedCard Arithmetic(int a, int b, string sign, int answer)
        {
            return Number("Реши пример", a + " " + sign + " " + b + " = □", answer);
        }

        private static GradedCard Number(string instruction, string text, int answer)
        {
            return new GradedCard("math", instruction, text, "",
                answer.ToString(CultureInfo.InvariantCulture), true);
        }

        private static GradedCard NumericStory(string text, string question, int answer)
        {
            return new GradedCard("math", "Прочитай и реши", text, question,
                answer.ToString(CultureInfo.InvariantCulture), true);
        }

        private static string Form(int number, string one, string few, string many)
        {
            if (number % 100 >= 11 && number % 100 <= 14) return many;
            if (number % 10 == 1) return one;
            if (number % 10 >= 2 && number % 10 <= 4) return few;
            return many;
        }
    }
}
