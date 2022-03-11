// NativeEventListener.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include <iostream>
#include <stdio.h>
#include <stdlib.h>
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

    //// details of the named pipe
    //DumpNamedPipeInfo(hPipe, pszPipeName);

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

// -pid : pid
// -i   : input filename
// -o   : output filename
void ParseCommandLine(int argc, wchar_t* argv[], DWORD& pid, const wchar_t*& inputFilename, const wchar_t*& outputFilename)
{
    pid = -1;
    inputFilename = nullptr;
    outputFilename = nullptr;

    for (int i = 0; i < argc; i++)
    {
        if (lstrcmp(argv[i], L"-pid") == 0)
        {
            if (i + 1 == argc)
                return;
            i++;

            pid = wcstol(argv[i], nullptr, 10);
        }
        else
        if (lstrcmp(argv[i], L"-in") == 0)
        {
            if (i + 1 == argc)
                return;
            i++;

            inputFilename = argv[i];
        }
        else
        if (lstrcmp(argv[i], L"-out") == 0)
        {
            if (i + 1 == argc)
                return;
            i++;

            outputFilename = argv[i];
        }
    }
}


// 32 bit:  -pid 118048 -out d:\temp\diagnostics\recording.bin
// 64 bit:  -pid 125152 -out d:\temp\diagnostics\recording.bin
// -in d:\temp\diagnostics\record_5_exceptionsWithMissingMessage.bin
// -in d:\temp\diagnostics\record_exceptions_wcoutBroken.bin
int wmain(int argc, wchar_t* argv[])
{
    // simulator pid
    DWORD pid = -1;
    const wchar_t* inputFilename;
    const wchar_t* outputFilename;
    ParseCommandLine(argc, argv, pid, inputFilename, outputFilename);
    if ((pid == -1) && (inputFilename == nullptr))
    {
        std::cout << "Missing -pid <pid> or -in <recording filename>...\n";
        return -1;
    }

    //// direct connection and ProcessInfo command handling
    //BasicConnection(pid);
    //return 0;

    DiagnosticsClient* pClient = nullptr;
    DiagnosticsClient* pStopClient = nullptr;
    if (pid != -1)
    {
        pClient = DiagnosticsClient::Create(pid, outputFilename);
        pStopClient = DiagnosticsClient::Create(pid, nullptr);
    }
    else
    if (inputFilename != nullptr)
    {
        pClient = DiagnosticsClient::Create(inputFilename, outputFilename);

        // no need to stop a live session when replay a recorded session
        pStopClient = nullptr;
    }

    if (pClient == nullptr)
        return -1;

    //// get process information with DiagnosticsClient
    //ProcessInfoRequest request;
    //pClient->GetProcessInfo(request);
    //DumpProcessInfo(request);
    //// Note: it seems that the connection gets closed after the response so pClient can't be reused

    // listen to CLR events
    bool is64Bit = true; // TODO: need to figure out the bitness of monitored application (using the Process::ProcessInfo command)

    // TODO: pass an IIpcRecorder
    auto pSession = pClient->OpenEventPipeSession(
        is64Bit,
            EventKeyword::gc |
            EventKeyword::exception |
            EventKeyword::contention
            ,
        EventVerbosityLevel::Verbose  // required for AllocationTick
        );
    if (pSession != nullptr)
    {
        DWORD tid = 0;
        auto hThread = ::CreateThread(nullptr, 0, ListenToEvents, pSession, 0, &tid);

        std::cout << "Press ENTER to stop listening to events...\n\n";
        std::string line;
        std::getline(std::cin, line);

        std::cout << "Stopping session\n\n";
        pSession->Stop();

        // it is neeeded to use a different ipc connection to stop the pSession
        if (pStopClient != nullptr)
            pStopClient->StopEventPipeSession(pSession->SessionId);

        std::cout << "Session stopped\n\n";

        // test if it works
        ::Sleep(1000);

        ::CloseHandle(hThread);
    }

    delete pClient;

    std::cout << "Exit application\n\n";
    std::cout << "\n";
}
