using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Helpers;

internal static class CommandTimeoutPolicy
{
    public const int MaxTimeoutSeconds = 86_400;
    public static void Validate(int commandTimeoutSeconds)
    {
        if (commandTimeoutSeconds is < 0 or > MaxTimeoutSeconds)
        {
            throw new DatabaseMcpException(
                DatabaseErrorCode.InvalidParameters,
                $"commandTimeoutSeconds 必须在 0-{MaxTimeoutSeconds} 之间（0 表示无限等待）");
        }
    }

}
