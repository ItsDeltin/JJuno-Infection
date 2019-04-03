using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JjunoInfection
{
    static class Helpers
    {
        public static string CommaSeperate(List<string> values)
        {
            if (values.Count == 0)
                return string.Empty;

            if (values.Count == 1)
                return values[0];

            string last = values[values.Count - 1];

            values.RemoveAt(values.Count - 1);

            return $"{string.Join(", ", values)} and {last}";
        }

        public static string FirstLetterToUpperCase(string s)
        {
            if (string.IsNullOrEmpty(s))
                throw new ArgumentException("There is no first letter");

            char[] a = s.ToLower().ToCharArray();
            a[0] = char.ToUpper(a[0]);
            return new string(a);
        }
    }
}
