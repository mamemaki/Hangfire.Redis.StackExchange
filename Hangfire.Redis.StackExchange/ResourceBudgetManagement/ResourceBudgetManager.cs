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

using Hangfire.Logging;
using Hangfire.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    internal class ResourceBudgetManager
    {
        class ResourceLimitTypeInternal
        {
            public ResourceLimitType Type { get; set; }
            public long Limit { get; set; }
            public long DefaultRequest { get; set; }

            public override string ToString() => Type.ToString();
        }

        private static readonly ILog Logger = LogProvider.For<ResourceBudgetManager>();

        private readonly object lockObj = new object();
        private readonly IDictionary<string, FetchedJobInfo> _fetchedJobs;
        private readonly List<ResourceLimitTypeInternal> _resourceLimitTypes;
        private readonly TimeSpan _usageLimitReachedWaitTimeBase;
        private readonly Dictionary<string, Dictionary<string, string>> _jobResourceRequestsCache;
        private int _consecutiveLimitReachedCount;

        public ResourceBudgetManager(RedisStorageOptions options)
        {
            _fetchedJobs = new Dictionary<string, FetchedJobInfo>();

            _resourceLimitTypes = options.ResourceLimitTypes.Select(s =>
            {
                return new ResourceLimitTypeInternal
                {
                    Type = s,
                    Limit = s.DeserializeResourceLimitValue(s.Limit),
                    DefaultRequest = s.DeserializeResourceLimitValue(s.DefaultRequest),
                };
            }).ToList();
            _usageLimitReachedWaitTimeBase = options.UsageLimitReachedWaitTimeBase;
            _jobResourceRequestsCache = new Dictionary<string, Dictionary<string, string>>();
        }

        internal IDictionary<string, FetchedJobInfo> FetchedJobs => _fetchedJobs;

        public void RemoveFetchedJob(RedisFetchedJob fetchedJob)
        {
            lock (lockObj)
            {
                _fetchedJobs.Remove(fetchedJob.JobId);
            }
        }

        public void AddFetchedJob(RedisFetchedJob fetchedJob)
        {
            _fetchedJobs.Add(fetchedJob.JobId, new FetchedJobInfo()
            {
                Job = fetchedJob,
            });
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
                _jobResourceRequestsCache.Clear();

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
            foreach (var resourceLimitType in _resourceLimitTypes)
            {
                var resUsage = GetFetchedJobsResourceUsage(resourceLimitType, redisConn);
                var newJobResReq = 0L;
                if (newJobId != null && newJobQueue != null)
                    newJobResReq = GetJobResourceUsage(resourceLimitType, redisConn, newJobId);
                if (resUsage + newJobResReq >= resourceLimitType.Limit)
                {
                    Logger.InfoFormat("The resource usage limit({0}) reached. (fetchedJobs={1}, newJob={2}, newJobId={3})",
                        resourceLimitType.Limit, resUsage, newJobResReq, newJobId);
                    return true;
                }
            }

            return false;
        }

        private Dictionary<string, string> GetJobResourceRequests(
            IRedisConnectionForResourceBudgetManager redisConn, string jobId)
        {
            if (_jobResourceRequestsCache.TryGetValue(jobId, out var jobResourceRequests))
            {
                return jobResourceRequests;
            }

            jobResourceRequests = redisConn.GetJobResourceRequests(jobId);
            _jobResourceRequestsCache[jobId] = jobResourceRequests;
            return jobResourceRequests;
        }

        private long GetJobResourceUsage(
            ResourceLimitTypeInternal resourceLimitType,
            IRedisConnectionForResourceBudgetManager redisConn,
            string jobId)
        {
            if (resourceLimitType.Type.FetchIndividualJobRequests)
            {
                var jobResourceRequests = GetJobResourceRequests(redisConn, jobId);
                var resVal = jobResourceRequests.GetValueOrDefault(resourceLimitType.Type.TypeName);
                if (resVal != null)
                    return resourceLimitType.Type.DeserializeResourceLimitValue(resVal);
            }
            return resourceLimitType.DefaultRequest;
        }

        private long GetFetchedJobsResourceUsage(
            ResourceLimitTypeInternal resourceLimitType,
            IRedisConnectionForResourceBudgetManager redisConn)
        {
            var totalResUsage = 0L;
            foreach (var fetchedJob in _fetchedJobs.Values)
            {
                var resTypeName = resourceLimitType.Type.TypeName;
                if (!fetchedJob.ResourceUsages.TryGetValue(resTypeName, out var resUsage))
                {
                    fetchedJob.ResourceUsages[resTypeName] = 
                        GetJobResourceUsage(resourceLimitType, redisConn, fetchedJob.JobId);
                }
                totalResUsage += fetchedJob.ResourceUsages[resTypeName].Value;
            }
            return totalResUsage;
        }
    }
}
