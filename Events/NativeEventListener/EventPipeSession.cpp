#include <iostream>

#include "EventPipeSession.h"
#include "DiagnosticsProtocol.h"

// from https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/EventPipeFormat.md
// 
// The header is formed by:
//  "Nettrace" in ASCII (no final \0)
//  20 as uint32_t (length of following string)
//  "!FastSerialization.1" in ASCII

#pragma pack(1)
struct NettraceHeader
{
    uint8_t Magic[8];               // "Nettrace" with not '\0'
    uint32_t FastSerializationLen;  // 20
    uint8_t FastSerialization[20];  // "!FastSerialization.1" with not '\0'
};

const char* NettraceHeaderMagic = "Nettrace";
const char* FastSerializationMagic = "!FastSerialization.1";

bool IsSameAsString(uint8_t* bytes, uint16_t length, const char* characters)
{
    return memcmp(bytes, characters, length) == 0;
}

bool CheckNettraceHeader(NettraceHeader& header)
{
    if (!IsSameAsString(header.Magic, sizeof(header.Magic), NettraceHeaderMagic))
        return false;

    if (header.FastSerializationLen != strlen(FastSerializationMagic))
        return false;

    if (!IsSameAsString(header.FastSerialization, sizeof(header.FastSerialization), FastSerializationMagic))
        return false;

    return true;
};


// from https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/EventPipeFormat.md
// 
// The TraceObject header is formed by an ObjectHeader followed by:
//  "Trace" in ASCII (no final \0)

#pragma pack(1)
struct TraceObjectHeader : ObjectHeader
{
  //NettraceTag TagTraceObject;         // 5
  //NettraceTag TagTypeObjectForTrace;  // 5
  //NettraceTag TagType;                // 1
  //uint32_t Version;                   // 4
  //uint32_t MinReaderVersion;          // 4
  //uint32_t NameLength;                // 5
    uint8_t Name[5];                    // 'Trace'
    NettraceTag TagEndTraceObject;      // 6
};

bool CheckTraceObjectHeader(TraceObjectHeader& header)
{
    if (header.TagTraceObject != NettraceTag::BeginPrivateObject) return false;
    if (header.TagTypeObjectForTrace != NettraceTag::BeginPrivateObject) return false;
    if (header.TagType != NettraceTag::NullReference) return false;

    if (header.Version != 4) return false;
    if (header.MinReaderVersion != 4) return false;

    if (header.NameLength != 5) return false;
    if (!IsSameAsString(header.Name, sizeof(header.Name), "Trace")) return false;

    if (header.TagEndTraceObject != NettraceTag::EndObject) return false;

    return true;
}



EventPipeSession::EventPipeSession()
{
    Error = 0;

    _position = 0;
    _pEndpoint = nullptr;
    _sessionId = 0;
    _stopRequested = false;
    _blobHeader = {};
}

EventPipeSession::EventPipeSession(IIpcEndpoint* pEndpoint, uint64_t sessionId)
{
    Error = 0;

    _position = 0;
    _pEndpoint = pEndpoint;
    _sessionId = sessionId;
    _stopRequested = false;
    _blobHeader = {};
}

void EventPipeSession::Listen()
{
    if (!ReadHeader())
        return;

    if (!ReadTraceObjectHeader())
        return;

    ObjectFields ofTrace;
    if (!ReadObjectFields(ofTrace))
        return;

    // don't forget to check the end object tag
    uint8_t tag;
    if (!ReadByte(tag) || (tag != NettraceTag::EndObject))
        return;

    while (ReadNextObject() && !_stopRequested)
    {
        std::cout << "------------------------------------------------\n";
    }
    
    // ??? the response to the StopTracing command could be received here: do we need to send it using another endpoint?
}


bool EventPipeSession::Read(LPVOID buffer, DWORD bufferSize)
{
    DWORD readBytes = 0;
    auto success = _pEndpoint->Read(buffer, bufferSize, &readBytes);
    if (success)
        _position += readBytes;

    return success;
}

