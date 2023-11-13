using Hangfire.Storage;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    public interface IRedisConnectionForResourceBudgetManager
    {
        string DequeueJob(string queueName);
        IFetchedJob OnJobFetched(string jobId, string queueName);
        void Requeue(string jobId, string queueName);
        void Sleep(TimeSpan timeout, CancellationToken cancellationToken);
        Dictionary<string, string> GetJobResourceRequests(string jobId);
    }
}
