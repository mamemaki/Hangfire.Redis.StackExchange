using System;
using System.Collections.Generic;
using System.Text;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    public class JobResourceRequests
    {
        public string JobId { get; }
        public Dictionary<string, string> ResourceRequests { get; }

        internal JobResourceRequests(string jobId, Dictionary<string, string> resourceRequests)
        {
            JobId = jobId;
            ResourceRequests = resourceRequests;
        }
    }
}
