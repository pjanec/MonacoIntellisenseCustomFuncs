using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ScribanLanguageServer.Core.Services;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Services;

[Trait("Stage", "B2")]
public class DocumentSessionServiceTests
{
    private readonly DocumentSessionService _service;

    public DocumentSessionServiceTests()
    {
        _service = new DocumentSessionService(
            NullLogger<DocumentSessionService>.Instance);
    }

    [Fact]
    public void RegisterDocument_NewDocument_Succeeds()
    {
        // Act
        _service.RegisterDocument("conn1", "file:///test.scriban");

        // Assert
        _service.GetTotalDocuments().Should().Be(1);
        _service.GetTotalConnections().Should().Be(1);
    }

    [Fact]
    public void RegisterDocument_SameConnectionMultipleDocs_Succeeds()
    {
        // Act
        _service.RegisterDocument("conn1", "file:///test1.scriban");
        _service.RegisterDocument("conn1", "file:///test2.scriban");

        // Assert
        var docs = _service.GetDocumentsForConnection("conn1");
        docs.Should().HaveCount(2);
        docs.Should().Contain("file:///test1.scriban");
        docs.Should().Contain("file:///test2.scriban");
    }

    [Fact]
    public void RegisterDocument_MultipleConnections_Succeeds()
    {
        // Act
        _service.RegisterDocument("conn1", "file:///test1.scriban");
        _service.RegisterDocument("conn2", "file:///test2.scriban");

        // Assert
        _service.GetTotalDocuments().Should().Be(2);
        _service.GetTotalConnections().Should().Be(2);
    }

    [Fact]
    public void RegisterDocument_NullConnectionId_ThrowsException()
    {
        // Act & Assert
        var act = () => _service.RegisterDocument(null!, "file:///test.scriban");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterDocument_NullDocumentUri_ThrowsException()
    {
        // Act & Assert
        var act = () => _service.RegisterDocument("conn1", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ValidateAccess_OwnerConnection_ReturnsTrue()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test.scriban");

        // Act
        var hasAccess = _service.ValidateAccess("conn1", "file:///test.scriban");

        // Assert
        hasAccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateAccess_DifferentConnection_ReturnsFalse()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test.scriban");

        // Act
        var hasAccess = _service.ValidateAccess("conn2", "file:///test.scriban");

        // Assert
        hasAccess.Should().BeFalse();
    }

    [Fact]
    public void ValidateAccess_UnregisteredDocument_ReturnsTrue()
    {
        // Act - unregistered documents allow access
        var hasAccess = _service.ValidateAccess("conn1", "file:///new.scriban");

        // Assert
        hasAccess.Should().BeTrue();
    }

    [Fact]
    public void UnregisterDocument_RemovesDocument()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test.scriban");

        // Act
        _service.UnregisterDocument("conn1", "file:///test.scriban");

        // Assert
        _service.GetTotalDocuments().Should().Be(0);
    }

    [Fact]
    public void UnregisterDocument_LeavesOtherDocuments()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test1.scriban");
        _service.RegisterDocument("conn1", "file:///test2.scriban");

        // Act
        _service.UnregisterDocument("conn1", "file:///test1.scriban");

        // Assert
        _service.GetTotalDocuments().Should().Be(1);
        var docs = _service.GetDocumentsForConnection("conn1");
        docs.Should().Contain("file:///test2.scriban");
    }

    [Fact]
    public void CleanupConnection_RemovesAllDocuments()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test1.scriban");
        _service.RegisterDocument("conn1", "file:///test2.scriban");

        // Act
        _service.CleanupConnection("conn1");

        // Assert
        _service.GetTotalDocuments().Should().Be(0);
        _service.GetTotalConnections().Should().Be(0);
    }

    [Fact]
    public void CleanupConnection_DoesNotAffectOtherConnections()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test1.scriban");
        _service.RegisterDocument("conn2", "file:///test2.scriban");

        // Act
        _service.CleanupConnection("conn1");

        // Assert
        _service.GetTotalDocuments().Should().Be(1);
        _service.GetTotalConnections().Should().Be(1);
        _service.GetDocumentsForConnection("conn2").Should().Contain("file:///test2.scriban");
    }

    [Fact]
    public void GetDocumentsForConnection_NonExistentConnection_ReturnsEmpty()
    {
        // Act
        var docs = _service.GetDocumentsForConnection("non-existent");

        // Assert
        docs.Should().BeEmpty();
    }

    [Fact]
    public void RegisterDocument_CaseInsensitiveDocumentUris()
    {
        // Act
        _service.RegisterDocument("conn1", "file:///Test.scriban");
        _service.RegisterDocument("conn1", "file:///test.scriban");

        // Assert - should treat as same document (case insensitive)
        _service.GetTotalDocuments().Should().Be(1);
        var docs = _service.GetDocumentsForConnection("conn1");
        docs.Should().HaveCount(1);
    }

    [Fact]
    public async Task RegisterDocument_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            _service.RegisterDocument($"conn{i % 10}", $"file:///test{i}.scriban");
        })).ToArray();

        // Act
        await Task.WhenAll(tasks);

        // Assert
        _service.GetTotalDocuments().Should().Be(100);
        _service.GetTotalConnections().Should().Be(10);
    }

    [Fact]
    public void RegisterDocument_ReassignDocument_UpdatesOwnership()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test.scriban");

        // Act - another connection registers same document
        _service.RegisterDocument("conn2", "file:///test.scriban");

        // Assert - ownership transferred
        _service.ValidateAccess("conn1", "file:///test.scriban").Should().BeFalse();
        _service.ValidateAccess("conn2", "file:///test.scriban").Should().BeTrue();
    }

    [Fact]
    public void GetTotalDocuments_ReflectsCurrentState()
    {
        // Act & Assert
        _service.GetTotalDocuments().Should().Be(0);

        _service.RegisterDocument("conn1", "file:///test1.scriban");
        _service.GetTotalDocuments().Should().Be(1);

        _service.RegisterDocument("conn1", "file:///test2.scriban");
        _service.GetTotalDocuments().Should().Be(2);

        _service.UnregisterDocument("conn1", "file:///test1.scriban");
        _service.GetTotalDocuments().Should().Be(1);
    }

    [Fact]
    public void GetTotalConnections_ReflectsCurrentState()
    {
        // Act & Assert
        _service.GetTotalConnections().Should().Be(0);

        _service.RegisterDocument("conn1", "file:///test1.scriban");
        _service.GetTotalConnections().Should().Be(1);

        _service.RegisterDocument("conn2", "file:///test2.scriban");
        _service.GetTotalConnections().Should().Be(2);

        _service.CleanupConnection("conn1");
        _service.GetTotalConnections().Should().Be(1);
    }
}
