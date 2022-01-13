#include "DiagnosticsClient.h"
#include "PidEndpoint.h"

DiagnosticsClient* DiagnosticsClient::Create(int pid)
{
    IIpcEndpoint* pEndpoint = PidEndpoint::Create(pid);
    if (pEndpoint == nullptr)
        return nullptr;

    return new DiagnosticsClient(pid, pEndpoint);
}


DiagnosticsClient::DiagnosticsClient(int pid, IIpcEndpoint* pEndpoint)
{
    _pid = pid;
    _pEndpoint = pEndpoint;
}

DiagnosticsClient::~DiagnosticsClient()
{
    if (_pEndpoint != nullptr)
    {
        _pEndpoint->Close();
        _pEndpoint = nullptr;
    }
}


bool DiagnosticsClient::GetProcessInfo(ProcessInfoRequest& request)
{
    return (request.Process(_pEndpoint));
}


EventPipeSession* DiagnosticsClient::OpenEventPipeSession(EventKeyword keywords, EventVerbosityLevel verbosity)
{
    EventPipeStartRequest request;
    if (!request.Process(_pEndpoint, keywords, verbosity))
        return nullptr;

    auto session = new EventPipeSession(_pEndpoint, request.SessionId);
    return session;
}
