# ClrEvents

Source code based on TraceEvent to listen to CLR events at runtime.

## Introduction

Most of the code is detailed in the blog series related to CLR events:

Part 1: [Replace .NET performance counters by CLR event tracing.](https://chrisnas.github.io/posts/2018-06-19_replace-net-performance-counters/)

Part 2: [Grab ETW Session, Providers and Events.](https://chrisnas.github.io/posts/2018-07-26_grab-etw-session-providers/)

Part 3: [Monitor Finalizers, contention and threads in your application.](https://chrisnas.github.io/posts/2018-09-28_monitor-finalizers-contention-threads/)

Part 4: [Spying on .NET Garbage Collector with TraceEvent.](https://chrisnas.github.io/posts/2018-12-15_spying-on-net-garbage/)

Part 5: [Building your own Java-like GC logs in .NET.](https://chrisnas.github.io/posts/2019-02-12_building-your-own-java/)

Part 6: [Spying on .NET Garbage Collector with .NET Core EventPipes](https://chrisnas.github.io/posts/2019-05-28_spying-on-net-garbage/)

Part 7: [.NET Core Counters internals: how to integrate counters in your monitoring pipeline](https://chrisnas.github.io/posts/2019-07-23_net-core-counters-internals/)

Part 8: [How to expose your custom counters in .NET Core](https://chrisnas.github.io/posts/2019-10-17_how-to-expose-your/)

Part 9: [Build your own .NET memory profiler in C# - allocations(1/2)](https://chrisnas.github.io/posts/2020-04-18_build-your-own-net/)

Part 10: [Build your own .NET memory profiler in C# - call stacks(2/2-1)](https://chrisnas.github.io/posts/2020-05-18_build-your-own-net/)

Part 11: [Build your own .NET memory profiler in C# - call stacks(2/2-2)]((https://chrisnas.github.io/posts/2020-06-19_build-your-own-net/)

## Source Code

The `Events\ClrEtw` Visual Studio solution contains different projects:

1. `Events.Shared`: .NET Standard assembly to easily listen to CLR events with TraceEvent (.NET Core and Framework) or EventPipe (.NET Core only). 

2. `ConsoleListener`: Demo console application that uses 'Events.Shared' to display CLR details of events emitted by a running .NET application including HTTP requests.

3. `NaiveListener`: Demo console application that displays raw CLR events with TraceEvent.

4. `Simulator`: Console application used to simulate interesting behaviours (contention, exceptions, allocations, thread pool usage).

5. `GcLog`: Helper classes to generate a log file containing one line per garbage collection happening in a .NET Application given its process ID.
   
                    EtwGcLog is based on TraceEvent and listen to ETW events.

6. `GcLogger`: Console application used to test GcLog.

7. `EventPipeGcLogger`: Console application used to test EventPipeGcLog (.NET Core 3.0.100).

8. `AllocationTickProfiler`: simple memory profiler using AllocationTick event.

9. `SampledObjectAllocationProfiler`: simple memory profiler using SampledObjectAllocation(High/Low) events.

10. `dotnet-activity`: tool to encode and decode event activities GUID

11. `dotnet-http`: CLI tool to monitor HTTP requests live

The `Counters\EventPipeCounters` Visual Studio solution contains different projets

1. `Counters.RuntimeClient`: Helper classes to easily get .NET Core counters; including .csv file automatic creation.

2. `SimpleCounters`: sample application to use CsvCounterListener and CounterMonitor helper classes.

3. `CountersWebApp`: sample ASP.NET Core application to demonstrate custom counters about count/duration of requests processed with(out) GC.

Projects dependecies:

- [TraceEvent](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent/): C# library to listen to CLR events.
  Source code is available on [Github](https://github.com/Microsoft/perfview/tree/master/src/TraceEvent).

- Microsoft.Diagnostics.Tools.RuntimeClient: copied from [github](https://github.com/dotnet/diagnostics/tree/master/src/Microsoft.Diagnostics.Tools.RuntimeClient) because it is supporting both ETW and EventPipe. For EventPipe only, use [Microsoft.Diagnostics.NETCore.Client](https://www.nuget.org/packages/Microsoft.Diagnostics.NETCore.Client)
