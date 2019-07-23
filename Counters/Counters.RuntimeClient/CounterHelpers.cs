using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.Diagnostics.Tracing;

namespace Counters.RuntimeClient
{
    public class CounterHelpers
    {
        public static Provider MakeProvider(string name, int refreshIntervalInSec)
        {
            var filterData = BuildFilterData(refreshIntervalInSec);
            return new Provider(name, 0xFFFFFFFF, EventLevel.Verbose, filterData);
        }

        private static string BuildFilterData(int refreshIntervalInSec)
        {
            if (refreshIntervalInSec < 1)
                throw new ArgumentOutOfRangeException(nameof(refreshIntervalInSec), $"must be at least 1 second");

            return $"EventCounterIntervalSec={refreshIntervalInSec}";
        }
    }
}
