using System;
using System.Collections.Generic;
using System.Text;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    internal class Utils
    {
        /// <summary>
        /// Pase string to cpu milliseconds(0 to 1000)
        /// </summary>
        /// <param name="s"></param>
        /// <returns>0 to 1000</returns>
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
            if (found == false)
                throw new FormatException($"No separate position found in value '{s}'.");

            string numberPart = s.Substring(0, sepPos).Trim();
            string sizePart = s.Substring(sepPos, s.Length - sepPos).Trim();

            if (!double.TryParse(numberPart, out var number))
                throw new FormatException($"No number found in value '{s}'.");

            switch (sizePart)
            {
                case "m":
                    // Allowed range: 0 to 1000
                    if (number > 1000)
                        throw new FormatException($"The number must be less than 1000 '{numberPart}'.");
                    return Math.Min((int)number, 100);
                case "":
                    // Allowed range: 0.001 to 1
                    if (number > 1)
                        throw new FormatException($"The number must be less than 1.0 '{numberPart}'.");
                    return (int)(number * 100);
                default:
                    throw new FormatException($"Unknown size part '{sizePart}'.");
            }
        }

        /// <summary>
        /// Pase string to memory bytes(0 to int.MaxValue)
        /// </summary>
        /// <param name="s"></param>
        /// <returns>0 to int.MaxValue</returns>
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
            if (found == false)
                throw new FormatException($"No separate position found in value '{s}'.");

            string numberPart = s.Substring(0, sepPos).Trim();
            string sizePart = s.Substring(sepPos, s.Length - sepPos).Trim();

            if (!long.TryParse(numberPart, out var number))
                throw new FormatException($"No number found in value '{s}'.");

            if (number > long.MaxValue)
                throw new FormatException($"The number must be less than {long.MaxValue} '{numberPart}'.");

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
