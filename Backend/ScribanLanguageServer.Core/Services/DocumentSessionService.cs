using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Thread-safe service for managing document ownership and access control across SignalR connections
/// </summary>
public class DocumentSessionService : IDocumentSessionService
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionToDocuments = new();
    private readonly ConcurrentDictionary<string, string> _documentToConnection = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<DocumentSessionService> _logger;
    private readonly object _lock = new();

    public DocumentSessionService(ILogger<DocumentSessionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterDocument(string connectionId, string documentUri)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentNullException(nameof(connectionId));
        if (string.IsNullOrWhiteSpace(documentUri))
            throw new ArgumentNullException(nameof(documentUri));

        lock (_lock)
        {
            // Add to connection's document list
            if (!_connectionToDocuments.TryGetValue(connectionId, out var docs))
            {
                docs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _connectionToDocuments[connectionId] = docs;
            }
            docs.Add(documentUri);

            // Map document to connection
            _documentToConnection[documentUri] = connectionId;

            _logger.LogDebug(
                "Document registered: {Uri} -> Connection {ConnectionId}",
                documentUri, connectionId);
        }
    }

    public void UnregisterDocument(string connectionId, string documentUri)
    {
        lock (_lock)
        {
            if (_connectionToDocuments.TryGetValue(connectionId, out var docs))
            {
                docs.Remove(documentUri);
            }

            _documentToConnection.TryRemove(documentUri, out _);

            _logger.LogDebug(
                "Document unregistered: {Uri} from Connection {ConnectionId}",
                documentUri, connectionId);
        }
    }

    public bool ValidateAccess(string connectionId, string documentUri)
    {
        if (_documentToConnection.TryGetValue(documentUri, out var ownerConnectionId))
        {
            var hasAccess = ownerConnectionId.Equals(connectionId, StringComparison.Ordinal);

            if (!hasAccess)
            {
                _logger.LogWarning(
                    "Access denied: Connection {ConnectionId} attempted to access {Uri} (owned by {Owner})",
                    connectionId, documentUri, ownerConnectionId);
            }

            return hasAccess;
        }

        // Document not registered - allow access (will be registered on first use)
        return true;
    }

    public IEnumerable<string> GetDocumentsForConnection(string connectionId)
    {
        if (_connectionToDocuments.TryGetValue(connectionId, out var docs))
        {
            return docs.ToList();
        }
        return Enumerable.Empty<string>();
    }

    public void CleanupConnection(string connectionId)
    {
        lock (_lock)
        {
            if (_connectionToDocuments.TryRemove(connectionId, out var docs))
            {
                foreach (var doc in docs)
                {
                    _documentToConnection.TryRemove(doc, out _);
                }

                _logger.LogInformation(
                    "Connection cleanup: {ConnectionId} had {Count} documents",
                    connectionId, docs.Count);
            }
        }
    }

    public int GetTotalDocuments() => _documentToConnection.Count;
    public int GetTotalConnections() => _connectionToDocuments.Count;

    public SessionStatistics GetStatistics()
    {
        return new SessionStatistics
        {
            ActiveConnections = GetTotalConnections(),
            TotalDocuments = GetTotalDocuments()
        };
    }
}
