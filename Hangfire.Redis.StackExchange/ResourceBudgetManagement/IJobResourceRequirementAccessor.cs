using System;
using System.Collections.Generic;
using System.Text;

namespace Hangfire.Redis.StackExchange.ResourceBudgetManagement
{
    public interface IJobResourceRequirementAccessor
    {
        string GetCpuRequest(string jobId);
        string GetMemoryRequest(string jobId);
    }
}
