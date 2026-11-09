using System;

namespace Bubliklyuk
{
    // Адаптер к полному банку v5. Старые строки из шести полей поддерживает загрузчик.
    internal sealed class ReadingBank
    {
        internal readonly GradedReadingBank Data;
        internal string Warning { get { return Data.Warning; } }
        internal const string DefaultText = DefaultReadingData.Text;

        private ReadingBank(GradedReadingBank data) { Data = data; }
        internal static ReadingBank Load() { return new ReadingBank(GradedReadingBank.Load()); }
    }
}
