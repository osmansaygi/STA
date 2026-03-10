using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ST4AksCizCSharp
{
    public static class St4Text
    {
        public static List<string> SplitCsv(string line)
        {
            return (line ?? string.Empty).Split(',').Select(x => x.Trim()).ToList();
        }

        public static bool TryParseInt(string s, out int value)
        {
            return int.TryParse((s ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryParseDouble(string s, out double value)
        {
            return double.TryParse((s ?? string.Empty).Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
        }
    }
}
