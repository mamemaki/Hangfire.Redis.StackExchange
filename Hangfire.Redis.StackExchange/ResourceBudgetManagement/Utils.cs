using System;
using System.Collections.Generic;
using System.Text;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    internal class Utils
    {
        /// <summary>
        /// Pase string to cpu milliseconds
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        /// <exception cref="FormatException"></exception>
        public static int ConvertStringToCpuMilliseconds(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return 0;

            s = s.Trim();

            var found = false;
            int sepPos;
            for (sepPos = 0; sepPos < s.Length; sepPos++)
                if (!(s[sepPos] >= '0' && s[sepPos] <= '9' || s[sepPos] == '.'))
                {
                    found = true;
                    break;
                }
            string numberPart;
            string sizePart;
            if (found)
            {
                numberPart = s.Substring(0, sepPos).Trim();
                sizePart = s.Substring(sepPos, s.Length - sepPos).Trim();
            }
            else
            {
                numberPart = s.Trim();
                sizePart = "";
            }


            if (!double.TryParse(numberPart, out var number))
                throw new FormatException($"No number found in value '{s}'.");

            switch (sizePart)
            {
                case "m":
                    return (int)number;
                case "":
                    return (int)(number * 1000);
                default:
                    throw new FormatException($"Unknown size part '{sizePart}'.");
            }
        }

        /// <summary>
        /// Pase string to memory bytes(0 to long.MaxValue)
        /// </summary>
        /// <param name="s"></param>
        /// <returns>0 to long.MaxValue</returns>
        /// <exception cref="FormatException"></exception>
        public static long ConvertStringToMemoryBytes(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return 0;

            s = s.Trim();

            var found = false;
            int sepPos;
            for (sepPos = 0; sepPos < s.Length; sepPos++)
                if (!(s[sepPos] >= '0' && s[sepPos] <= '9'))
                {
                    found = true;
                    break;
                }

            string numberPart;
            string sizePart;
            if (found)
            {
                numberPart = s.Substring(0, sepPos).Trim();
                sizePart = s.Substring(sepPos, s.Length - sepPos).Trim();
            }
            else
            {
                numberPart = s.Trim();
                sizePart = "";
            }

            if (!long.TryParse(numberPart, out var number))
                throw new FormatException($"No number found in value '{s}'.");

            switch (sizePart)
            {
                case "T":
                    return number * 1000 * 1000 * 1000 * 1000;
                case "G":
                    return number * 1000 * 1000 * 1000;
                case "M":
                    return number * 1000 * 1000;
                case "K":
                    return number * 1000;
                case "Ti":
                    return number * 1024 * 1024 * 1024 * 1024;
                case "Gi":
                    return number * 1024 * 1024 * 1024;
                case "Mi":
                    return number * 1024 * 1024;
                case "Ki":
                    return number * 1024;
                case "":
                    return number;
                default:
                    throw new FormatException($"Unknown size part '{sizePart}'.");
            }
        }
    }
}
