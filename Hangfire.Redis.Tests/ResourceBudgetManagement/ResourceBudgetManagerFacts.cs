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
            private List<JobInfo> _jobs;
            private Queue<JobInfo> _enqueuedJobs;
            private List<JobInfo> _dequeuedJobs;
            public RedisStorageConnectionMock(ResourceBudgetManager resourceBudgetManager)
            {
                _resourceBudgetManager = resourceBudgetManager;
                _jobs = new List<JobInfo>();
                _enqueuedJobs = new Queue<JobInfo>();
                _dequeuedJobs = new List<JobInfo>();
            }

            public Queue<JobInfo> EnqueuedJobs => _enqueuedJobs;
            public List<JobInfo> Jobs => _jobs;

            public string DequeueJob(string queueName)
            {
                if (_enqueuedJobs.TryDequeue(out var fetchedJob))
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

            public Dictionary<string, string> GetJobResourceRequests(string jobId)
            {
                var job = _jobs.FirstOrDefault(x => x.FetchedJob.JobId == jobId);
                if (job == null)
                    return default;
                return new Dictionary<string, string>()
                {
                    { "Cpu", job.CpuRequest },
                    { "Memory", job.MemoryRequest },
                };
            }
        }

        private static readonly Mock<RedisStorage> _storage = new Mock<RedisStorage>();
        private static readonly Mock<IDatabase> _redis = new Mock<IDatabase>();
        private RedisStorageOptions _redisStorageOptionsDefault;

        public ResourceBudgetManagerFacts()
        {
            _redisStorageOptionsDefault = new RedisStorageOptions()
            {
                FetchIndividualJobRequests = true,
                ResourceLimitTypes = new List<ResourceLimitType>()
                {
                    new ResourceLimitType_Cpu()
                    {
                        Limit = "1.0",
                        DefaultRequest = "0.6",
                    },
                    new ResourceLimitType_Memory()
                    {
                        Limit = "1Gi",
                        DefaultRequest = "400Mi",
                    },
                },
            };
        }

        public static IEnumerable<object[]> TryFetchJobTestData => new List<object[]>()
        {
            new object[] { new JobInfo[] {}, new JobInfo[] {}, null },
            new object[] {
                new JobInfo[] {
                    new JobInfo("job1", "q1", "0.5", "150Mi"),
                },
                new JobInfo[] {
                    new JobInfo("job2", "q1", "0.4", "150Mi"),
                },
                "job2",
            },
            new object[] {
                new JobInfo[] {},
                new JobInfo[] {
                    new JobInfo("job1", "q1", "0.5", "1500Mi"),
                },
                null,
            },
            new object[] {
                new JobInfo[] {
                    new JobInfo("job1", "q1", "0.8", "150Mi"),
                },
                new JobInfo[] {
                    new JobInfo("job2", "q1", "0.5", "150Mi"),
                },
                null,
            },
        };

        [Theory]
        [MemberData(nameof(TryFetchJobTestData))]
        internal void TryFetchJob(JobInfo[] fetchedJobs, JobInfo[] enqueuedJobs, string expectedFetchedJobId)
        {
            var cts = new CancellationTokenSource();
            var resourceBudgetManager = new ResourceBudgetManager(_redisStorageOptionsDefault);
            var connection = new RedisStorageConnectionMock(resourceBudgetManager);

            foreach (var fetchedJob in fetchedJobs)
            {
                resourceBudgetManager.AddFetchedJob(fetchedJob.FetchedJob);
                connection.Jobs.Add(fetchedJob);
            }

            foreach (var enqueuedJob in enqueuedJobs)
            {
                connection.EnqueuedJobs.Enqueue(enqueuedJob);
                connection.Jobs.Add(enqueuedJob);
            }

            var fecthedJobActual = resourceBudgetManager.TryFetchJob(
                new string[] { "q1" }, connection, cts.Token);
            Assert.Equal(expectedFetchedJobId, fecthedJobActual?.JobId);
        }
    }
}
