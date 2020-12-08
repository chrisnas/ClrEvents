using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CpuSamplingProfiler
{
    public static class ProfilingPermission
    {
        private const uint TRACELOG_GUID_ENABLE = 0x0080;
        private const int NO_ERROR = 0;  // ERROR_SUCCESS in C++
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        // read https://docs.microsoft.com/en-us/windows/win32/etw/configuring-and-starting-a-systemtraceprovider-session 
        // for more details 
        public static void EnableProfilerUser(string accountName)
        {
            // Kernel provider from https://github.com/microsoft/perfview/blob/master/src/TraceEvent/Parsers/KernelTraceEventParser.cs#L43
            Guid kernelProviderGuid = new Guid("{9e814aad-3204-11d2-9a82-006008a86939}");
            byte[] sid = LookupSidByName(accountName);

            // from https://docs.microsoft.com/en-us/windows/win32/etw/configuring-and-starting-a-systemtraceprovider-session
            uint operation = (uint)EventSecurityOperation.EventSecurityAddDACL;
            uint rights = TRACELOG_GUID_ENABLE;
            bool allowOrDeny = ("Allow" != null);
            uint result = EventAccessControl(
                ref kernelProviderGuid,
                operation,
                sid,
                rights,
                allowOrDeny
            );

            if (result != NO_ERROR)
            {
                var lastErrorMessage = new Win32Exception((int)result).Message;
                throw new InvalidOperationException($"Failed to add ACL ({result.ToString()}) : {lastErrorMessage}");
            }
        }

        private static byte[] LookupSidByName(string accountName)
        {
            byte[] sid = null;
            uint cbSid = 0;
            StringBuilder referencedDomainName = new StringBuilder();
            uint cchReferencedDomainName = (uint)referencedDomainName.Capacity;
            SID_NAME_USE sidUse;

            int err = NO_ERROR;
            if (!LookupAccountName(null, accountName, sid, ref cbSid, referencedDomainName, ref cchReferencedDomainName, out sidUse))
            {
                err = Marshal.GetLastWin32Error();
                if (err == ERROR_INSUFFICIENT_BUFFER)
                {
                    sid = new byte[cbSid];
                    referencedDomainName.EnsureCapacity((int)cchReferencedDomainName);
                    err = NO_ERROR;
                    if (!LookupAccountName(null, accountName, sid, ref cbSid, referencedDomainName, ref cchReferencedDomainName, out sidUse))
                        err = Marshal.GetLastWin32Error();
                }
            }

            if (err != NO_ERROR)
            {
                var lastErrorMessage = new Win32Exception(err).Message;
                throw new InvalidOperationException($"LookupAccountName fails ({err.ToString()}) : {lastErrorMessage}");
            }

            // display the SID associated to the given user
            IntPtr ptrSid;
            if (!ConvertSidToStringSid(sid, out ptrSid))
            {
                err = Marshal.GetLastWin32Error();
                var lastErrorMessage = new Win32Exception(err).Message;
                Console.WriteLine($"No SID string associated to user {accountName} ({err.ToString()}) : {lastErrorMessage}");
            }
            else
            {
                string sidString = Marshal.PtrToStringAuto(ptrSid);
                ProfilingPermission.LocalFree(ptrSid);
                Console.WriteLine($"Account ({referencedDomainName}){accountName} mapped to {sidString}");
            }

            return sid;
        }

        [DllImport("Sechost.dll", SetLastError = true)]
        static extern uint EventAccessControl(
            ref Guid providerGuid,
            uint operation,
            [MarshalAs(UnmanagedType.LPArray)] byte[] Sid,
            uint right,
            bool allowOrDeny // true means ALLOW
            );

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr hMem);


        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool LookupAccountName(
            string systemName,
            string accountName,
            [MarshalAs(UnmanagedType.LPArray)] byte[] Sid,
            ref uint cbSid,
            StringBuilder referencedDomainName,
            ref uint cchReferencedDomainName,
            out SID_NAME_USE nameUse);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool ConvertSidToStringSid(
            [MarshalAs(UnmanagedType.LPArray)] byte[] pSID,
            out IntPtr ptrSid); // can't be an out string because we need to explicitly call LocalFree on it;
                                // the marshaller would call CoTaskMemFree in case of a string

        // from http://pinvoke.net/default.aspx/advapi32/LookupAccountName.html
        enum SID_NAME_USE
        {
            SidTypeUser = 1,
            SidTypeGroup,
            SidTypeDomain,
            SidTypeAlias,
            SidTypeWellKnownGroup,
            SidTypeDeletedAccount,
            SidTypeInvalid,
            SidTypeUnknown,
            SidTypeComputer
        }

        // from evntcons.h
        enum EventSecurityOperation
        {
            EventSecuritySetDACL = 0,
            EventSecuritySetSACL,
            EventSecurityAddDACL,
            EventSecurityAddSACL,
            EventSecurityMax
        } // EVENTSECURITYOPERATION
    }
}
