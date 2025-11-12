using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace ScribanLanguageServer.Server.Hubs;

/// <summary>
/// Context information when checking for trigger characters
/// </summary>
public class TriggerContext
{
    /// <summary>
    /// Document URI
    /// </summary>
    public string DocumentUri { get; set; } = string.Empty;

    /// <summary>
    /// Full document text content
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Current cursor position
    /// </summary>
    public Position Position { get; set; } = new();

    /// <summary>
    /// Current line text for quick context
    /// </summary>
    public string? CurrentLine { get; set; }

    /// <summary>
    /// Trigger character that was typed (e.g., "(", "\"")
    /// </summary>
    public string? TriggerCharacter { get; set; }
}

/// <summary>
/// Result of parameter context analysis
/// </summary>
public class ParameterContext
{
    /// <summary>
    /// Name of the function
    /// </summary>
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>
    /// Parameter index (0-based)
    /// </summary>
    public int ParameterIndex { get; set; }

    /// <summary>
    /// Current value in the parameter position (if any)
    /// </summary>
    public string? CurrentValue { get; set; }

    /// <summary>
    /// Parameter specification from ApiSpec
    /// </summary>
    public Core.ApiSpec.ParameterEntry? ParameterSpec { get; set; }

    /// <summary>
    /// Whether the parameter context was found
    /// </summary>
    public bool IsValid { get; set; }
}
