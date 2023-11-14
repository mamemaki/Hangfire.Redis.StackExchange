using System;
using System.Collections.Generic;
using System.Text;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    public class DeterminationResult
    {
        public bool LimitReached { get; set; }
        public string Limit { get; set; }
        public Dictionary<string, string> Context { get; set; }
    }
}
