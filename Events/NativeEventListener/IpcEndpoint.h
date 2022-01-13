#pragma once
#include <windows.h>
#include "IIpcEndpoint.h"


class IpcEndpoint : public IIpcEndpoint
{
public:
    IpcEndpoint();

    // Implements IIpcEndpoint interface
    virtual bool Write(LPCVOID buffer, DWORD bufferSize, DWORD* writtenBytes) override;
    virtual bool Read(LPVOID buffer, DWORD bufferSize, DWORD* readBytes) override;
    virtual bool ReadByte(uint8_t& byte) override;
    virtual bool ReadWord(uint16_t& word) override;
    virtual bool ReadDWord(uint32_t& dword) override;
    virtual bool ReadLong(uint64_t& ulong) override;

    virtual bool Close() = 0;

protected:
    ~IpcEndpoint();

protected:
    HANDLE _handle;
    uint8_t* _pCurrentBuffer;
    uint8_t* _pNextBuffer;
    DWORD _readBytes;
    
    // point to the first buffered byte to return
    uint16_t _pos;
};

