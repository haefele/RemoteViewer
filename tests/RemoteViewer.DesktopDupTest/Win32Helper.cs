using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.RemoteDesktop;
using Windows.Win32.System.StationsAndDesktops;
using Windows.Win32.System.Threading;

namespace RemoteViewer.DesktopDupTest;

public static class Win32Helper
{
    public static void SwitchToInputDesktop()
    {
        using var inputDesktop = PInvoke.OpenInputDesktop_SafeHandle(
            0,
            false,
            DESKTOP_ACCESS_FLAGS.DESKTOP_READOBJECTS);
        if (inputDesktop.IsInvalid)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new Win32Exception(errorCode);
        }

        if (PInvoke.SetThreadDesktop(inputDesktop) == false)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new Win32Exception(errorCode);
        }
    }

    public unsafe static List<DesktopSession> GetActiveSessions()
    {
        var sessions = new List<DesktopSession>();

        // var consoleSessionId = PInvoke.WTSGetActiveConsoleSessionId();
        // sessions.Add(new DesktopSession(consoleSessionId, "Console", DesktopSessionType.Console));

        var sessionResult = PInvoke.WTSEnumerateSessions(
            HANDLE.WTS_CURRENT_SERVER_HANDLE,
            0,
            1,
            out var ppSessionInfo,
            out var sessionCount);

        if (!sessionResult || ppSessionInfo == null)
        {
            return sessions;
        }

        var currentSession = ppSessionInfo; // keep original pointer for WTSFreeMemory
        try
        {
            for (uint i = 0; i < sessionCount; i++)
            {
                if (currentSession->State == WTS_CONNECTSTATE_CLASS.WTSActive)
                {
                    sessions.Add(new DesktopSession(
                        currentSession->SessionId,
                        currentSession->pWinStationName.ToString(),
                        DesktopSessionType.Rdp));
                }

                currentSession++;
            }
        }
        finally
        {
            PInvoke.WTSFreeMemory(ppSessionInfo);
        }


        return sessions;
    }
    
    private const uint MaximumAllowedRights = 0x2000000u;

    public static unsafe Process? CreateInteractiveSystemProcess(string commandLine, uint sessionId)
    {
        try
        {
            uint winLogonPid = 0;

            var winLogonProcs = Process.GetProcessesByName("winlogon");
            foreach (var p in winLogonProcs)
            {
                if ((uint)p.SessionId == sessionId)
                {
                    winLogonPid = (uint)p.Id;
                }
            }

            // Obtain a handle to the winlogon process;
            using var winLogonProcessHandle = PInvoke.OpenProcess_SafeHandle(
              (PROCESS_ACCESS_RIGHTS)MaximumAllowedRights,
              true,
              winLogonPid);

            // Obtain a handle to the access token of the winlogon process.
            if (!PInvoke.OpenProcessToken(winLogonProcessHandle, TOKEN_ACCESS_MASK.TOKEN_DUPLICATE, out var winLogonToken))
            {
                var lastWin32 = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastWin32);
            }

            // Security attribute structure used in DuplicateTokenEx and CreateProcessAsUser.
            var securityAttributes = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(SECURITY_ATTRIBUTES)
            };

            // Copy the access token of the winlogon process; the newly created token will be a primary token.
            if (!PInvoke.DuplicateTokenEx(
                  winLogonToken,
                  (TOKEN_ACCESS_MASK)MaximumAllowedRights,
                  null,
                  SECURITY_IMPERSONATION_LEVEL.SecurityIdentification,
                  TOKEN_TYPE.TokenPrimary,
                  out var duplicatedToken))
            {
                winLogonToken.Dispose();
                
                var lastWin32 = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastWin32);
            }

            // Target the interactive windows station and desktop.
            var startupInfo = new STARTUPINFOW
            {
                cb = (uint)sizeof(STARTUPINFOW)
            };

            var desktopName = ResolveDesktopName(sessionId);
            var desktopPtr = Marshal.StringToHGlobalAuto($"winsta0\\{desktopName}\0");
            startupInfo.lpDesktop = new PWSTR((char*)desktopPtr.ToPointer());

            // Flags that specify the priority and creation method of the process.
            var dwCreationFlags = PROCESS_CREATION_FLAGS.NORMAL_PRIORITY_CLASS |
                                  PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT;
            dwCreationFlags |= PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW | PROCESS_CREATION_FLAGS.DETACHED_PROCESS;
            startupInfo.dwFlags = STARTUPINFOW_FLAGS.STARTF_USESHOWWINDOW;
            startupInfo.wShowWindow = 0;

            var cmdLineSpan = $"{commandLine}\0".ToCharArray().AsSpan();
            // Create a new process in the current user's logon session.
            var createResult = PInvoke.CreateProcessAsUser(
              duplicatedToken,
              null,
              ref cmdLineSpan,
              securityAttributes,
              securityAttributes,
              false,
              dwCreationFlags,
              null,
              null,
              in startupInfo,
              out var procInfo);

            // Invalidate the handles.
            Marshal.FreeHGlobal(desktopPtr);
            winLogonToken.Close();
            duplicatedToken.Close();

            if (!createResult)
            {
                var lastWin32 = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastWin32);
            }

            return Process.GetProcessById((int)procInfo.dwProcessId);
        }
        catch (Exception)
        {
            throw;
        }
    }
    
    
  private static string ResolveDesktopName(uint targetSessionId)
  {
    var isLogonScreenVisible = Process
      .GetProcessesByName("LogonUI")
      .Any(x => x.SessionId == targetSessionId);

    var isSecureDesktopVisible = Process
      .GetProcessesByName("consent")
      .Any(x => x.SessionId == targetSessionId);

    if (isLogonScreenVisible || isSecureDesktopVisible)
    {
      return "Winlogon";
    }

    return "Default";
  }
}

public enum DesktopSessionType
{
    Console,
    Rdp,
}
public record DesktopSession(uint SessionId, string Name, DesktopSessionType Type);