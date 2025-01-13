using Microsoft.Diagnostics.Tracing;
using System;
using System.Runtime.CompilerServices;

namespace Shared
{
    // from https://github.com/ocoanet/EventPipePlayground

    public unsafe partial class EventPipeUnresolvedStack
    {
        public EventPipeUnresolvedStack(ulong[] addresses)
        {
            Addresses = addresses;
        }

        public ulong[] Addresses { get; }

        public static EventPipeUnresolvedStack ReadFrom(TraceEvent traceEvent) => ReadStackUsingUnsafeAccessor(traceEvent);

        public static EventPipeUnresolvedStack ReadStackUsingUnsafeAccessor(TraceEvent traceEvent)
        {
            //var eventRecord = GetEventRecord(traceEvent);

            return GetFromEventRecord(traceEvent.eventRecord);
        }

        private static EventPipeUnresolvedStack GetFromEventRecord(TraceEventNativeMethods.EVENT_RECORD* eventRecord)
        {
            if (eventRecord == null)
                return null;

            var extendedDataCount = eventRecord->ExtendedDataCount;

            for (var dataIndex = 0; dataIndex < extendedDataCount; dataIndex++)
            {
                var extendedData = eventRecord->ExtendedData[dataIndex];
                if (extendedData.ExtType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_STACK_TRACE64)
                {
                    var stackRecord = (TraceEventNativeMethods.EVENT_EXTENDED_ITEM_STACK_TRACE64*)extendedData.DataPtr;

                    var addresses = &stackRecord->Address[0];
                    var addressCount = (extendedData.DataSize - sizeof(UInt64)) / sizeof(UInt64);
                    if (addressCount == 0)
                        return null;

                    var callStackAddresses = new ulong[addressCount];
                    for (var index = 0; index < addressCount; index++)
                    {
                        callStackAddresses[index] = addresses[index];
                    }

                    return new EventPipeUnresolvedStack(callStackAddresses);
                }
                else if (extendedData.ExtType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_STACK_TRACE32)
                {
                    var stackRecord = (TraceEventNativeMethods.EVENT_EXTENDED_ITEM_STACK_TRACE32*)extendedData.DataPtr;

                    var addresses = &stackRecord->Address[0];
                    var addressCount = (extendedData.DataSize - sizeof(UInt32)) / sizeof(UInt32);
                    if (addressCount == 0)
                        return null;

                    var callStackAddresses = new ulong[addressCount];  // store the 32 addresses as 64 bit addresses
                    for (var index = 0; index < addressCount; index++)
                    {
                        callStackAddresses[index] = addresses[index];
                    }

                    return new EventPipeUnresolvedStack(callStackAddresses);
                }
            }

            return null;
        }

        //[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "eventRecord")]
        //private static extern ref TraceEventNativeMethods.EVENT_RECORD* GetEventRecord(TraceEvent traceEvent);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "eventRecord")]
        private static unsafe TraceEventNativeMethods.EVENT_RECORD* GetEventRecord(TraceEvent traceEvent)
        {
            return traceEvent.eventRecord;
        }

    }
}
