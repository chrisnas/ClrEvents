#include <type_traits>
#include "IpcEndpoint.h"


// 2 buffers are allocated
const uint16_t BufferSize = 2 * 1024;  // 2 KB

IpcEndpoint::IpcEndpoint()
{
    _handle = 0;
    _pCurrentBuffer = new uint8_t[BufferSize];
    _pNextBuffer = new uint8_t[BufferSize];
    _readBytes = 0;
    _pos = 0;

}

IpcEndpoint::~IpcEndpoint()
{
    if (_pCurrentBuffer != nullptr)
        delete [] _pCurrentBuffer;
    if (_pNextBuffer != nullptr)
        delete [] _pNextBuffer;
}

// from CLR diagnosticsprotocol.h
template <typename T>
bool Parse(HANDLE handle, T& result)
{
    static_assert(
        std::is_integral<T>::value
        || std::is_same<T, float>::value
        || std::is_same<T, double>::value
        || std::is_same<T, CLSID>::value
        ,
        "Can only be instantiated with integral and floating point types.");

    DWORD readBytes = 0;
    return ::ReadFile(handle, &result, sizeof(result), &readBytes, nullptr);
}


// IIEndpoint interface implementation
//

bool IpcEndpoint::Write(LPCVOID buffer, DWORD bufferSize, DWORD* writtenBytes)
{
    DWORD bytesWrittenCount = 0;
    return ::WriteFile(_handle, buffer, bufferSize, writtenBytes, nullptr);
}

// TODO: add buffer management if performance it not good enough
//       read from the file/pipe by 2 KB buffer and then read from the memory buffer when needed
bool IpcEndpoint::Read(LPVOID buffer, DWORD bufferSize, DWORD* readBytes)
{
    uint8_t* pBuffer = static_cast<uint8_t*>(buffer);
    DWORD totalReadBytes = 0;
    while (totalReadBytes < bufferSize)
    {
        DWORD readBytes = 0;
        if (!::ReadFile(_handle, &(pBuffer[totalReadBytes]), bufferSize - totalReadBytes, &readBytes, nullptr))
        {
            return false;
        }
        totalReadBytes += readBytes;
    }

    *readBytes = totalReadBytes;
    return true;
    //// handle buffering
    //if (_pos == 0)
    //{
    //    // need to read at least bufferSize
    //    DWORD totalReadBytes = 0;
    //    while (totalReadBytes < bufferSize)
    //    {
    //        DWORD readBytes = 0;
    //        if (!::ReadFile(_handle, _pCurrentBuffer, BufferSize, &readBytes, nullptr))
    //        {
    //            return false;
    //        }
    //        totalReadBytes += readBytes;

    //    }
    //    _readBytes = totalReadBytes;



    //    return true;
    //}

    //return ::ReadFile(_handle, buffer, bufferSize, readBytes, nullptr);
}

bool IpcEndpoint::ReadByte(uint8_t& byte)
{
    return Parse(_handle, byte);
}

bool IpcEndpoint::ReadWord(uint16_t& word)
{
    return Parse(_handle, word);
}

bool IpcEndpoint::ReadDWord(uint32_t& dword)
{
    return Parse(_handle, dword);
}

bool IpcEndpoint::ReadLong(uint64_t& ulong)
{
    return Parse(_handle, ulong);
}

