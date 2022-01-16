#pragma once

#include<stdint.h>
#include<unordered_map>
#include <windows.h>

#include "NettraceFormat.h"


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

void DumpMetadataDefinition(EventCacheMetadata metadataDef);
void DumpBlobHeader(EventBlobHeader& header);


// TODO: move it to .cpp when no more used in EventPipeSession.cpp
enum EventIDs : uint32_t
{
    AllocationTick = 10,
    ExceptionThrown = 80,
    ContentionStart = 81,
    ContentionStop = 91,
};



class BlockParser
{
public:
    BlockParser();
    bool Parse(uint8_t* pBlock, uint32_t bytesCount, uint64_t blockOriginInFile);

protected:
    virtual bool OnParse() = 0;

    // Access helpers
    bool Read(LPVOID buffer, DWORD bufferSize);
    bool ReadByte(uint8_t& byte);
    bool ReadWord(uint16_t& word);
    bool ReadDWord(uint32_t& dword);
    bool ReadLong(uint64_t& ulong);
    bool ReadVarUInt32(uint32_t& val, DWORD& size);
    bool ReadVarUInt64(uint64_t& val, DWORD& size);
    bool ReadWString(std::wstring& wstring, DWORD& bytesRead);
    bool SkipBytes(uint32_t byteCount);

private:
    bool CheckBoundaries(uint32_t byteCount);

protected:
    uint32_t _blockSize;
    uint32_t _pos;

private:
    uint8_t* _pBlock;
    uint64_t _blockOriginInFile;
};


class EventParserBase : public BlockParser
{
public:
    EventParserBase(std::unordered_map<uint32_t, EventCacheMetadata>& metadata);

protected:
    std::unordered_map<uint32_t, EventCacheMetadata>& _metadata;

protected:
    virtual bool OnParse();
    virtual bool OnParseBlob(EventBlobHeader& header, bool isCompressed, DWORD& blobSize) = 0;
    virtual const char* GetBlockName() = 0;

// helpers
protected:
    bool ReadCompressedHeader(EventBlobHeader& header, DWORD& size);
    bool ReadUncompressedHeader(EventBlobHeader& header, DWORD& size);
};


// Available block parsers
class MetadataParser : public EventParserBase
{
public:
    MetadataParser(std::unordered_map<uint32_t, EventCacheMetadata>& metadata) : EventParserBase(metadata) {}

protected:
    virtual bool OnParseBlob(EventBlobHeader& header, bool isCompressed, DWORD& blobSize);
    virtual const char* GetBlockName()
    {
        return "Metadata";
    }
};


class EventParser : public EventParserBase
{
// TODO: probably pass a IEventListener interface that contains OnException, OnAllocationTick,...
public:
    EventParser(std::unordered_map<uint32_t, EventCacheMetadata>& metadata) : EventParserBase(metadata) {}

protected:
    virtual bool OnParseBlob(EventBlobHeader& header, bool isCompressed, DWORD& blobSize);
    virtual const char* GetBlockName()
    {
        return "Event";
    }

// event handlers
private:
    bool OnExceptionThrown(DWORD payloadSize, EventCacheMetadata& metadataDef);
};


class StackParser : public BlockParser
{
protected:
    virtual bool OnParse();
};