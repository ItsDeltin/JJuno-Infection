using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JjunoInfection
{
    static class Helpers
    {
        public static string CommaSeperate(string[] values)
        {
            if (values.Length == 0)
                return string.Empty;

            if (values.Length == 1)
                return values[0];

            string last = values[values.Length - 1];

            return $"{string.Join(", ", values, 0, values.Length - 1)} and {last}";
        }

        public static string FirstLetterToUpperCase(string s)
        {
            if (string.IsNullOrEmpty(s))
                throw new ArgumentException("There is no first letter");

            char[] a = s.ToLower().ToCharArray();
            a[0] = char.ToUpper(a[0]);
            return new string(a);
        }

        // Makes each line of text a new line for Overwatch.
        public static string FormatMessage(params string[] text)
        {
            string newLine = string.Concat(Enumerable.Repeat("\u3000", 30));

            string result = "";
            for (int i = 0; i < text.Length; i++)
            {
                if (i < text.Length - 1)
                    result += text[i] + " " + (text[i].Length * 0.80 < newLine.Length ? newLine.Substring((int)(text[i].Length * 0.80)) : "");
                else
                    result += text[i];
            }

            return result;
        }

    }
}
