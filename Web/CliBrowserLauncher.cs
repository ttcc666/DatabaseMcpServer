using System.Diagnostics;

namespace DatabaseMcpServer.Web;

internal interface ICliBrowserLauncher
{
    bool TryOpen(Uri uri, out string? errorMessage);
}

internal sealed class CliBrowserLauncher : ICliBrowserLauncher
{
    public bool TryOpen(Uri uri, out string? errorMessage)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            };

            _ = Process.Start(startInfo);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
