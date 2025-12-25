using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.RemoteDesktop;
using Windows.Win32.System.StationsAndDesktops;
using Windows.Win32.System.Threading;

namespace RemoteViewer.Client.Services.WindowsSession;

public interface IWin32SessionService
{
    bool SwitchToInputDesktop();

    ImmutableList<DesktopSession> GetActiveSessions();

    Process? CreateInteractiveSystemProcess(string commandLine, uint sessionId);
}

public record DesktopSession(uint SessionId, string Name, DesktopSessionType Type, string Username);

public enum DesktopSessionType
{
    Console,
    Rdp,
}

public class Win32SessionService(ILogger<Win32SessionService> logger) : IWin32SessionService
{
    public bool SwitchToInputDesktop()
    {
        try
        {
            using var inputDesktop = PInvoke.OpenInputDesktop_SafeHandle(
                0,
                false,
                DESKTOP_ACCESS_FLAGS.DESKTOP_READOBJECTS);

            if (inputDesktop.IsInvalid)
            {
                logger.LogError("Failed to open input desktop: {ErrorCode}", Marshal.GetLastWin32Error());
                return false;
            }

            if (PInvoke.SetThreadDesktop(inputDesktop) == false)
            {
                logger.LogError("Failed to set thread desktop: {ErrorCode}", Marshal.GetLastWin32Error());
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Exception occurred while switching to input desktop");
            return false;
        }
    }

    public unsafe ImmutableList<DesktopSession> GetActiveSessions()
    {
        var sessions = ImmutableList.CreateBuilder<DesktopSession>();

        var consoleSessionId = PInvoke.WTSGetActiveConsoleSessionId();
        if (consoleSessionId != 0xFFFFFFFF)
        {
            sessions.Add(new DesktopSession(
                consoleSessionId,
                "Console",
                DesktopSessionType.Console,
                this.GetUsernameFromSessionId(consoleSessionId)));
        }

        var sessionResult = (bool)PInvoke.WTSEnumerateSessions(
            HANDLE.WTS_CURRENT_SERVER_HANDLE,
            0,
            1,
            out var sessionInfoPtr,
            out var sessionCount);

        if (sessionResult is false || sessionInfoPtr == null)
        {
            return sessions.ToImmutable();
        }

        var currentSession = sessionInfoPtr; // keep original pointer for WTSFreeMemory
        try
        {
            for (uint i = 0; i < sessionCount; i++)
            {
                if (currentSession->State == WTS_CONNECTSTATE_CLASS.WTSActive && currentSession->SessionId != consoleSessionId)
                {
                    sessions.Add(new DesktopSession(
                        currentSession->SessionId,
                        currentSession->pWinStationName.ToString(),
                        DesktopSessionType.Rdp,
                        this.GetUsernameFromSessionId(currentSession->SessionId)));
                }

                currentSession++;
            }
        }
        finally
        {
            PInvoke.WTSFreeMemory(sessionInfoPtr);
        }

        return sessions.ToImmutable();
    }
    private string GetUsernameFromSessionId(uint sessionId)
    {
        try
        {
            var result = PInvoke.WTSQuerySessionInformation(
                HANDLE.Null,
                sessionId,
                WTS_INFO_CLASS.WTSUserName,
                out var username,
                out var bytesReturned);

            return result && bytesReturned > 1
                ? username.ToString()
                : string.Empty;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to get username for session {SessionId}", sessionId);
            return string.Empty;
        }
    }

    public unsafe Process? CreateInteractiveSystemProcess(string commandLine, uint sessionId)
    {
        try
        {
            const uint MaximumAllowedRights = 0x2000000u;

            var winLogonPid = (uint?)Process.GetProcessesByName("winlogon")
                .FirstOrDefault(f => (uint)f.SessionId == sessionId)
                ?.Id;

            if (winLogonPid == null)
            {
                logger.LogError("No winlogon process found for session ID {SessionId}", sessionId);
                return null;
            }

            // Obtain a handle to the winlogon process
            using var winLogonProcessHandle = PInvoke.OpenProcess_SafeHandle(
                (PROCESS_ACCESS_RIGHTS)MaximumAllowedRights,
                true,
                winLogonPid.Value);

            // Obtain a handle to the access token of the winlogon process
            if (PInvoke.OpenProcessToken(winLogonProcessHandle, TOKEN_ACCESS_MASK.TOKEN_DUPLICATE, out var winLogonToken) == false)
            {
                logger.LogError("Failed to open process token for winlogon process: {ErrorCode}", Marshal.GetLastWin32Error());
                return null;
            }

            using (winLogonToken)
            {
                // Copy the access token of the winlogon process - the newly created token will be a primary token
                var duplicateTokenResult = (bool)PInvoke.DuplicateTokenEx(
                    winLogonToken,
                    (TOKEN_ACCESS_MASK)MaximumAllowedRights,
                    null,
                    SECURITY_IMPERSONATION_LEVEL.SecurityIdentification,
                    TOKEN_TYPE.TokenPrimary,
                    out var duplicatedToken);

                if (duplicateTokenResult is false)
                {
                    logger.LogError("Failed to duplicate token: {ErrorCode}", Marshal.GetLastWin32Error());
                    return null;
                }

                using (duplicatedToken)
                {
                    // Target the interactive windows station and desktop
                    var startupInfo = new STARTUPINFOW
                    {
                        cb = (uint)sizeof(STARTUPINFOW),
                        dwFlags = STARTUPINFOW_FLAGS.STARTF_USESHOWWINDOW,
                        wShowWindow = 0,
                    };

                    var desktopPtr = Marshal.StringToHGlobalAuto("winsta0\\Default");
                    try
                    {
                        startupInfo.lpDesktop = new PWSTR((char*)desktopPtr.ToPointer());

                        // Flags that specify the priority and creation method of the process
                        var creationFlags = PROCESS_CREATION_FLAGS.NORMAL_PRIORITY_CLASS |
                                            PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT |
                                            PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW |
                                            PROCESS_CREATION_FLAGS.DETACHED_PROCESS;

                        // Security attribute structure used in DuplicateTokenEx and CreateProcessAsUser
                        var securityAttributes = new SECURITY_ATTRIBUTES
                        {
                            nLength = (uint)sizeof(SECURITY_ATTRIBUTES)
                        };

                        var commandLineSpan = $"{commandLine}\0".ToCharArray().AsSpan();

                        // Create a new process in the current user's logon session.
                        var createResult = (bool)PInvoke.CreateProcessAsUser(
                            duplicatedToken,
                            null,
                            ref commandLineSpan,
                            securityAttributes,
                            securityAttributes,
                            false,
                            creationFlags,
                            null,
                            null,
                            in startupInfo,
                            out var processInfo);

                        if (createResult is false)
                        {
                            logger.LogError("Failed to create process as user: {ErrorCode}", Marshal.GetLastWin32Error());
                            return null;
                        }

                        PInvoke.CloseHandle(processInfo.hProcess);
                        PInvoke.CloseHandle(processInfo.hThread);

                        return Process.GetProcessById((int)processInfo.dwProcessId);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(desktopPtr);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Exception occurred while creating interactive system process");
            return null;
        }
    }
}
