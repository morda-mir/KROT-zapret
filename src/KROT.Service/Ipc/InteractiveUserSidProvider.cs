using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace KROT.Service.Ipc;

internal static class InteractiveUserSidProvider
{
    public static SecurityIdentifier? TryGetActiveUserSid()
    {
        if (Environment.UserInteractive)
        {
            return WindowsIdentity.GetCurrent().User;
        }

        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue || !WTSQueryUserToken(sessionId, out var token))
        {
            return null;
        }

        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            return identity.User;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);
}

