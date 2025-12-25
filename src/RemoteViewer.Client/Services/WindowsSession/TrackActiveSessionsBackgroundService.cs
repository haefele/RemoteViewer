using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Common;

namespace RemoteViewer.Client.Services.WindowsSession;

public class TrackActiveSessionsBackgroundService(
    ILogger<TrackActiveSessionsBackgroundService> logger,
    IWin32SessionService win32SessionService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var sessionProcesses = new Dictionary<uint, Process>();

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var sessions = win32SessionService.GetActiveSessions();
                    logger.LogInformation("Found {SessionCount} active sessions", sessions.Count);

                    // Remove processes for sessions that are no longer active
                    foreach (var process in sessionProcesses.Where(f => sessions.Any(d => d.SessionId == f.Key) is false).ToList())
                    {
                        process.Value.KillSafely(logger);
                        sessionProcesses.Remove(process.Key);
                    }

                    // Start processes for new active sessions
                    foreach (var session in sessions)
                    {
                        var process = sessionProcesses.GetValueOrDefault(session.SessionId);
                        if (process is { HasExited: false })
                            continue;

                        logger.LogInformation("Starting process for session {SessionId}", session.SessionId);

                        process = win32SessionService.CreateInteractiveSystemProcess($"\"{Environment.ProcessPath}\" --session-recorder", session.SessionId);
                        if (process is null)
                        {
                            logger.LogError("Failed to start process for session {SessionId}", session.SessionId);
                            continue;
                        }

                        sessionProcesses[session.SessionId] = process;
                        logger.LogInformation("Started process {ProcessId} for session {SessionId}", process.Id, session.SessionId);
                    }
                }
                catch (Win32Exception ex)
                {
                    logger.LogError(ex, "Failed to start child-process for sessions: {ErrorMessage}", ex.Message);
                    continue;
                }
            }
        }
        finally
        {
            // Cleanup all started processes
            foreach (var process in sessionProcesses.Values)
            {
                process.KillSafely(logger);
            }
        }
    }
}
