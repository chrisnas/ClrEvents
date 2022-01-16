#include <iostream>

#include "DiagnosticsProtocol.h"
#include "BlockParser.h"


// look at:
//  EventpipeEventBlock.ReadBlockContent()
bool EventParser::OnParseBlob(EventBlobHeader& header, bool isCompressed, DWORD& blobSize)
{
    if (isCompressed)
    {
        if (!ReadCompressedHeader(header, blobSize))
        {
            return false;
        }
    }
    else
    {
        if (!ReadCompressedHeader(header, blobSize))
        {
            return false;
        }
    }

    // TODO: uncomment to show blob header
    DumpBlobHeader(header);

    auto& metadataDef = _metadata[header.MetadataId];
    if (metadataDef.MetadataId == 0)
    {
        // this should never occur: no definition was previously received

        uint8_t* pBuffer = new uint8_t[header.PayloadSize];
        if (!Read(pBuffer, header.PayloadSize))
        {
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
bool EventParser::OnExceptionThrown(DWORD payloadSize, EventCacheMetadata& metadataDef)
{
    DWORD readBytesCount = 0;
    DWORD size = 0;

    // string: exception type
    // string: exception message
    // Note: followed by "instruction pointer" 
    //          could be 32 bit or 64 bit: how to figure out the bitness of the monitored application?
    std::wstring strBuffer;
    strBuffer.reserve(128);
    std::cout << "\nException thrown:\n";

    if (!ReadWString(strBuffer, size))
    {
        std::cout << "Error while reading exception thrown type name\n";
        return false;
    }
    readBytesCount += size;
    if (size == 2)
        std::wcout << L"   type    = ''\n";
    else
        std::wcout << L"   type    = " << strBuffer.c_str() << L"\n";

    strBuffer.clear();
    if (!ReadWString(strBuffer, size))
    {
        std::cout << "Error while reading exception thrown type name\n";
        return false;
    }
    readBytesCount += size;
    
    // handle empty string case
    if (size == 2)
        std::wcout << L"   message = ''\n";
    else
        std::wcout << L"   message = " << strBuffer.c_str() << L"\n";

    // skip the rest of the payload
    return SkipBytes(payloadSize - readBytesCount);
}



void DumpBlobHeader(EventBlobHeader& header)
{
    std::cout << "\nblob header:\n";
    std::cout << "   PayloadSize       = " << header.PayloadSize << "\n";
    std::cout << "   MetadataId        = " << header.MetadataId << "\n";
    std::cout << "   SequenceNumber    = " << header.SequenceNumber << "\n";
    std::cout << "   ThreadId          = " << header.ThreadId << "\n";
    std::cout << "   CaptureThreadId   = " << header.CaptureThreadId << "\n";
    std::cout << "   ProcessorNumber   = " << header.ProcessorNumber << "\n";
    std::cout << "   StackId           = " << header.StackId << "\n";
    std::cout << "   Timestamp         = " << header.Timestamp << "\n";
    std::cout << "   ActivityId        = "; DumpGuid(header.ActivityId); std::cout << "\n";
    std::cout << "   RelatedActivityId = "; DumpGuid(header.RelatedActivityId); std::cout << "\n";
}
