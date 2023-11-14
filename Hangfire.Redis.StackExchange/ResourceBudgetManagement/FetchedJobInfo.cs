using System;
using System.Collections.Generic;
using System.Text;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    internal class FetchedJobInfo
    {
        public RedisFetchedJob Job { get; set; }
        public string JobId => Job.JobId;

        public JobResourceRequests ResourceRequests { get; set; }
    }
}
