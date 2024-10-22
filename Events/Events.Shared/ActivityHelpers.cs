using System.Diagnostics;
using System.Text;
using System;
using System.Runtime.InteropServices;

namespace Shared
{

public static class ActivityHelpers
{
// from https://github.com/microsoft/perfview/blob/main/src/TraceEvent/Computers/StartStopActivityComputer.cs#L649
// also via Twitter from https://x.com/_MihaZupan
    private enum NumberListCodes : byte
    {
        End = 0x0,
        LastImmediateValue = 0xA,
        PrefixCode = 0xB,
        MultiByte1 = 0xC,
    }

    public static unsafe bool IsActivityPath(Guid guid, int processID)
    {
        uint* uintPtr = (uint*)&guid;
        uint sum = uintPtr[0] + uintPtr[1] + uintPtr[2] + 0x599D99AD;
        if (processID == 0)
        {
            return ((sum & 0xFFF00000) == (uintPtr[3] & 0xFFF00000));
        }
        if ((sum ^ (uint)processID) == uintPtr[3])
        {
            return true;
        }
        return (sum == uintPtr[3]);

        //var bytes = new ReadOnlySpan<byte>(guid.ToByteArray());
        //var uints = MemoryMarshal.Cast<byte, uint>(bytes);

        //uint sum = uints[0] + uints[1] + uints[2] + 0x599D99AD;
        //if (processID == 0)
        //{
        //    return ((sum & 0xFFF00000) == (uints[3] & 0xFFF00000));
        //}
        //if ((sum ^ (uint)processID) == uints[3])
        //{
        //    return true;
        //}
        //return (sum == uints[3]);
    }

    public static unsafe int ActivityPathProcessID(Guid guid)
    {
        uint* uintPtr = (uint*)&guid;
        uint sum = uintPtr[0] + uintPtr[1] + uintPtr[2] + 0x599D99AD;
        return (int)(sum ^ uintPtr[3]);

        //var bytes = new ReadOnlySpan<byte>(guid.ToByteArray());
        //var uints = MemoryMarshal.Cast<byte, uint>(bytes);

        //uint sum = uints[0] + uints[1] + uints[2] + 0x599D99AD;
        //return (int)(sum ^ uints[3]);
    }

    public static string ActivityPathString(Guid guid, int processID = 0)
    {
        //return IsActivityPath(guid, processID) ? CreateActivityPathString(guid, processID != 0) : guid.ToString();
        return IsActivityPath(guid, processID) ? CreateActivityPathStringPerfview(guid, processID != 0) : guid.ToString();
    }

