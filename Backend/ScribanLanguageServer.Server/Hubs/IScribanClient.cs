using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace ScribanLanguageServer.Server.Hubs;

/// <summary>
/// Strongly-typed interface for SignalR client methods that the hub can call
/// </summary>
public interface IScribanClient
{
    /// <summary>
    /// Instructs the client to open a picker UI for the given parameter
    /// </summary>
    Task OpenPicker(OpenPickerData data);

    /// <summary>
    /// Sends diagnostics to the client
    /// </summary>
    Task ReceiveDiagnostics(PublishDiagnosticsParams diagnostics);

    /// <summary>
    /// Sends an LSP JSON-RPC message to the client
    /// </summary>
    Task ReceiveMessage(object message);
}

/// <summary>
/// Data for the OpenPicker client call
/// </summary>
public class OpenPickerData
{
    /// <summary>
    /// Type of picker to open (e.g., "file-picker", "enum-list")
    /// </summary>
    public string PickerType { get; set; } = string.Empty;

    /// <summary>
    /// Name of the function this picker is for
    /// </summary>
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>
    /// Index of the parameter within the function
    /// </summary>
    public int ParameterIndex { get; set; }

    /// <summary>
    /// Current value (if any) for smart suggestions
    /// </summary>
    public string? CurrentValue { get; set; }

    /// <summary>
    /// Options for enum-list pickers
    /// </summary>
    public List<string>? Options { get; set; }

    /// <summary>
    /// Base path for file-picker
    /// </summary>
    public string? BasePath { get; set; }
}
