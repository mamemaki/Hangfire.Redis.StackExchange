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
using System.Collections.Generic;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    public class ResourceLimitType_Cpu : ResourceLimitType
    {
        /// <summary>
        /// The resource usage limit. e.g. 800m
        /// </summary>
        public string Limit { get; set; }

        /// <summary>
        /// The default resource usage limit.
        /// </summary>
        public string DefaultLimit { get; } = Environment.ProcessorCount.ToString();

        /// <summary>
        /// The default resource request value for each job.
        /// </summary>
        public string DefaultRequest { get; set; }

        private const string ResourceRequestPropName = "Cpu";

        public override DeterminationResult IsUsageLimitReached(
            List<JobResourceRequests> fetchedJobReqs, JobResourceRequests newJobReq = null)
        {
            var limit = Limit ?? DefaultLimit;
            var resUsage = GetFetchedJobsResourceUsage(fetchedJobReqs);
            var newJobResReq = 0L;
            if (newJobReq != null)
                newJobResReq = GetJobResourceUsage(newJobReq);
            var ret = new DeterminationResult();
            ret.LimitReached = (resUsage + newJobResReq >= ConvertStringToCpuMilliseconds(limit));
            ret.Limit = limit;
            ret.Context = new Dictionary<string, string>()
            {
                { "resUsage", resUsage.ToString() },
                { "newJobResReq", newJobResReq.ToString() },
            };
            return ret;
        }

        private long GetJobResourceUsage(JobResourceRequests jobReq)
        {
            var resVal = jobReq.ResourceRequests?.GetValueOrDefault(ResourceRequestPropName);
            if (resVal != null)
                return ConvertStringToCpuMilliseconds(resVal);
            return ConvertStringToCpuMilliseconds(DefaultRequest);
        }

        private long GetFetchedJobsResourceUsage(List<JobResourceRequests> fetchedJobReqs)
        {
            var totalResUsage = 0L;
            foreach (var jobReq in fetchedJobReqs)
            {
                totalResUsage += GetJobResourceUsage(jobReq);
            }
            return totalResUsage;
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
