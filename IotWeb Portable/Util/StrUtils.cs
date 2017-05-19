using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IotWeb.Common.Util
{
    internal sealed class StrUtils
    {
        StrUtils() { }

        public static bool StartsWith(string str1, string str2)
        {
            return StartsWith(str1, str2, false);
        }

        public static bool StartsWith(string str1, string str2, bool ignore_case)
        {
            int l2 = str2.Length;
            if (l2 == 0)
                return true;

            int l1 = str1.Length;
            if (l2 > l1)
                return false;

            return (0 == String.Compare(str1, 0, str2, 0, l2, StringComparison.CurrentCultureIgnoreCase));
        }

        public static bool EndsWith(string str1, string str2)
        {
            return EndsWith(str1, str2, false);
        }

        public static bool EndsWith(string str1, string str2, bool ignore_case)
        {
            int l2 = str2.Length;
            if (l2 == 0)
                return true;

            int l1 = str1.Length;
            if (l2 > l1)
                return false;

            return (0 == String.Compare(str1, l1 - l2, str2, 0, l2, StringComparison.CurrentCultureIgnoreCase));
        }

        public static string EscapeQuotesAndBackslashes(string attributeValue)
        {
            StringBuilder sb = null;
            for (int i = 0; i < attributeValue.Length; i++)
            {
                char ch = attributeValue[i];
                if (ch == '\'' || ch == '"' || ch == '\\')
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder();
                        sb.Append(attributeValue.Substring(0, i));
                    }
                    sb.Append('\\');
                    sb.Append(ch);
                }
                else
                {
                    if (sb != null)
                        sb.Append(ch);
                }
            }
            if (sb != null)
                return sb.ToString();
            return attributeValue;
        }

        public static bool IsNullOrEmpty(string value)
        {
#if NET_2_0
			return String.IsNullOrEmpty (value);
#else
            return value == null || value.Length == 0;
#endif
        }

        public static string[] SplitRemoveEmptyEntries(string value, char[] separator)
        {
#if NET_2_0
			return value.Split (separator, StringSplitOptions.RemoveEmptyEntries);
#else
            string[] parts = value.Split(separator);
            int delta = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (IsNullOrEmpty(parts[i]))
                {
                    delta++;
                }
                else
                {
                    if (delta > 0)
                        parts[i - delta] = parts[i];
                }
            }
            if (delta == 0)
                return parts;

            string[] parts_copy = new string[parts.Length - delta];
            Array.Copy(parts, parts_copy, parts_copy.Length);
            return parts_copy;
#endif
        }
    }
}
