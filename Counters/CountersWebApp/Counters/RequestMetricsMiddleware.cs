using Microsoft.AspNetCore.Http;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Counters.Request
{

    //
    // see https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/write?view=aspnetcore-3.0
    // for more details about custom middleware
    //
    public class RequestMetricsMiddleware
    {
        private readonly RequestDelegate _next;

        public RequestMetricsMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // get the count of GCs before processing the request
            var collectionCountBeforeProcessingTheRequest = GetCurrentCollectionCount();

            var sw = Stopwatch.StartNew();

            try
            {
                // Call the next delegate/middleware in the pipeline
                await _next(context);
            }
            finally
            {
                // compare the counter of GCs after processing the request
                // if the count changed, a garbage collection occurred during the processing 
                // and might have slowed it down and maybe reaching SLA limit: this could 
                // explain 9x-percentile in slow requests for example
                if (GetCurrentCollectionCount() - collectionCountBeforeProcessingTheRequest != 0)
                {
                    // update with collection metric
                    Debug.WriteLine("a GC occured during request processing");
                    RequestCountersEventSource.Instance.AddRequestWithGcDuration(sw.ElapsedMilliseconds);
                }
                else
                {
                    // update without collection metric
                    Debug.WriteLine("no GC during request processing");
                    RequestCountersEventSource.Instance.AddRequestWithoutGcDuration(sw.ElapsedMilliseconds);
                }
            }
        }

        private int GetCurrentCollectionCount()
        {
            int count = 0;
            for (int i = 0; i < GC.MaxGeneration; i++)
            {
                count += GC.CollectionCount(i);
            }

            return count;
        }
    }
}
