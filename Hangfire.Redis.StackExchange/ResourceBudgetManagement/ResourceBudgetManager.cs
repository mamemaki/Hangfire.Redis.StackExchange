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
        private static readonly ILog Logger = LogProvider.For<ResourceBudgetManager>();

        private readonly object lockObj = new object();
        private readonly IDictionary<string, FetchedJobInfo> _fetchedJobs;
        private readonly List<ResourceLimitType> _resourceLimitTypes;
        private readonly bool _fetchIndividualJobRequests;
        private readonly TimeSpan _usageLimitReachedWaitTimeBase;
        private int _consecutiveLimitReachedCount;

        public ResourceBudgetManager(RedisStorageOptions options)
        {
            _fetchedJobs = new Dictionary<string, FetchedJobInfo>();

            _resourceLimitTypes = options.ResourceLimitTypes;
            _fetchIndividualJobRequests = options.FetchIndividualJobRequests;
            _usageLimitReachedWaitTimeBase = options.UsageLimitReachedWaitTimeBase;
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
                var waitTime = new TimeSpan(_usageLimitReachedWaitTimeBase.Ticks * 
                    TimeSpan.FromMinutes(Math.Min(_consecutiveLimitReachedCount, 60)).Ticks);
                Logger.InfoFormat("Sleep {0} before fetch next job", waitTime);
                redisConn.Sleep(waitTime, cancellationToken);
            }

            lock (lockObj)
            {
                var (limitReached, _) = IsUsageLimitReached(redisConn);
                if (limitReached)
                {
                    _consecutiveLimitReachedCount++;
                    return null;
                }

                limitReached = false;
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
                var (limitReached, newJobReq) = IsUsageLimitReached(redisConn, jobId);
                if (limitReached)
                {
                    // Requeue to head of the queue
                    Logger.InfoFormat("Requeue job({0}) to head of the queue({1})", jobId, queueName);
                    redisConn.Requeue(jobId, queueName);
                    return (null, true);
                }

                var fetchedJob = redisConn.OnJobFetched(jobId, queueName);
                if (_fetchedJobs.TryGetValue(jobId, out var fetchedJob2))
                    fetchedJob2.ResourceRequests = newJobReq;
                return (fetchedJob, false);
            }

            return (null, false);
        }

        /// <summary>
        /// Get whether resource usage limit reached or not
        /// </summary>
        /// <param name="redisConn"></param>
        /// <param name="newJobId">ID for the new job</param>
        /// <returns>Return true if the current and new job resource usage reaches limit, otherwise return false</returns>
        private (bool, JobResourceRequests) IsUsageLimitReached(IRedisConnectionForResourceBudgetManager redisConn,
            string newJobId = null)
        {
            JobResourceRequests newJobReq = null;
            if (newJobId != null)
            {
                Dictionary<string, string> jobReq = null;
                if (_fetchIndividualJobRequests)
                    jobReq = redisConn.GetJobResourceRequests(newJobId);
                newJobReq = new JobResourceRequests(newJobId, jobReq);
            }

            var fetchedJobReqs = GetFetchedJobResourceRequests(redisConn);

            foreach (var resourceLimitType in _resourceLimitTypes)
            {
                var ret = resourceLimitType.IsUsageLimitReached(fetchedJobReqs, newJobReq);
                if (ret.LimitReached)
                {
                    Logger.InfoFormat("The {0} resource usage limit({1}) reached. (context={2})",
                        resourceLimitType.GetType().Name, ret.Limit, DictToDebugString(ret.Context));
                    return (true, null);
                }
            }

            return (false, newJobReq);
        }

        private List<JobResourceRequests> GetFetchedJobResourceRequests(IRedisConnectionForResourceBudgetManager redisConn)
        {
            var jobReqs = new List<JobResourceRequests>();
            foreach (var fetchedJob in _fetchedJobs.Values)
            {
                if (fetchedJob.ResourceRequests == null)
                {
                    Dictionary<string, string> jobReq = null;
                    if (_fetchIndividualJobRequests)
                        jobReq = redisConn.GetJobResourceRequests(fetchedJob.JobId);
                    fetchedJob.ResourceRequests = new JobResourceRequests(
                        fetchedJob.JobId, jobReq);
                }

                jobReqs.Add(fetchedJob.ResourceRequests);
            }
            return jobReqs;
        }

        private static string DictToDebugString<TKey, TValue>(ICollection<KeyValuePair<TKey, TValue>> dict)
        {
            return "[" + string.Join(",", dict.Select(kv => $"{kv.Key}: {kv.Value}").ToArray()) + "]";
        }
    }
}
