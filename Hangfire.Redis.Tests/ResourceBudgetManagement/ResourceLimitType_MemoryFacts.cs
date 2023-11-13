using Hangfire.Redis.StackExchange.ResourceBudgetManagement;
using System;
using Xunit;

namespace Hangfire.Redis.Tests.ResourceBudgetManagement
{
    public class ResourceLimitType_MemoryFacts
    {
        [Theory]
        [InlineData("1T", 1000000000000)]
        [InlineData("1G", 1000000000)]
        [InlineData("1M", 1000000)]
        [InlineData("1K", 1000)]
        [InlineData("1", 1)]
        [InlineData("1Ti", 1099511627776)]
        [InlineData("1Gi", 1073741824)]
        [InlineData("1Mi", 1048576)]
        [InlineData("1Ki", 1024)]
        [InlineData("1234", 1234)]
        [InlineData("", 0)]
        public void ConvertStringToMemoryBytes(string s, long expected)
        {
            var actual = ResourceLimitType_Memory.ConvertStringToMemoryBytes(s);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("1a")]
        public void ConvertStringToMemoryBytes_FormatException(string s)
        {
            Assert.Throws<FormatException>(() => ResourceLimitType_Memory.ConvertStringToMemoryBytes(s));
        }
    }
}
