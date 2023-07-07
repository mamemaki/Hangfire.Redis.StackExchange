using System;
using Xunit;
using Utils2 = Hangfire.Redis.StackExchange.ResourceBudgetManagement.Utils;

namespace Hangfire.Redis.Tests.ResourceBudgetManagement
{
    public class UtilsFacts
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
            var actual = Utils2.ConvertStringToCpuMilliseconds(s);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("1a")]
        public void ConvertStringToCpuMilliseconds_FormatException(string s)
        {
            Assert.Throws<FormatException>(() => Utils2.ConvertStringToCpuMilliseconds(s));
        }

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
            var actual = Utils2.ConvertStringToMemoryBytes(s);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("1a")]
        public void ConvertStringToMemoryBytes_FormatException(string s)
        {
            Assert.Throws<FormatException>(() => Utils2.ConvertStringToMemoryBytes(s));
        }
    }
}
