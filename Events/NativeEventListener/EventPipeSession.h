#pragma once
#include <unordered_map>
#include <string>

#include "IIpcEndpoint.h"


enum class ObjectType : uint8_t
{
    Unknown = 0,
    Trace,
    EventBlock,
    MetadataBlock,
    StackBlock,
    SequencePointBlock,
};

// from FastSerialization.Tag
enum NettraceTag : uint8_t
{
    // from format spec - https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/EventPipeFormat.md
    NullReference = 1,
    BeginPrivateObject = 5,
    EndObject = 6,

    Error = 0,
    ObjectReference = 2,
    ForwardReference = 3,
    BeginObject = 4,
    ForwardDefinition = 7,
    Byte = 8,
    Int16 = 9,
    Int32 = 10,
    Int64 = 11,
    SkipRegion = 12,
    String = 13,
    Blob = 14,
    Limit = 15
};

#pragma pack(1)
struct ObjectHeader
{
    NettraceTag TagTraceObject;         // 5
    NettraceTag TagTypeObjectForTrace;  // 5
    NettraceTag TagType;                // 1
    uint32_t Version;                   // 
    uint32_t MinReaderVersion;          // 
    uint32_t NameLength;                // length of UTF8 name that follows
};


// filled up by EventPipeEventSource.FromStream(Deserializer)
#pragma pack(1)
struct ObjectFields
{
    uint16_t Year;
    uint16_t Month;
    uint16_t DayOfWeek;
    uint16_t Day;
    uint16_t Hour;
    uint16_t Minute;
    uint16_t Second;
    uint16_t Millisecond;
    uint64_t SyncTimeQPC;
    uint64_t QPCFrequency;
    uint32_t PointerSize;
    uint32_t ProcessId;
    uint32_t NumProcessors;
    uint32_t ExpectedCPUSamplingRate;
};


// from https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/EventPipeFormat.md
#pragma pack(1)
struct EventBlockHeader
{
    uint16_t HeaderSize;
    uint16_t Flags;
    uint64_t MinTimespamp;
    uint64_t MaxTimespamp;

    // some optional reserved space might be following
};


// from https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/EventPipeFormat.md
//


#pragma pack(1)
struct EventBlobHeader_V3
{
    uint32_t EventSize;
    uint32_t MetaDataId;
    uint32_t ThreadId;
    uint64_t TimeStamp;
    GUID ActivityID;
    GUID RelatedActivityID;
    uint32_t PayloadSize;
};

#pragma pack(1)
struct EventBlobHeader_V4
{
    uint32_t EventSize;
    uint32_t MetadataId;
    uint32_t SequenceNumber;
    uint64_t ThreadId;
    uint64_t CaptureThreadId;
    uint32_t ProcessorNumber;
    uint32_t StackId;
    uint64_t Timestamp;
    GUID ActityId;
    GUID RelatedActivityId;
    uint32_t PayloadSize;
};

struct EventBlobHeader : EventBlobHeader_V4
{
    bool IsSorted;
    uint32_t PayloadSize;
    uint32_t HeaderSize;
    uint32_t TotalNonHeaderSize;
};

class EventCacheThread
{
public:
    uint32_t SequenceNumber;
    uint64_t LastCachedEventTimestamp;
};

class EventCacheMetadata
{
public:
    uint32_t MetadataId;
    std::wstring ProviderName;
    uint32_t EventId;
    std::wstring EventName; // could be empty
    uint64_t Keywords;
    uint32_t Version;
    uint32_t Level;
};

class EventPipeSession
{
public:
    EventPipeSession(IIpcEndpoint* pEndpoint, uint64_t sessionId);
    void Listen();
    void Stop();

public:
    DWORD Error;

private:
    EventPipeSession();

    // helper functions that keep track of the current position
    // since the beginning of the "file"
    bool Read(LPVOID buffer, DWORD bufferSize);
    bool ReadByte(uint8_t& byte);
    bool ReadWord(uint16_t& word);
    bool ReadDWord(uint32_t& dword);
    bool ReadLong(uint64_t& ulong);

    // objects parsing helpers
    bool ReadHeader();
    bool ReadTraceObjectHeader();
    bool ReadObjectFields(ObjectFields& objectFields);
    bool ReadNextObject();
    ObjectType GetObjectType(ObjectHeader& header);
    
    // event block parsing helpers
    bool ParseEventBlock(ObjectHeader& header);
    bool ParseEventBlob(bool isCompressed, DWORD& blobSize);
    // event parsing helpers
    bool OnExceptionThrown(DWORD payloadSize, EventCacheMetadata& metadataDef);

    // metadata block parsing helpers
    bool ParseMetadataBlock(ObjectHeader& header);
    bool ParseMetadataBlob(bool isCompressed, DWORD& blobSize);

    // stack block parsing helpers
    bool ParseStackBlock(ObjectHeader& header);

    // sequence point block parsing helpers
    bool ParseSequencePointBlock(ObjectHeader& header);
    
    bool ReadBlockSize(const char* blockName, uint32_t& blockSize);
    bool ReadCompressedHeader(EventBlobHeader& header, DWORD& size);
    bool ReadVarUInt32(uint32_t& val, DWORD& size);
    bool ReadVarUInt64(uint64_t& val, DWORD& size);
    bool ReadWString(std::wstring& wstring, DWORD& bytesRead);
    
    // dump if title is not null
    bool SkipBytes(DWORD byteCount);
    bool SkipPadding();
    bool SkipBlock(const char* blockName);

private:
    IIpcEndpoint* _pEndpoint;
    uint64_t _sessionId;
    bool _stopRequested;
    
    // Keep track of the position since the beginning of the "file"
    // i.e. starting at 0 from the first character of the NettraceHeader
    //      Nettrace
    uint64_t _position;

    // per block header
    EventBlobHeader _blobHeader;

    // per thread event info
    std::unordered_map<uint64_t, EventCacheThread> _threads;

    // per metadataID event metadata description
    std::unordered_map<uint32_t, EventCacheMetadata> _metadata;
};

