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

using Hangfire.Common;
using System;
using System.Collections.Generic;

namespace Hangfire.Redis.StackExchange
{
    internal class ResourceBudgetManager
    {
        class QueueConfig
        {
            public int CpuRequest { get; set; }
            public long MemoryRequest { get; set; }
            public bool FetchIndividualCpuRequests { get; set; }
            public bool FetchIndividualMemoryRequests { get; set; }
        }

        private readonly IList<RedisFetchedJob> _fetchedJobs;
        private readonly int _cpuLimit;
        private readonly long _memoryLimit;
        private readonly IDictionary<string, QueueConfig> _queueConfigDict;

        public ResourceBudgetManager(RedisStorageOptions options)
        {
            _fetchedJobs = new List<RedisFetchedJob>();

            _cpuLimit = Environment.ProcessorCount * 1000;
            if (options.CpuLimit != null)
                _cpuLimit = ConvertStringToCpuMilliseconds(options.CpuLimit);

            if (options.MemoryLimit != null)
            {
                _memoryLimit = ConvertStringToMemoryBytes(options.MemoryLimit);
            }
            else
            {
                //TODO: Replace to 'GC.GetGCMemoryInfo().TotalAvailableMemoryBytes' when we upgrade to netstandard3.0
                _memoryLimit = 10L * 1024 * 1024 * 1024;    // 10Gi
            }

            _queueConfigDict = new Dictionary<string, QueueConfig>();
            foreach (var queueOption in options.QueueOptions)
            {
                _queueConfigDict[queueOption.QueueName] = new QueueConfig()
                {
                    CpuRequest = ConvertStringToCpuMilliseconds(queueOption.CpuRequest),
                    MemoryRequest = ConvertStringToMemoryBytes(queueOption.MemoryRequest),
                    FetchIndividualCpuRequests = queueOption.FetchIndividualCpuRequests,
                    FetchIndividualMemoryRequests = queueOption.FetchIndividualMemoryRequests,
                };
            }
        }

        internal IList<RedisFetchedJob> FetchedJobs => _fetchedJobs;
        internal int CpuLimit => _cpuLimit;
        internal long MemoryLimit => _memoryLimit;

        /// <summary>
        /// Pase string to cpu milliseconds(0 to 1000)
        /// </summary>
        /// <param name="s"></param>
        /// <returns>0 to 1000</returns>
        /// <exception cref="FormatException"></exception>
        private int ConvertStringToCpuMilliseconds(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return 0;

            s = s.Trim();

            var found = false;
            int sepPos;
            for (sepPos = 0; sepPos < s.Length; sepPos++)
                if (!((s[sepPos] >= '0' && s[sepPos] <= '9') || s[sepPos] == '.'))
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
        private long ConvertStringToMemoryBytes(string s)
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

        public void RemoveFetchedJob(RedisFetchedJob fetchedJob)
        {
            lock (this)
                _fetchedJobs.Remove(fetchedJob);
        }

        public void AddFetchedJob(RedisFetchedJob fetchedJob)
        {
            _fetchedJobs.Add(fetchedJob);
        }

        /// <summary>
        /// Get whether resource usage limit reached or not
        /// </summary>
        /// <returns>Return true if the current resource usage reaches limit, otherwise return false</returns>
        public bool IsUsageLimitReached(RedisConnection connection)
        {
            var cpuUsage = GetCurrentCpuUsage(connection);
            if (cpuUsage >= _cpuLimit)
                return true;

            var memoryUsage = GetCurrentMemoryUsage(connection);
            if (memoryUsage >= _memoryLimit)
                return true;

            return false;
        }

        private int GetCurrentCpuUsage(RedisConnection connection)
        {
            var totalCpuUsage = 0;
            foreach (var fetchedJob in _fetchedJobs)
            {
                _queueConfigDict.TryGetValue(fetchedJob.Queue, out var queueConfig);
                int? cpuUsage = null;
                if (queueConfig?.FetchIndividualCpuRequests == true)
                    cpuUsage = ConvertStringToCpuMilliseconds(
                        SerializationHelper.Deserialize<string>(connection.GetJobParameter(fetchedJob.JobId, "CpuRequest")));
                cpuUsage ??= queueConfig?.CpuRequest ?? 0;
                totalCpuUsage += cpuUsage.Value;
            }
            return totalCpuUsage;
        }

        private long GetCurrentMemoryUsage(RedisConnection connection)
        {
            var totalMemoryUsage = 0L;
            foreach (var fetchedJob in _fetchedJobs)
            {
                _queueConfigDict.TryGetValue(fetchedJob.Queue, out var queueConfig);
                long? memoryUsage = null;
                if (queueConfig?.FetchIndividualMemoryRequests == true)
                    memoryUsage = ConvertStringToMemoryBytes(
                        SerializationHelper.Deserialize<string>(connection.GetJobParameter(fetchedJob.JobId, "MemoryRequest")));
                memoryUsage ??= queueConfig?.MemoryRequest ?? 0;
                totalMemoryUsage += memoryUsage.Value;
            }
            return totalMemoryUsage;
        }
    }
}