bool EventPipeSession::ReadByte(uint8_t& byte)
{
    return Read(&byte, sizeof(uint8_t));
}

bool EventPipeSession::ReadWord(uint16_t& word)
{
    return Read(&word, sizeof(uint16_t));
}

bool EventPipeSession::ReadDWord(uint32_t& dword)
{
    return Read(&dword, sizeof(uint32_t));
}

bool EventPipeSession::ReadLong(uint64_t& ulong)
{
    return Read(&ulong, sizeof(uint64_t));
}

bool EventPipeSession::ReadHeader()
{
    NettraceHeader header;
    if (!Read(&header, sizeof(header)))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading Nettrace header: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }

    return CheckNettraceHeader(header);
}

bool EventPipeSession::ReadTraceObjectHeader()
{
    TraceObjectHeader header;
    if (!Read(&header, sizeof(header)))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading Trace Object header: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }

    return CheckTraceObjectHeader(header);
}

bool EventPipeSession::ReadObjectFields(ObjectFields& objectFields)
{
    if (!Read(&objectFields, sizeof(objectFields)))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading Object fields: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }

    return true;
}

bool EventPipeSession::SkipPadding()
{
    if (_position % 4 != 0)
    {
        // need to skip the padding
        uint8_t paddingLength = 4 - (_position % 4);
        uint8_t padding[4];
        if (!Read(padding, paddingLength))
        {
            Error = ::GetLastError();
            std::cout << "Error while skipping padding (" << paddingLength << " bytes): 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }
    }

    return true;
}


// look at FastSerialization implementation with a decompiler:
//  .ReadObject() 
//  .ReadObjectDefinition()
bool EventPipeSession::ReadNextObject()
{
    // get the type of object from the header
    ObjectHeader header;
    if (!Read(&header, sizeof(ObjectHeader)))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading Object header: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }

    ObjectType ot = GetObjectType(header);
    if (ot == ObjectType::Unknown)
        return false;

    // don't forget to check the end object tag
    uint8_t tag;
    if (!ReadByte(tag) || (tag != NettraceTag::EndObject))
    {
        return false;
    }

    switch (ot)
    {
        case ObjectType::EventBlock:
            return ParseEventBlock(header);
        case ObjectType::MetadataBlock:
            return ParseMetadataBlock(header);
        case ObjectType::StackBlock:
            return ParseStackBlock(header);
        case ObjectType::SequencePointBlock:
            return ParseSequencePointBlock(header);

        default:
            return false;
    }
}

const char* EventBlockName = "EventBlock";
const char* MetadataBlockName = "MetadataBlock";
const char* StackBlockName = "StackBlock";
const char* SequencePointBlockName = "SPBlock";

ObjectType EventPipeSession::GetObjectType(ObjectHeader& header)
{
    // check validity
    if (header.TagTraceObject != NettraceTag::BeginPrivateObject) return ObjectType::Unknown;
    if (header.TagTypeObjectForTrace != NettraceTag::BeginPrivateObject) return ObjectType::Unknown;
    if (header.TagType != NettraceTag::NullReference) return ObjectType::Unknown;

    // figure out which type it is based on the name:
    //   EventBlock -> "EventBlock"  (size = 10)
    //   MetadataBlock -> "MetadataBlock" (size = 13)
    //   StackBlock -> "StackBlock" (size = 10)
    //   SequencePointBlock -> "SPBlock" (size = 7)
    if (header.NameLength == 13)
    {
        uint8_t buffer[13];
        if (!Read(buffer, 13))
            return ObjectType::Unknown;
        
        if (IsSameAsString(buffer, 13, MetadataBlockName))
            return ObjectType::MetadataBlock;

        return ObjectType::Unknown;
    }
    else
    if (header.NameLength == 10)
    {
        uint8_t buffer[10];
        if (!Read(buffer, 10))
            return ObjectType::Unknown;

        if (IsSameAsString(buffer, 10, EventBlockName))
            return ObjectType::EventBlock;
        else
        if (IsSameAsString(buffer, 10, StackBlockName))
            return ObjectType::StackBlock;

        return ObjectType::Unknown;
    }
    else
    if (header.NameLength == 7)
    {
        uint8_t buffer[7];
        if (!Read(buffer, 7))
            return ObjectType::Unknown;

        if (IsSameAsString(buffer, 7, SequencePointBlockName))
            return ObjectType::SequencePointBlock;

        return ObjectType::Unknown;
    }

    return ObjectType::Unknown;
}


