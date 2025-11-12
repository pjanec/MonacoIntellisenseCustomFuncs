namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Service for managing document ownership and access control across SignalR connections.
/// Ensures that only the connection that opened a document can edit it.
/// </summary>
public interface IDocumentSessionService
{
    /// <summary>
    /// Registers a document as being owned by a specific connection
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID</param>
    /// <param name="documentUri">The document URI</param>
    void RegisterDocument(string connectionId, string documentUri);

    /// <summary>
    /// Unregisters a document from a connection
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID</param>
    /// <param name="documentUri">The document URI</param>
    void UnregisterDocument(string connectionId, string documentUri);

    /// <summary>
    /// Validates whether a connection has access to a document
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID</param>
    /// <param name="documentUri">The document URI</param>
    /// <returns>True if the connection owns the document or document is not registered</returns>
    bool ValidateAccess(string connectionId, string documentUri);

    /// <summary>
    /// Gets all documents owned by a specific connection
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID</param>
    /// <returns>Collection of document URIs</returns>
    IEnumerable<string> GetDocumentsForConnection(string connectionId);

    /// <summary>
    /// Cleans up all documents for a connection (called when connection disconnects)
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID</param>
    void CleanupConnection(string connectionId);

    /// <summary>
    /// Gets the total number of registered documents
    /// </summary>
    /// <returns>Document count</returns>
    int GetTotalDocuments();

    /// <summary>
    /// Gets the total number of active connections
    /// </summary>
    /// <returns>Connection count</returns>
    int GetTotalConnections();

    /// <summary>
    /// Gets statistics about current sessions
    /// </summary>
    /// <returns>Session statistics</returns>
    SessionStatistics GetStatistics();
}

/// <summary>
/// Statistics about document sessions
/// </summary>
public record SessionStatistics
{
    public int ActiveConnections { get; init; }
    public int TotalDocuments { get; init; }
}
