using Microsoft.Extensions.Logging;

namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Service for creating timeout-aware cancellation tokens
/// </summary>
public interface ITimeoutService
{
    CancellationTokenSource CreateTimeout(TimeSpan timeout, CancellationToken linkedToken = default);
    CancellationTokenSource CreateTimeoutForOperation(string operationType, CancellationToken linkedToken = default);
}

public class TimeoutService : ITimeoutService
{
    private readonly TimeoutConfiguration _config;
    private readonly ILogger<TimeoutService> _logger;

    public TimeoutService(TimeoutConfiguration config, ILogger<TimeoutService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CancellationTokenSource CreateTimeout(TimeSpan timeout, CancellationToken linkedToken = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    public CancellationTokenSource CreateTimeoutForOperation(string operationType, CancellationToken linkedToken = default)
    {
        var timeout = operationType switch
        {
            "parsing" => TimeSpan.FromSeconds(_config.ParsingTimeoutSeconds),
            "filesystem" => TimeSpan.FromSeconds(_config.FileSystemTimeoutSeconds),
            "validation" => TimeSpan.FromSeconds(_config.ValidationTimeoutSeconds),
            "signalr" => TimeSpan.FromSeconds(_config.SignalRMethodTimeoutSeconds),
            _ => TimeSpan.FromSeconds(_config.GlobalRequestTimeoutSeconds)
        };

        _logger.LogDebug("Creating timeout for {Operation}: {Timeout}s", operationType, timeout.TotalSeconds);
        return CreateTimeout(timeout, linkedToken);
    }
}