bool EventPipeSession::ReadBlockSize(const char* blockName, uint32_t& blockSize)
{
    if (!ReadDWord(blockSize))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading " << blockName << " block size: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    // Note: blockSizeInBytes does not include padding bytes to ensure alignment.

    // the rest of the block must be 4 bytes aligned with the beginning of the file
    if (!SkipPadding())
        return false;

    return true;
}



// look at:
//  Microsoft.Diagnostics.Tracing.EventPipeEventHeader.ReadFromFormatV4()
//  https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/EventPipeEventSource.cs
enum CompressedHeaderFlags
{
    MetadataId                  = 1 << 0,
    CaptureThreadAndSequence    = 1 << 1,
    ThreadId                    = 1 << 2,
    StackId                     = 1 << 3,
    ActivityId                  = 1 << 4,
    RelatedActivityId           = 1 << 5,
    Sorted                      = 1 << 6,
    DataLength                  = 1 << 7
};

bool EventPipeSession::ReadVarUInt32(uint32_t& val, DWORD& size)
{
    int shift = 0;
    byte b;
    do
    {
        if (shift == 5 * 7)
        {
            // TODO: specific Error code
            return false;
        }

        if (!ReadByte(b))
        {
            return false;
        }
        size++;
        val |= (uint32_t)(b & 0x7f) << shift;
        shift += 7;
    } while ((b & 0x80) != 0);

    return true;
}

bool EventPipeSession::ReadVarUInt64(uint64_t& val, DWORD& size)
{
    int shift = 0;
    byte b;
    do
    {
        if (shift == 10 * 7)
        {
            // TODO: specific Error code
            return false;
        }
        if (!ReadByte(b))
        {
            return false;
        }
        size++;
        val |= (uint64_t)(b & 0x7f) << shift;
        shift += 7;
    } while ((b & 0x80) != 0);

    return true;
}

