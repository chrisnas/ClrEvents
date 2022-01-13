#pragma once
#include "IpcEndpoint.h"

class PidEndpoint : public IpcEndpoint
{
public:
    static PidEndpoint* Create(int pid);

    virtual bool Close() override;

private:
    PidEndpoint();
    static PidEndpoint* CreateForWindows(int pid);
    //TODO: static PidEndpoint* CreateForLinux();

    void CloseForWindows();
};

