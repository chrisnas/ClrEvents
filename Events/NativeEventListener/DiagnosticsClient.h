#pragma once

#include "IIpcEndpoint.h"
#include "EventPipeSession.h"
#include "DiagnosticsProtocol.h"

class DiagnosticsClient
{
public:
    static DiagnosticsClient* Create(int pid);
    ~DiagnosticsClient();

    // Expose the available commands from the protocol
    //
 
    // PROCESS 
    bool GetProcessInfo(ProcessInfoRequest& request);

    // EVENTPIPE
    // Don't forget to call EventPipeSession::Stop() to send the Stop command 
    // and cancel the receiving of CLR events after EventPipeSession::Listen() is called
    EventPipeSession* OpenEventPipeSession(EventKeyword keywords, EventVerbosityLevel verbosity);

    // DUMP
    // PROFILE
    // COUNTER
    // 
private:
    DiagnosticsClient(int pid, IIpcEndpoint* pEndpoint);

private:
    int _pid;
    IIpcEndpoint* _pEndpoint;
};

