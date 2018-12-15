# ClrEvents
Source code based on TraceEvent to listen to CLR events at runtime.

## Introduction
Most of the code is detailed in the blog series related to CLR events:

Part 1: [Replace .NET performance counters by CLR event tracing.](http://labs.criteo.com/2018/06/replace-net-performance-counters-by-clr-event-tracing/)

Part 2: [Grab ETW Session, Providers and Events.](http://labs.criteo.com/2018/07/grab-etw-session-providers-and-events/)

Part 3: [Monitor Finalizers, contention and threads in your application.](http://labs.criteo.com/2018/09/monitor-finalizers-contention-and-threads-in-your-application/)

Part 4: [Spying on .NET Garbage Collector with TraceEvent.](https://medium.com/@chnasarre/spying-on-net-garbage-collector-with-traceevent-f49dc3117de)


## Source Code
The `DebuggingExtensions` Visual Studio 2017 solution contains four projects:

1. `ClrCounters`:.NET Standard assembly to easily listen to CLR events with TraceEvent. 

2. `ConsoleListener`: Demo console application that uses 'ClrCounters' to display CLR details of a running application.

3. `NaiveListener`: Demo console application that displays raw CLR events with TraceEvent.

4. `Simulator`: Console application used to simulate interesting behaviours (contention, exceptions, allocations, thread pool usage).



These projects depends on Nuget package:

- [TraceEvent](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent/): C# library to listen to CLR events.
Source code is available on [Github](https://github.com/Microsoft/perfview/tree/master/src/TraceEvent).
