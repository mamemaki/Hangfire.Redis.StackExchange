// Copyright © 2013-2015 Sergey Odinokov, Marco Casamento
// This software is based on https://github.com/HangfireIO/Hangfire.Redis

// Hangfire.Redis.StackExchange is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as
// published by the Free Software Foundation, either version 3
// of the License, or any later version.
//
// Hangfire.Redis.StackExchange is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with Hangfire.Redis.StackExchange. If not, see <http://www.gnu.org/licenses/>.

using System;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    public class ResourceLimitType_Cpu : ResourceLimitType
    {
        public override string DefaultLimit { get; } = Environment.ProcessorCount.ToString();

        public override string TypeName => "Cpu";

        public override long DeserializeResourceLimitValue(string val)
        {
            return ConvertStringToCpuMilliseconds(val);
        }

        /// <summary>
        /// Pase string to cpu milliseconds
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        /// <exception cref="FormatException"></exception>
        internal static int ConvertStringToCpuMilliseconds(string s)
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
    }
}