bool EventPipeSession::ReadCompressedHeader(EventBlobHeader& header, DWORD& size)
{
    // used to compute the compressed header size
    uint32_t headerStartPos = _position;

    // read Flags byte
    uint8_t flags;
    if (!ReadByte(flags))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading compressed header flags: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    size++;

    if ((flags & CompressedHeaderFlags::MetadataId) != 0)
    {
        if (!ReadVarUInt32(header.MetadataId, size))
        {
            Error = ::GetLastError();
            std::cout << "Error while reading compressed header metadata ID: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }
    }

    if ((flags & CompressedHeaderFlags::CaptureThreadAndSequence) != 0)
    {
        uint32_t val;
        if (!ReadVarUInt32(val, size))
        {
            Error = ::GetLastError();
            std::cout << "Error while reading compressed header sequence number: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }
        header.SequenceNumber += val + 1;

        if (!ReadVarUInt64(header.CaptureThreadId, size))
        {
            Error = ::GetLastError();
            std::cout << "Error while reading compressed header captured thread ID: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }

        if (!ReadVarUInt32(val, size))
        {
            Error = ::GetLastError();
            std::cout << "Error while reading compressed header sequence number: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }
        header.CaptureThreadId = val;
    }
    else
    {
        if (header.MetadataId != 0)
        {
            // WEIRD because this field has not been initialized yet :^(
            header.SequenceNumber++;
        }
    }

    if ((flags & CompressedHeaderFlags::ThreadId) != 0)
    {
        if (!ReadVarUInt64(header.ThreadId, size))
        {
            Error = ::GetLastError();
            std::cout << "Error while reading compressed header thread ID: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }
    }

    if ((flags & CompressedHeaderFlags::StackId) != 0)
    {
        if (!ReadVarUInt32(header.StackId, size))
        {
            Error = ::GetLastError();
            std::cout << "Error while reading compressed header stack ID: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }
    }

    uint64_t timestampDelta = 0;
    if (!ReadVarUInt64(timestampDelta, size))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading compressed header timestamp delta: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    header.Timestamp += timestampDelta;

    if ((flags & CompressedHeaderFlags::ActivityId) != 0)
    {
        if (!Read(&header.ActityId, sizeof(header.ActityId)))
        {
            Error = ::GetLastError();
            std::cout << "Error while reading compressed header activity ID: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }

        size += sizeof(header.ActityId);
    }

    if ((flags & (byte)CompressedHeaderFlags::RelatedActivityId) != 0)
    {
        if (!Read(&header.RelatedActivityId, sizeof(header.RelatedActivityId)))
        {
            Error = ::GetLastError();
            std::cout << "Error while reading compressed header related activity ID: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }

        size += sizeof(header.RelatedActivityId);
    }

    header.IsSorted = (flags & CompressedHeaderFlags::Sorted) != 0;

    if ((flags & CompressedHeaderFlags::DataLength) != 0)
    {
        if (!ReadVarUInt32(header.PayloadSize, size))
        {
            Error = ::GetLastError();
            std::cout << "Error while reading compressed header payload size: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }
    }

    header.HeaderSize = _position - headerStartPos;
    header.TotalNonHeaderSize = header.PayloadSize;

    return true;
}

void DumpBlobHeader(EventBlobHeader& header)
{
    std::cout << "\nblob header:\n";
    std::cout << "   EventSize         = " << header.EventSize << "\n";
    std::cout << "   MetadataId        = " << header.MetadataId << "\n";
    std::cout << "   SequenceNumber    = " << header.SequenceNumber << "\n";
    std::cout << "   ThreadId          = " << header.ThreadId << "\n";
    std::cout << "   CaptureThreadId   = " << header.CaptureThreadId << "\n";
    std::cout << "   ProcessorNumber   = " << header.ProcessorNumber << "\n";
    std::cout << "   StackId           = " << header.StackId << "\n";
    std::cout << "   Timestamp         = " << header.Timestamp << "\n";
    std::cout << "   ActityId          = "; DumpGuid(header.ActityId); std::cout << "\n";
    std::cout << "   RelatedActivityId = "; DumpGuid(header.RelatedActivityId); std::cout << "\n";
}


enum EventIDs : uint32_t
{
    AllocationTick  = 10,
    ExceptionThrown = 80,
    ContentionStart = 81,
    ContentionStop  = 91,
};

// look at:
//      Microsoft.Diagnostics.Tracing.EventPipe.EventCache.ProcessEventBlock()
bool EventPipeSession::ParseEventBlob(bool isCompressed, DWORD& blobSize)
{
    EventBlobHeader header = {};
    
    if (isCompressed)
    {
        ReadCompressedHeader(header, blobSize);
    }
    else
    {
        // TODO: read directly from the stream
    }

    //// TODO: uncomment to show blob header
    //DumpBlobHeader(header);

    auto& metadataDef = _metadata[header.MetadataId];
    if (metadataDef.MetadataId == 0)
    {
        // this should never occur: no definition was previously received

        uint8_t* pBuffer = new uint8_t[header.PayloadSize];
        if (!Read(pBuffer, header.PayloadSize))
        {
            Error = ::GetLastError();
            std::cout << "Error while reading EventBlob payload: 0x" << std::hex << Error << std::dec << "\n";

            delete[] pBuffer;
            return false;
        }

        std::cout << "Event blob\n";
        DumpBuffer(pBuffer, header.PayloadSize);
        delete[] pBuffer;
    }

    switch (metadataDef.EventId)
    {
        case EventIDs::ExceptionThrown:
            if (!OnExceptionThrown(header.PayloadSize, metadataDef))
            {
                return false;
            }
        break;

        default:
        {
            std::cout << "Event = " << metadataDef.EventId << "\n";
            SkipBytes(header.PayloadSize);
        }
    }


    blobSize += header.PayloadSize;

    return true;
}

// from https://docs.microsoft.com/en-us/dotnet/framework/performance/exception-thrown-v1-etw-event
bool EventPipeSession::OnExceptionThrown(DWORD payloadSize, EventCacheMetadata& metadataDef)
{
    DWORD readBytesCount = 0;
    DWORD size = 0;

    // string: exception type
    // string: exception message
    // Note: followed by "instruction pointer" 
    //          could be 32 bit or 64 bit: how to figure out the bitness of the monitored application?
    std::wstring strBuffer;
    strBuffer.reserve(128);
    std::wcout << "\nException thrown:\n";

    if (!ReadWString(strBuffer, size))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading exception thrown type name: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    readBytesCount += size;
    std::wcout << "   type    = " << strBuffer << "\n";

    strBuffer.clear();
    if (!ReadWString(strBuffer, size))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading exception thrown type name: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    readBytesCount += size;
    std::wcout << "   message = " << strBuffer << "\n";

    // skip the rest of the payload
    return SkipBytes(payloadSize - readBytesCount);
}


// look at:
//  EventpipeEventBlock.ReadBlockContent()
bool EventPipeSession::ParseEventBlock(ObjectHeader& header)
{
    if (header.Version != 2) return false;
    if (header.MinReaderVersion != 2) return false;

    // TODO: uncomment to dump the event block
    //return SkipBlock("Event");


    // get the block size
    uint32_t blockSize = 0;
    if (!ReadBlockSize("Event", blockSize))
        return false;

    // read event block header
    EventBlockHeader ebHeader = {};
    if (!Read(&ebHeader, sizeof(ebHeader)))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading EventBlock header: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }

    // skip any optional content if any
    if (ebHeader.HeaderSize > sizeof(EventBlockHeader))
    {
        uint8_t optionalSize = ebHeader.HeaderSize - sizeof(EventBlockHeader);
        uint8_t* pBuffer = new uint8_t[optionalSize];
        if (!Read(pBuffer, optionalSize))
        {
            Error = ::GetLastError();
            std::cout << "Error while skipping optional info from EventBlock header: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }
    }

    // from https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/EventPipeFormat.md
    // the rest of the block is a list of Event blobs
    // 
    DWORD blobSize = 0;
    DWORD totalBlobSize = 0;
    DWORD remainingBlockSize = blockSize - ebHeader.HeaderSize;
    bool isCompressed = ((ebHeader.Flags & 1) == 1);
    ::ZeroMemory(&_blobHeader, sizeof(_blobHeader));
    while (ParseEventBlob(isCompressed, blobSize))
    {
        totalBlobSize += blobSize;

        if (totalBlobSize >= remainingBlockSize - 1) // try to detect last blob
        {
            // don't forget to check the end block tag
            uint8_t tag;
            if (!ReadByte(tag) || (tag != NettraceTag::EndObject))
            {
                return false;
            }

            return true;
        }
    }

    return false;
}


// look at implementation:
//  TraceEventNativeMethods.EVENT_RECORD* ReadEvent() implementation
//  EventPipeBlock.FromStream(Deserializer)
bool EventPipeSession::ParseMetadataBlock(ObjectHeader& header)
{
    if (header.Version != 2) return false;
    if (header.MinReaderVersion != 2) return false;

    // TODO: uncomment to dump metadata block
    //return SkipBlock("Metadata");

    // TODO: this is the same code as in ParseEventBlock
    // get the block size
    uint32_t blockSize = 0;
    if (!ReadBlockSize("Metadata", blockSize))
        return false;

    // read event block header
    EventBlockHeader ebHeader = {};
    if (!Read(&ebHeader, sizeof(ebHeader)))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading MetadataBlock header: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }

    // skip any optional content if any
    if (ebHeader.HeaderSize > sizeof(EventBlockHeader))
    {
        uint8_t optionalSize = ebHeader.HeaderSize - sizeof(EventBlockHeader);
        uint8_t* pBuffer = new uint8_t[optionalSize];
        if (!Read(pBuffer, optionalSize))
        {
            Error = ::GetLastError();
            std::cout << "Error while skipping optional info from MetadataBlock header: 0x" << std::hex << Error << std::dec << "\n";
            return false;
        }
    }

    // from https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/EventPipeFormat.md
    // the rest of the block is a list of Event blobs
    // 
    DWORD blobSize = 0;
    DWORD totalBlobSize = 0;
    DWORD remainingBlockSize = blockSize - ebHeader.HeaderSize;
    bool isCompressed = ((ebHeader.Flags & 1) == 1);
    while (ParseMetadataBlob(isCompressed, blobSize))
    {
        totalBlobSize += blobSize;

        if (totalBlobSize >= remainingBlockSize - 1) // try to detect last blob
        {
            // don't forget to check the end block tag
            uint8_t tag;
            if (!ReadByte(tag) || (tag != NettraceTag::EndObject))
            {
                return false;
            }

            return true;
        }
    }

    return false;
}

const std::wstring DotnetRuntimeProvider = L"Microsoft-Windows-DotNETRuntime";
const std::wstring EventPipeProvider = L"Microsoft-DotNETCore-EventPipe";

bool EventPipeSession::ParseMetadataBlob(bool isCompressed, DWORD& blobSize)
{
    EventBlobHeader header = {};

    if (isCompressed)
    {
        ReadCompressedHeader(header, blobSize);
    }
    else
    {
        // TODO: read directly from the stream
    }

    //// TODO: uncomment to show blob header
    //DumpBlobHeader(header);
    //
    //// dump the payload
    //uint8_t* pBuffer = new uint8_t[header.PayloadSize];
    //if (!Read(pBuffer, header.PayloadSize))
    //{
    //    Error = ::GetLastError();
    //    std::cout << "Error while reading MetadataBlob payload: 0x" << std::hex << Error << std::dec << "\n";
    //    return false;
    //}
    //
    //std::cout << "Metadata blob\n";
    //DumpBuffer(pBuffer, header.PayloadSize);
    //delete[] pBuffer;
    //return true;

    // keep track of the only read bytes in the payload
    DWORD readBytesCount = 0;
    DWORD size = 0;

    // from https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/EventPipeFormat.md
    // A metadata blob is supposed to contain:
    //  
    //  int MetaDataId;      // The Meta-Data ID that is being defined.
    //  string ProviderName; // The 2 byte Unicode, null terminated string representing the Name of the Provider (e.g. EventSource)
    //  int EventId;         // A small number that uniquely represents this Event within this provider.  
    //  string EventName;    // The 2 byte Unicode, null terminated string representing the Name of the Event
    //  long Keywords;       // 64 bit set of groups (keywords) that this event belongs to.
    //  int Version          // The version number for this event.
    //  int Level;           // The verbosity (5 is verbose, 1 is only critical) for the event.
    //
    
    uint32_t metadataId;
    if (!ReadDWord(metadataId))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading metadata provider name: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    readBytesCount += sizeof(metadataId);

    auto& metadataDef = _metadata[metadataId];
    metadataDef.MetadataId = metadataId;
    
    // look for the provider name
    metadataDef.ProviderName.reserve(48);  // no provider name longer than 32+ characters
    if (!ReadWString(metadataDef.ProviderName, size))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading metadata provider name: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    readBytesCount += size;

    if (!ReadDWord(metadataDef.EventId))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading metadata event ID: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    readBytesCount += sizeof(metadataDef.EventId);

    // could be empty
    if (!ReadWString(metadataDef.EventName, size))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading metadata event name: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    readBytesCount += size;

    if (!ReadLong(metadataDef.Keywords))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading metadata keywords: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    readBytesCount += sizeof(metadataDef.Keywords);

    if (!ReadDWord(metadataDef.Version))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading metadata version: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    readBytesCount += sizeof(metadataDef.Version);

    if (!ReadDWord(metadataDef.Level))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading metadata level: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    readBytesCount += sizeof(metadataDef.Level);

    std::cout << "\nMetadata definition:\n";
    std::cout << "   Provider: ";
    std::wcout << metadataDef.ProviderName.c_str();
    std::cout << "\n";
    std::cout << "   Name    : ";
    std::wcout << metadataDef.EventName.c_str();
    std::cout << "\n";
    std::cout << "   ID      : " << metadataDef.EventId << "\n";
    std::cout << "   Version : " << metadataDef.Version << "\n";
    std::cout << "   Keywords: 0x" << std::hex << metadataDef.Keywords << std::dec << "\n";
    std::cout << "   Level   : " << metadataDef.Level << "\n";

    // skip remaining payload
    SkipBytes(header.PayloadSize - readBytesCount);

    blobSize += header.PayloadSize;
    return true;
}

bool EventPipeSession::ParseStackBlock(ObjectHeader& header)
{
    if (header.Version != 2) return false;
    if (header.MinReaderVersion != 2) return false;

    return SkipBlock("Stack");
}

bool EventPipeSession::ParseSequencePointBlock(ObjectHeader& header)
{
    if (header.Version != 2) return false;
    if (header.MinReaderVersion != 2) return false;

    return SkipBlock("SequencePoint");
}


bool EventPipeSession::SkipBlock(const char* blockName)
{
    // get the block size
    uint32_t blockSize = 0;
    if (!ReadBlockSize(blockName, blockSize))
        return false;

    // skip the block + final EndOfObject tag
    blockSize++;
    uint8_t* pBuffer = new uint8_t[blockSize];
    if (!Read(pBuffer, blockSize))
    {
        Error = ::GetLastError();
        std::cout << "Error while reading " << blockName << " block: 0x" << std::hex << Error << std::dec << "\n";
        return false;
    }
    std::cout << "\n" << blockName << " block (" << blockSize << " bytes)\n";
    DumpBuffer(pBuffer, blockSize);
    delete[] pBuffer;

    return true;
}

bool EventPipeSession::SkipBytes(DWORD byteCount)
{
    // use the stack for small buffer (no need to delete)
    uint8_t* pBuffer = static_cast<uint8_t*>(_alloca(byteCount));
    auto success = Read(pBuffer, byteCount);
    if (success)
    {
        std::cout << "skip " << byteCount << " bytes\n";
        DumpBuffer(pBuffer, byteCount);
    }

    return success;
}

// read UTF16 character one after another until the \0 us found to rebuild a string
bool EventPipeSession::ReadWString(std::wstring& wstring, DWORD& bytesRead)
{
    uint16_t character;
    bytesRead = 0;  // in case of empty string
    while (true)
    {
        if (!ReadWord(character))
        {
            return false;
        }
        bytesRead += sizeof(character);

        // Note that an empty string contains only that \0 character
        if (character == 0) // \0 final character of the string
            return true;

        wstring.push_back(character);
    }
}



StopSessionMessage* CreateStopMessage(uint64_t sessionId)
{
    auto message = new StopSessionMessage();
    ::ZeroMemory(message, sizeof(message));
    memcpy(message->Magic, &DotnetIpcMagic_V1, sizeof(message->Magic));
    message->Size = sizeof(StartSessionMessage);
    message->CommandSet = (uint8_t)DiagnosticServerCommandSet::EventPipe;
    message->CommandId = (uint8_t)EventPipeCommandId::CollectTracing2;
    message->Reserved = 0;
    message->SessionId = sessionId;

    return message;
}

void EventPipeSession::Stop()
{
    _stopRequested = true;

    auto message = CreateStopMessage(_sessionId);
    DWORD writtenBytes;
    if (!_pEndpoint->Write(&message, sizeof(message), &writtenBytes))
    {
        auto error = ::GetLastError();
        std::cout << "Error while sending EventPipe stop message to the CLR: 0x" << std::hex << error << std::dec << "\n";
        return;
    }
}

