// NativeEventListener.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include <iostream>
#include <stdio.h>
#include <string>
#include <windows.h>

#include "DiagnosticsClient.h"
#include "DiagnosticsProtocol.h"


void DumpNamedPipeInfo(HANDLE hPipe, LPCWSTR pszName)
{
    DWORD flags, outBufferSize, inBufferSize, maxInstances;

    // TODO: check GetNamedPipeHandleState()
    //
    if (!::GetNamedPipeInfo(hPipe, &flags, &outBufferSize, &inBufferSize, &maxInstances))
    {
        auto error = ::GetLastError();
        std::cout << "Error while getting named pipe information: 0x" << std::hex << error << std::dec << "\n";
        return;
    }

    std::cout << ".NET Diagnostic named pipe '";
    std::wcout << pszName;
    std::cout << "':\n";
    if ((PIPE_TYPE_MESSAGE & flags) == PIPE_TYPE_MESSAGE)
    {
        std::cout << "   type = message\n";
    }
    else
    {
        std::cout << "   type = byte\n";
    }
    std::cout << "   out  = " << outBufferSize << "\n";
    std::cout << "   in   = " << inBufferSize << "\n";
    std::cout << "   max  = " << maxInstances << "\n";
}

bool CheckNop(HANDLE hPipe)
{
    IpcHeader nopMessage =
    {
        { DotnetIpcMagic_V1 },
        (uint16_t)sizeof(IpcHeader),
        (uint8_t)DiagnosticServerCommandSet::Server,
        (uint8_t)DiagnosticServerResponseId::OK,
        (uint16_t)0x0000
    };

    DWORD bytesWrittenCount = 0;
    if (!::WriteFile(hPipe, &nopMessage, sizeof(nopMessage), &bytesWrittenCount, nullptr))
    {
        auto error = ::GetLastError();
        std::cout << "Error while sending NOP message to the CLR: 0x" << std::hex << error << std::dec << "\n";
        return false;
    }

    IpcHeader response = {};
    DWORD bytesReadCount = 0;
    if (!::ReadFile(hPipe, &response, sizeof(response), &bytesReadCount, nullptr))
    {
        auto error = ::GetLastError();
        std::cout << "Error while getting NOP response from the CLR: 0x" << std::hex << error << std::dec << "\n";
        return false;
    }

    std::cout << "Response from NOP ";
    if (response.CommandId == (uint8_t)DiagnosticServerResponseId::OK)
        std::cout << "is successful \n";
    else
        std::cout << "failed\n";

    return true;
}

void DumpProcessInfo(ProcessInfoRequest& request)
{
    std::cout << "\nProcessInfo Command\n";
    std::cout << "   pid      = " << request.Pid << "\n";
    std::cout << "   cookie   = ";
    DumpGuid(request.RuntimeCookie);
    std::cout << "\n";
    std::wcout << "   cmd line = " << request.CommandLine << "\n";
    std::wcout << "   OS       = " << request.OperatingSystem << "\n";
    std::wcout << "   Archi    = " << request.Architecture << "\n";
}

bool CheckProcessInfo(HANDLE hPipe)
{
    ProcessInfoRequest request;
    if (!request.Send(hPipe))
    {
        return false;
    }

    DumpProcessInfo(request);
    return true;
}

int BasicConnection(DWORD pid)
{
    // https://docs.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-createnamedpipew
    wchar_t pszPipeName[256];

    // build the pipe name as described in the protocol
    int nCharactersWritten = -1;
    nCharactersWritten = wsprintf(
        pszPipeName,
        L"\\\\.\\pipe\\dotnet-diagnostic-%d",
        pid
    );

    // check that CLR has created the diagnostics named pipe
    if (!::WaitNamedPipe(pszPipeName, 200))
    {
        auto error = ::GetLastError();
        std::cout << "Diagnostics named pipe is not available for process #" << pid << " (" << error << ")" << "\n";
        return -1;
    }

    // connect to the named pipe
    HANDLE hPipe;
    hPipe = ::CreateFile(
        pszPipeName,    // pipe name 
        GENERIC_READ |  // read and write access 
        GENERIC_WRITE,
        0,              // no sharing 
        NULL,           // default security attributes
        OPEN_EXISTING,  // opens existing pipe 
        0,              // default attributes 
        NULL);          // no template file 

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        std::cout << "Impossible to connect to " << pszPipeName << "\n";
        return -2;
    }

    // GetLastError() could be ERROR_PIPE_BUSY: should retry in that case

    // details of the named pipe
    DumpNamedPipeInfo(hPipe, pszPipeName);

    //// send NOP to CLR
    //if (!CheckNop(hPipe))
    //{
    //    return -4;
    //}

    // send ProcessInfo command to CLR
    if (!CheckProcessInfo(hPipe))
    {
        return -5;
    }

    // don't forget to close the named pipe
    ::CloseHandle(hPipe);

    return 0;
}


DWORD WINAPI ListenToEvents(void* pParam)
{
    EventPipeSession* pSession = static_cast<EventPipeSession*>(pParam);

    pSession->Listen();

    return 0;
}

int main()
{
    // simulator pid
    DWORD pid = 105028;

    //BasicConnection(pid);

    DiagnosticsClient* pClient = DiagnosticsClient::Create(pid);
    if (pClient == nullptr)
        return -1;

    //// get process information
    //ProcessInfoRequest request;
    //pClient->GetProcessInfo(request);
    //DumpProcessInfo(request);

    // listen to CLR exceptions events
    auto pSession = pClient->OpenEventPipeSession(EventKeyword::exception, EventVerbosityLevel::Error);
    if (pSession != nullptr)
    {
        DWORD tid = 0;
        auto hThread = ::CreateThread(nullptr, 0, ListenToEvents, pSession, 0, &tid);

        std::cout << "Press ENTER to stop listening to events...\n\n";
        std::string line;
        std::getline(std::cin, line);

        std::cout << "Stopping session\n\n";
        pSession->Stop();
        std::cout << "Session stopped\n\n";

        // test if it works
        ::Sleep(1000);

        ::CloseHandle(hThread);
    }

    delete pClient;

    std::cout << "Exit application\n\n";
    std::cout << "\n";
}
