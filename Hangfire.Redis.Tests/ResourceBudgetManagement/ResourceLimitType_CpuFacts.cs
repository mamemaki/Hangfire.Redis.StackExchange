using Hangfire.Redis.StackExchange.ResourceBudgetManagement;
using System;
using Xunit;

namespace Hangfire.Redis.Tests.ResourceBudgetManagement
{
    public class ResourceLimitType_CpuFacts
    {
        [Theory]
        [InlineData("0.001", 1)]
        [InlineData("0.01", 10)]
        [InlineData("0.1", 100)]
        [InlineData("1", 1000)]
        [InlineData("1.234", 1234)]
        [InlineData("1m", 1)]
        [InlineData("10m", 10)]
        [InlineData("100m", 100)]
        [InlineData("1000m", 1000)]
        [InlineData("1234m", 1234)]
        public void ConvertStringToCpuMilliseconds(string s, int expected)
        {
            var actual = ResourceLimitType_Cpu.ConvertStringToCpuMilliseconds(s);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("1a")]
        public void ConvertStringToCpuMilliseconds_FormatException(string s)
        {
            Assert.Throws<FormatException>(() => ResourceLimitType_Cpu.ConvertStringToCpuMilliseconds(s));
        }
    }
}
