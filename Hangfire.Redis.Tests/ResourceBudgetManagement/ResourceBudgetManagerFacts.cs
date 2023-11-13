using Hangfire.Redis.StackExchange;
using Hangfire.Redis.StackExchange.ResourceBudgetManagement;
using Hangfire.Storage;
using Moq;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Hangfire.Redis.Tests.ResourceBudgetManagement
{
    public class ResourceBudgetManagerFacts
    {
        internal class JobInfo
        {
            public RedisFetchedJob FetchedJob { get; }
            public string CpuRequest { get; }
            public string MemoryRequest { get; }

            public JobInfo(string jobId, string queue,
                string cpuRequest = null, string memoryRequest = null)
            {
                FetchedJob = new RedisFetchedJob(_storage.Object, _redis.Object, jobId, queue);
                CpuRequest = cpuRequest;
                MemoryRequest = memoryRequest;
            }
        }

        class RedisStorageConnectionMock : IRedisConnectionForResourceBudgetManager
        {
            private ResourceBudgetManager _resourceBudgetManager;
            private Queue<JobInfo> _jobQueue;
            private List<JobInfo> _dequeuedJobs;
            public RedisStorageConnectionMock(ResourceBudgetManager resourceBudgetManager)
            {
                _resourceBudgetManager = resourceBudgetManager;
                _jobQueue = new Queue<JobInfo>();
                _dequeuedJobs = new List<JobInfo>();
            }

            public Queue<JobInfo> JobQueue => _jobQueue;

            public string DequeueJob(string queueName)
            {
                if (_jobQueue.TryDequeue(out var fetchedJob))
                {
                    _dequeuedJobs.Add(fetchedJob);
                    return fetchedJob.FetchedJob.JobId;
                }
                return null;
            }

            public IFetchedJob OnJobFetched(string jobId, string queueName)
            {
                return _dequeuedJobs.FirstOrDefault(s => s.FetchedJob.JobId == jobId).FetchedJob;
            }

            public void Requeue(string jobId, string queueName)
            {
            }

            public void Sleep(TimeSpan timeout, CancellationToken cancellationToken)
            {
            }

            public string GetCpuRequest(string jobId)
            {
                var job = _jobQueue.FirstOrDefault(x => x.FetchedJob.JobId == jobId);
                if (job == null)
                    return null;
                return job.CpuRequest;
            }

            public string GetMemoryRequest(string jobId)
            {
                var job = _jobQueue.FirstOrDefault(x => x.FetchedJob.JobId == jobId);
                if (job == null)
                    return null;
                return job.MemoryRequest;
            }

            public IFetchedJob TryFetchJob(string[] queues)
            {
                if (_jobQueue.TryDequeue(out var fetchedJob))
                    return fetchedJob.FetchedJob;
                return null;
            }
        }

        private static readonly Mock<RedisStorage> _storage = new Mock<RedisStorage>();
        private static readonly Mock<IDatabase> _redis = new Mock<IDatabase>();
        private RedisStorageOptions _redisStorageOptionsDefault;

        public ResourceBudgetManagerFacts()
        {
            _redisStorageOptionsDefault = new RedisStorageOptions()
            {
                CpuLimit = "1.0",
                MemoryLimit = "1Gi",
                QueueOptions = new List<QueueOptions>()
                {
                    new QueueOptions()
                    {
                        QueueName = "q1",
                        CpuRequest = "0.6",
                        MemoryRequest = "400Mi",
                        FetchIndividualCpuRequests = true,
                        FetchIndividualMemoryRequests = true,
                    },
                },
            };
        }

        public static IEnumerable<object[]> TryFetchJobTestData => new List<object[]>()
        {
            new object[] { new JobInfo[] {}, null },
            new object[] {
                new JobInfo[] {
                    new JobInfo("job1", "q1", "0.5", "150Mi"),
                },
                "job1",
            },
            new object[] {
                new JobInfo[] {
                    new JobInfo("job1", "q1", "0.5", "1500Mi"),
                },
                null,
            },
            new object[] {
                new JobInfo[] {
                    new JobInfo("job1", "q1", "0.8", "150Mi"),
                    new JobInfo("job2", "q1", "0.5", "150Mi"),
                },
                null,
            },
        };

        [Theory]
        [MemberData(nameof(TryFetchJobTestData))]
        internal void TryFetchJob(JobInfo[] jobs, string expectedFetchedJobId)
        {
            var cts = new CancellationTokenSource();
            var resourceBudgetManager = new ResourceBudgetManager(_redisStorageOptionsDefault);
            var connection = new RedisStorageConnectionMock(resourceBudgetManager);

            foreach (var job in jobs)
            {
                resourceBudgetManager.AddFetchedJob(job.FetchedJob);
                connection.JobQueue.Enqueue(job);
            }

            var fecthedJobActual = resourceBudgetManager.TryFetchJob(
                new string[] { "q1" }, connection, cts.Token);
            Assert.Equal(expectedFetchedJobId, fecthedJobActual?.JobId);
        }
    }
}
