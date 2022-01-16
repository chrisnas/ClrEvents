#pragma once

#include <unordered_map>
#include <vector>
#include <string>

#include "IIpcEndpoint.h"
#include "NettraceFormat.h"

#include "BlockParser.h"


class EventCacheThread
{
public:
    uint32_t SequenceNumber;
    uint64_t LastCachedEventTimestamp;
};


// from https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/EventPipeFormat.md
#pragma pack(1)
struct StackBlockHeader
{
    uint32_t FirstId;
    uint32_t Count;
};


class EventCacheStack32
{
public:
    uint32_t Id;
    std::vector<uint32_t> Frames;
};

class EventCacheStack64
{
public:
    uint32_t Id;
    std::vector<uint64_t> Frames;
};


// TODO: define an interface IIEventPipeSession because it will propably
//       be used by the profilers pipeline. Maybe just mocking IIpcEndPoint could be enough
class EventPipeSession
{
public:
    EventPipeSession(bool is64Bit, IIpcEndpoint* pEndpoint, uint64_t sessionId);
    ~EventPipeSession();

    bool Listen();
    bool Stop();

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
    bool ParseStack(uint32_t stackId, DWORD& size);

    // sequence point block parsing helpers
    bool ParseSequencePointBlock(ObjectHeader& header);
    
    bool ExtractBlock(const char* blockName, uint32_t& blockSize, uint64_t& blockOriginInFile);
    bool ReadBlockSize(const char* blockName, uint32_t& blockSize);
    bool ReadCompressedHeader(EventBlobHeader& header, DWORD& size);
    bool ReadVarUInt32(uint32_t& val, DWORD& size);
    bool ReadVarUInt64(uint64_t& val, DWORD& size);
    bool ReadWString(std::wstring& wstring, DWORD& bytesRead);

    bool SkipBytes(DWORD byteCount);
    bool SkipPadding();
    bool SkipBlock(const char* blockName);

private:
    bool Is64Bit;
    IIpcEndpoint* _pEndpoint;
    uint64_t _sessionId;
    bool _stopRequested;

    
    // Keep track of the position since the beginning of the "file"
    // i.e. starting at 0 from the first character of the NettraceHeader
    //      Nettrace
    uint64_t _position;

    // buffer used to read each block that will be then parsed
    uint8_t* _pBlock;
    uint32_t _blockSize;

    // parsers
    MetadataParser _metadataParser;
    EventParser _eventParser;
    StackParser _stackParser;

    // per block header
    EventBlobHeader _blobHeader;

    // per thread event info
    std::unordered_map<uint64_t, EventCacheThread> _threads;

    // per metadataID event metadata description
    std::unordered_map<uint32_t, EventCacheMetadata> _metadata;

    // per stackID stack
    // only one will be used depending on the bitness of the monitored application
    std::unordered_map<uint32_t, EventCacheStack32> _stacks32;
    std::unordered_map<uint32_t, EventCacheStack64> _stacks64;
};

