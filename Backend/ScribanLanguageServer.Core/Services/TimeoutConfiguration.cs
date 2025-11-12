namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Configuration for operation timeouts
/// </summary>
public class TimeoutConfiguration
{
    public int GlobalRequestTimeoutSeconds { get; set; } = 30;
    public int ParsingTimeoutSeconds { get; set; } = 10;
    public int FileSystemTimeoutSeconds { get; set; } = 5;
    public int ValidationTimeoutSeconds { get; set; } = 5;
    public int SignalRMethodTimeoutSeconds { get; set; } = 10;
}