    internal static unsafe string CreateActivityPathStringPerfview(Guid guid, bool includePID = false)
    {
        Debug.Assert(IsActivityPath(guid, 0));

        StringBuilder sb = new StringBuilder();

        if (includePID)
        {
            var processID = ActivityPathProcessID(guid);
            if (processID != 0)
            {
                sb.Append("#");
                sb.Append(processID);
                sb.Append("/");
            }
        }

        byte* bytePtr = (byte*)&guid;
        byte* endPtr = bytePtr + 12;
        char separator = '/';
        bool isRoot = true;

        while (bytePtr < endPtr)
        {
            uint nibble = (uint)(*bytePtr >> 4);
            bool secondNibble = false;              // are we reading the second nibble (low order bits) of the byte.
        NextNibble:
            if (nibble == (uint)NumberListCodes.End)
            {
                break;
            }

            if (nibble <= (uint)NumberListCodes.LastImmediateValue)
            {
                if (isRoot)
                {
                    isRoot = false;
                    sb.Append(nibble);
                }
                else
                {
                    sb.Append('/').Append(nibble);
                }

                if (!secondNibble)
                {
                    nibble = (uint)(*bytePtr & 0xF);
                    secondNibble = true;
                    goto NextNibble;
                }
                // We read the second nibble so we move on to the next byte.
                bytePtr++;
                continue;
            }
            else if (nibble == (uint)NumberListCodes.PrefixCode)
            {
                // This are the prefix codes.   If the next nibble is MultiByte, then this is an overflow ID.
                // we we denote with a $ instead of a / separator.

                // Read the next nibble.
                if (!secondNibble)
                {
                    nibble = (uint)(*bytePtr & 0xF);
                }
                else
                {
                    bytePtr++;
                    if (endPtr <= bytePtr)
                    {
                        break;
                    }

                    nibble = (uint)(*bytePtr >> 4);
                }

                if (nibble < (uint)NumberListCodes.MultiByte1)
                {
                    // If the nibble is less than MultiByte we have not defined what that means
                    // For now we simply give up, and stop parsing.  We could add more cases here...
                    return guid.ToString();
                }
                // If we get here we have a overflow ID, which is just like a normal ID but the separator is $
                separator = '$';
                // Fall into the Multi-byte decode case.
            }

            Debug.Assert((uint)NumberListCodes.MultiByte1 <= nibble);
            // At this point we are decoding a multi-byte number, either a normal number or a
            // At this point we are byte oriented, we are fetching the number as a stream of bytes.
            uint numBytes = nibble - (uint)NumberListCodes.MultiByte1;

            uint value = 0;
            if (!secondNibble)
            {
                value = (uint)(*bytePtr & 0xF);
            }

            bytePtr++;       // Advance to the value bytes

            numBytes++;     // Now numBytes is 1-4 and represents the number of bytes to read.
            if (endPtr < bytePtr + numBytes)
            {
                break;
            }

            // Compute the number (little endian) (thus backwards).
            for (int i = (int)numBytes - 1; 0 <= i; --i)
            {
                value = (value << 8) + bytePtr[i];
            }

            // Print the value
            sb.Append(separator).Append(value);

            bytePtr += numBytes;        // Advance past the bytes.

            // FIX: there is a special case for a value < 4096/0xFFF where the encoder made a mistake
            // It is encoded with 1 nibble + 1 byte + 1 byte that contains 0 (hence stopping the parsing)
            if ((value > 0xFF) && (value <= 0xFFF) && (bytePtr + 1 < endPtr) && (bytePtr[0] == 0) && (bytePtr[1] != 0))
            {
                bytePtr++;  // Advance past the 00 byte
            }
        }

        //sb.Append('/');
        return sb.ToString();
    }

    internal static string CreateActivityPathString(Guid guid, bool includePID = false)
    {
        Debug.Assert(IsActivityPath(guid, 0));

        StringBuilder sb = new StringBuilder();

        if (includePID)
        {
            var processID = ActivityPathProcessID(guid);
            if (processID != 0)
            {
                sb.Append("/#");
                sb.Append(processID);
            }
        }
        var bytes = new ReadOnlySpan<byte>(guid.ToByteArray());
        int pos = 0;
        char separator = '/';
        while (pos < 12)
        {
            uint nibble = (uint)(bytes[pos] >> 4);
            bool secondNibble = false;
        NextNibble:
            if (nibble == (uint)NumberListCodes.End)
            {
                break;
            }
            if (nibble <= (uint)NumberListCodes.LastImmediateValue)
            {
                sb.Append('/').Append(nibble);
                if (!secondNibble)
                {
                    nibble = (uint)(bytes[pos] & 0xF);
                    secondNibble = true;
                    goto NextNibble;
                }
                pos++;
                continue;
            }
            else if (nibble == (uint)NumberListCodes.PrefixCode)
            {
                if (!secondNibble)
                {
                    nibble = (uint)(bytes[pos] & 0xF);
                }
                else
                {
                    pos++;
                    if (pos <= 12)
                    {
                        break;
                    }
                    nibble = (uint)(bytes[pos] >> 4);
                }
                if (nibble < (uint)NumberListCodes.MultiByte1)
                {
                    return guid.ToString();
                }
                separator = '$';
            }
            Debug.Assert((uint)NumberListCodes.MultiByte1 <= nibble);
            uint numBytes = nibble - (uint)NumberListCodes.MultiByte1;
            uint value = 0;
            if (!secondNibble)
            {
                value = (uint)(bytes[pos] & 0xF);
            }
            pos++;
            numBytes++;
            if (12 < pos + numBytes)
            {
                break;
            }
            for (int i = (int)numBytes - 1; 0 <= i; --i)
            {
                value = (value << 8) + bytes[i];
            }
            sb.Append(separator).Append(value);

            pos += (int)numBytes;
        }

        sb.Append('/');
        return sb.ToString();
    }


