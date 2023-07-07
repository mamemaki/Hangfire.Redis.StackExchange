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
using Hangfire.Storage;
using System;
using System.Collections.Generic;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
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

        private readonly object lockObj = new object();
        private readonly IList<RedisFetchedJob> _fetchedJobs;
        private readonly int _cpuLimit;
        private readonly long _memoryLimit;
        private readonly IDictionary<string, QueueConfig> _queueConfigDict;
        private bool? _isUsageLimitReachedResultCache;

        public ResourceBudgetManager(RedisStorageOptions options)
        {
            _fetchedJobs = new List<RedisFetchedJob>();

            _cpuLimit = Environment.ProcessorCount * 1000;
            if (options.CpuLimit != null)
                _cpuLimit = Utils.ConvertStringToCpuMilliseconds(options.CpuLimit);

            if (options.MemoryLimit != null)
            {
                _memoryLimit = Utils.ConvertStringToMemoryBytes(options.MemoryLimit);
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
                    CpuRequest = Utils.ConvertStringToCpuMilliseconds(queueOption.CpuRequest),
                    MemoryRequest = Utils.ConvertStringToMemoryBytes(queueOption.MemoryRequest),
                    FetchIndividualCpuRequests = queueOption.FetchIndividualCpuRequests,
                    FetchIndividualMemoryRequests = queueOption.FetchIndividualMemoryRequests,
                };
            }
        }

        internal IList<RedisFetchedJob> FetchedJobs => _fetchedJobs;
        internal int CpuLimit => _cpuLimit;
        internal long MemoryLimit => _memoryLimit;

        public void RemoveFetchedJob(RedisFetchedJob fetchedJob)
        {
            lock (lockObj)
            {
                _fetchedJobs.Remove(fetchedJob);
                _isUsageLimitReachedResultCache = null;
            }
        }

        public void AddFetchedJob(RedisFetchedJob fetchedJob)
        {
            _fetchedJobs.Add(fetchedJob);
        }

        public IFetchedJob TryFetchJob(string[] queues, RedisConnection connection,
            Func<string[], IFetchedJob> tryFetchJob)
        {
            lock (lockObj)
            {
                var limitReached = IsUsageLimitReached(connection);
                if (!limitReached)
                {
                    var fecthedJob = tryFetchJob(queues);
                    if (fecthedJob != null)
                        return fecthedJob;
                }

                return null;
            }
        }

        /// <summary>
        /// Get whether resource usage limit reached or not
        /// </summary>
        /// <returns>Return true if the current resource usage reaches limit, otherwise return false</returns>
        private bool IsUsageLimitReached(RedisConnection connection)
        {
            if (_isUsageLimitReachedResultCache.HasValue)
                return _isUsageLimitReachedResultCache.Value;

            var cpuUsage = GetCurrentCpuUsage(connection);
            if (cpuUsage >= _cpuLimit)
                return (_isUsageLimitReachedResultCache = true).Value;

            var memoryUsage = GetCurrentMemoryUsage(connection);
            if (memoryUsage >= _memoryLimit)
                return (_isUsageLimitReachedResultCache = true).Value;

            return (_isUsageLimitReachedResultCache = false).Value;
        }

        private int GetCurrentCpuUsage(RedisConnection connection)
        {
            var totalCpuUsage = 0;
            foreach (var fetchedJob in _fetchedJobs)
            {
                _queueConfigDict.TryGetValue(fetchedJob.Queue, out var queueConfig);
                int? cpuUsage = null;
                if (queueConfig?.FetchIndividualCpuRequests == true)
                    cpuUsage = Utils.ConvertStringToCpuMilliseconds(
                        SerializationHelper.Deserialize<string>(
                            connection.GetJobParameter(fetchedJob.JobId, "CpuRequest")));
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
                    memoryUsage = Utils.ConvertStringToMemoryBytes(
                        SerializationHelper.Deserialize<string>(connection.GetJobParameter(fetchedJob.JobId, "MemoryRequest")));
                memoryUsage ??= queueConfig?.MemoryRequest ?? 0;
                totalMemoryUsage += memoryUsage.Value;
            }
            return totalMemoryUsage;
        }
    }
}
