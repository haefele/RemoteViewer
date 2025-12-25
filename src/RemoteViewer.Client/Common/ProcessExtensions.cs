using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Common;

public static class ProcessExtensions
{
    public static void KillSafely(this Process process, ILogger logger)
    {
        try
        {
            if (process.HasExited)
                return;

            logger.LogInformation("Killing process {ProcessId}", process.Id);
            process.Kill();
            logger.LogInformation("Killed process {ProcessId}", process.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to kill process {ProcessId}: {ErrorMessage}", process.Id, ex.Message);
        }
    }
}
