namespace DatabaseMcpServer.Configuration;

internal sealed class ConfigurationChangeSignal
{
    public event Action? Changed;
    // Subscribers enqueue work; they must not perform I/O while configuration is published.
    public void Publish() => Changed?.Invoke();
}
