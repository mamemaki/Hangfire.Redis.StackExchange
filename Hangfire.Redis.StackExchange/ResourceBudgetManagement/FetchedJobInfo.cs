using System;
using System.Collections.Generic;
using System.Text;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    internal class FetchedJobInfo
    {
        public RedisFetchedJob Job { get; set; }
        public string JobId => Job.JobId;

        public Dictionary<string, long?> ResourceUsages { get; set; }

        public FetchedJobInfo()
        {
            ResourceUsages = new Dictionary<string, long?>();
        }
    }
}
