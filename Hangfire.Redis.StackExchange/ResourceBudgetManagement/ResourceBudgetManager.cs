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
using Hangfire.Logging;
using Hangfire.Storage;
using System;
using System.Collections.Generic;
using System.Threading;

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

        private static readonly ILog Logger = LogProvider.For<ResourceBudgetManager>();

        private readonly object lockObj = new object();
        private readonly IDictionary<string, FetchedJobInfo> _fetchedJobs;
        private readonly int _cpuLimit;
        private readonly long _memoryLimit;
        private readonly IDictionary<string, QueueConfig> _queueConfigDict;
        private bool? _isUsageLimitReachedResultCache;
        private int _consecutiveLimitReachedCount;
        private TimeSpan _usageLimitReachedWaitTimeBase;

        public ResourceBudgetManager(RedisStorageOptions options)
        {
            _fetchedJobs = new Dictionary<string, FetchedJobInfo>();

            _usageLimitReachedWaitTimeBase = options.UsageLimitReachedWaitTimeBase;
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
                if (string.IsNullOrEmpty(queueOption.QueueName))
                    throw new Exception($"QueueName must be set");
                _queueConfigDict[queueOption.QueueName] = new QueueConfig()
                {
                    CpuRequest = Utils.ConvertStringToCpuMilliseconds(queueOption.CpuRequest),
                    MemoryRequest = Utils.ConvertStringToMemoryBytes(queueOption.MemoryRequest),
                    FetchIndividualCpuRequests = queueOption.FetchIndividualCpuRequests,
                    FetchIndividualMemoryRequests = queueOption.FetchIndividualMemoryRequests,
                };
            }
        }

        internal IDictionary<string, FetchedJobInfo> FetchedJobs => _fetchedJobs;
        internal int CpuLimit => _cpuLimit;
        internal long MemoryLimit => _memoryLimit;

        public void RemoveFetchedJob(RedisFetchedJob fetchedJob)
        {
            lock (lockObj)
            {
                _fetchedJobs.Remove(fetchedJob.JobId);
                _isUsageLimitReachedResultCache = null;
            }
        }

        public void AddFetchedJob(RedisFetchedJob fetchedJob)
        {
            _fetchedJobs.Add(fetchedJob.JobId, new FetchedJobInfo()
            {
                Job = fetchedJob,
            });
            _isUsageLimitReachedResultCache = null;
        }

        public IFetchedJob TryFetchJob(string[] queues, IRedisConnectionForResourceBudgetManager redisConn,
            CancellationToken cancellationToken)
        {
            // Sleep if the limit was reached last time
            if (_consecutiveLimitReachedCount > 0)
            {
                var waitTime = _usageLimitReachedWaitTimeBase * Math.Min(_consecutiveLimitReachedCount, 60);
                Logger.InfoFormat("Sleep {0} before fetch next job", waitTime);
                redisConn.Sleep(waitTime, cancellationToken);
            }

            lock (lockObj)
            {
                if (IsUsageLimitReached(redisConn))
                {
                    _consecutiveLimitReachedCount++;
                    return null;
                }

                var limitReached = false;
                for (int i = 0; i < queues.Length; i++)
                {
                    var (fecthedJob, limitReached2) = TryFetchJob(queues[i], redisConn);
                    if (fecthedJob != null)
                    {
                        _consecutiveLimitReachedCount = 0; // Reset
                        return fecthedJob;
                    }
                    if (limitReached2)
                        limitReached = limitReached2;
                }

                if (limitReached)
                    _consecutiveLimitReachedCount++;

                return null;
            }
        }

        private (IFetchedJob, bool) TryFetchJob(string queueName, IRedisConnectionForResourceBudgetManager redisConn)
        {
            var jobId = redisConn.DequeueJob(queueName);
            if (jobId != null)
            {
                var limitReached = IsUsageLimitReached(redisConn, jobId, queueName);
                if (limitReached)
                {
                    // Requeue to head of the queue
                    Logger.InfoFormat("Requeue job({0}) to head of the queue({1})", jobId, queueName);
                    redisConn.Requeue(jobId, queueName);
                    return (null, true);
                }

                return (redisConn.OnJobFetched(jobId, queueName), false);
            }

            return (null, false);
        }

        /// <summary>
        /// Get whether resource usage limit reached or not
        /// </summary>
        /// <param name="redisConn"></param>
        /// <param name="newJobId">ID for the new job</param>
        /// <param name="newJobQueue">Queue name for the new job</param>
        /// <returns>Return true if the current and new job resource usage reaches limit, otherwise return false</returns>
        private bool IsUsageLimitReached(IRedisConnectionForResourceBudgetManager redisConn,
            string newJobId = null, string newJobQueue = null)
        {
            if (_isUsageLimitReachedResultCache.HasValue)
                return _isUsageLimitReachedResultCache.Value;

            var cpuUsage = GetFetchedJobsCpuUsage(redisConn);
            int newJobCpuReq = 0;
            if (newJobId != null && newJobQueue != null)
                newJobCpuReq = GetJobCpuUsage(redisConn, newJobId, newJobQueue);
            if (cpuUsage + newJobCpuReq >= _cpuLimit)
            {
                Logger.InfoFormat("The CPU usage limit({0}) reached. (fetchedJobs={1}, newJob={2})", 
                    _cpuLimit, cpuUsage, newJobCpuReq);
                return (_isUsageLimitReachedResultCache = true).Value;
            }

            var memoryUsage = GetFetchedJobsMemoryUsage(redisConn);
            long newJobMemoryReq = 0;
            if (newJobId != null && newJobQueue != null)
                newJobMemoryReq = GetJobMemoryUsage(redisConn, newJobId, newJobQueue);
            if (memoryUsage + newJobMemoryReq >= _memoryLimit)
            {
                Logger.InfoFormat("The Memory usage limit({0}) reached. (fetchedJobs={1}, newJob={2})", 
                    _memoryLimit, memoryUsage, newJobMemoryReq);
                return (_isUsageLimitReachedResultCache = true).Value;
            }

            return (_isUsageLimitReachedResultCache = false).Value;
        }

        private int GetJobCpuUsage(IRedisConnectionForResourceBudgetManager redisConn,
            string jobId, string jobQueue)
        {
            _queueConfigDict.TryGetValue(jobQueue, out var queueConfig);
            if (queueConfig?.FetchIndividualCpuRequests == true)
                return Utils.ConvertStringToCpuMilliseconds(redisConn.GetCpuRequest(jobId));
            return queueConfig?.CpuRequest ?? 0;
        }

        private int GetFetchedJobsCpuUsage(IRedisConnectionForResourceBudgetManager redisConn)
        {
            var totalCpuUsage = 0;
            foreach (var fetchedJob in _fetchedJobs.Values)
            {
                if (!fetchedJob.CpuUsage.HasValue)
                {
                    fetchedJob.CpuUsage = GetJobCpuUsage(redisConn, fetchedJob.JobId, fetchedJob.Job.Queue);
                }
                totalCpuUsage += fetchedJob.CpuUsage.Value;
            }
            return totalCpuUsage;
        }

        private long GetJobMemoryUsage(IRedisConnectionForResourceBudgetManager redisConn,
            string jobId, string jobQueue)
        {
            _queueConfigDict.TryGetValue(jobQueue, out var queueConfig);
            if (queueConfig?.FetchIndividualMemoryRequests == true)
                return Utils.ConvertStringToMemoryBytes(redisConn.GetMemoryRequest(jobId));
            return queueConfig?.MemoryRequest ?? 0;
        }

        private long GetFetchedJobsMemoryUsage(IRedisConnectionForResourceBudgetManager redisConn)
        {
            var totalMemoryUsage = 0L;
            foreach (var fetchedJob in _fetchedJobs.Values)
            {
                if (!fetchedJob.MemoryUsage.HasValue)
                {
                    fetchedJob.MemoryUsage = GetJobMemoryUsage(redisConn, fetchedJob.JobId, fetchedJob.Job.Queue);
                }
                totalMemoryUsage += fetchedJob.MemoryUsage.Value;
            }
            return totalMemoryUsage;
        }
    }
}