    // ----------------------------------------------------------------------------------------------------------------------------------------
    // from https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/ActivityTracker.cs
    //

    /// Add the activity id 'id' to the output Guid 'outPtr' starting at the offset 'whereToAddId'
    /// Thus if this number is 6 that is where 'id' will be added.    This will return 13 (12
    /// is the maximum number of bytes that fit in a GUID) if the path did not fit.
    /// If 'overflow' is true, then the number is encoded as an 'overflow number (which has a
    /// special (longer prefix) that indicates that this ID is allocated differently
    public static unsafe int AddIdToGuid(Guid* outPtr, int whereToAddId, uint id, bool overflow = false)
    {
        byte* ptr = (byte*)outPtr;
        byte* endPtr = ptr + 12;
        ptr += whereToAddId;
        if (endPtr <= ptr)
            return 13;                // 12 means we might exactly fit, 13 means we definitely did not fit

        if (0 < id && id <= (uint)NumberListCodes.LastImmediateValue && !overflow)
            WriteNibble(ref ptr, endPtr, id);
        else
        {
            uint len = 4;
            if (id <= 0xFF)
                len = 1;
            else if (id <= 0xFFFF)
                len = 2;
            else if (id <= 0xFFFFFF)
                len = 3;

            if (overflow)
            {
                if (endPtr <= ptr + 2)        // I need at least 2 bytes
                    return 13;

                // Write out the prefix code nibble and the length nibble
                WriteNibble(ref ptr, endPtr, (uint)NumberListCodes.PrefixCode);
            }
            // The rest is the same for overflow and non-overflow case
            WriteNibble(ref ptr, endPtr, (uint)NumberListCodes.MultiByte1 + (len - 1));

            // Do we have an odd nibble?   If so flush it or use it for the 12 byte case.
            if (ptr < endPtr && *ptr != 0)
            {
                // If the value < 4096 we can use the nibble we are otherwise just outputting as padding.
                if (id < 4096)
                {
                    // Indicate this is a 1 byte multicode with 4 high order bits in the lower nibble.
                    *ptr = (byte)(((uint)NumberListCodes.MultiByte1 << 4) + (id >> 8));

                    // FIX: it means that we now just need 1 byte to store the id instead of 2 as computed before
                    //      --> the previous line is overwriting the "NumberListCodes.MultiByte1 + (len - 1)" value
                    //          with NumberListCodes.MultiByte1 followed by the 4 high order bits of the id
                    //      the 00 byte was due to the fact that the "id >>= 8;" line was leading to id = 0
                    //      that was stored in the additional unneeded byte
                    //len = 1;

                    id &= 0xFF;     // Now we only want the low order bits.
                }
                ptr++;
            }

            // Write out the bytes.
            while (0 < len)
            {
                if (endPtr <= ptr)
                {
                    ptr++;        // Indicate that we have overflowed
                    break;
                }
                *ptr++ = (byte)id;
                id >>= 8;
                --len;
            }
        }

        // Compute the checksum
        uint* sumPtr = (uint*)outPtr;
        // We set the last DWORD the sum of the first 3 DWORDS in the GUID.   This
        // This last number is a random number (it identifies us as us)  the process ID to make it unique per process.
        sumPtr[3] = (sumPtr[0] + sumPtr[1] + sumPtr[2] + 0x599D99AD) ^ (uint)Environment.ProcessId;

        return (int)(ptr - ((byte*)outPtr));
    }

    /// <summary>
    /// Write a single Nible 'value' (must be 0-15) to the byte buffer represented by *ptr.
    /// Will not go past 'endPtr'.  Also it assumes that we never write 0 so we can detect
    /// whether a nibble has already been written to ptr  because it will be nonzero.
    /// Thus if it is non-zero it adds to the current byte, otherwise it advances and writes
    /// the new byte (in the high bits) of the next byte.
    /// </summary>
    private static unsafe void WriteNibble(ref byte* ptr, byte* endPtr, uint value)
    {
        Debug.Assert(value < 16);
        Debug.Assert(ptr < endPtr);

        if (*ptr != 0)
            *ptr++ |= (byte)value;
        else
            *ptr = (byte)(value << 4);
    }
}
}