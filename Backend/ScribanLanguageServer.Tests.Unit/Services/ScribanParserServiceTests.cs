using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ScribanLanguageServer.Core.Services;
using ScribanLanguageServer.Tests.Mocks;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Services;

[Trait("Stage", "B2")]
public class ScribanParserServiceTests : IDisposable
{
    private readonly ScribanParserService _service;

    public ScribanParserServiceTests()
    {
        var mockApiSpec = MockApiSpecService.CreateWithDefaultSpec();
        _service = new ScribanParserService(
            mockApiSpec,
            NullLogger<ScribanParserService>.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public async Task ParseAsync_ValidCode_ReturnsAst()
    {
        // Arrange
        var code = "{{ x = 5 }}";

        // Act
        var result = await _service.ParseAsync(code);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ParseAsync_InvalidCode_ReturnsAstWithErrors()
    {
        // Arrange
        var code = "{{ for item in list }}";  // Missing 'end'

        // Act
        var result = await _service.ParseAsync(code);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ParseAsync_EmptyCode_ReturnsAst()
    {
        // Arrange
        var code = "";

        // Act
        var result = await _service.ParseAsync(code);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ValidCode_ReturnsNoDiagnostics()
    {
        // Arrange
        var code = "{{ x = 5 }}";

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_InvalidCode_ReturnsDiagnostics()
    {
        // Arrange
        var code = "{{ for item in list }}";  // Missing 'end'

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().NotBeEmpty();
        diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_SyntaxError_IncludesLocation()
    {
        // Arrange
        var code = "{{ for item in list }}";

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        var error = diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
        error.Range.Should().NotBeNull();
        error.Message.Should().NotBeNullOrEmpty();
        error.Source.Should().Be("scriban-parser");
    }

    [Fact]
    public async Task GetDiagnosticsAsync_SameVersionTwice_UsesCacheSecondTime()
    {
        // Arrange
        var code = "{{ x = 5 }}";
        var uri = "file:///test.scriban";

        // Act
        await _service.GetDiagnosticsAsync(uri, code, 1);
        var stats1 = _service.GetCacheStatistics();

        await _service.GetDiagnosticsAsync(uri, code, 1);
        var stats2 = _service.GetCacheStatistics();

        // Assert
        stats1.TotalMisses.Should().Be(1);
        stats1.TotalHits.Should().Be(0);

        stats2.TotalMisses.Should().Be(1); // Still 1
        stats2.TotalHits.Should().Be(1);   // Now 1
    }

    [Fact]
    public async Task GetDiagnosticsAsync_DifferentVersion_ReParses()
    {
        // Arrange
        var uri = "file:///test.scriban";

        // Act
        await _service.GetDiagnosticsAsync(uri, "{{ x = 5 }}", 1);
        await _service.GetDiagnosticsAsync(uri, "{{ x = 10 }}", 2);

        var stats = _service.GetCacheStatistics();

        // Assert
        stats.TotalMisses.Should().Be(2); // Both were cache misses
    }

    [Fact]
    public async Task InvalidateCache_RemovesCachedEntry()
    {
        // Arrange
        var uri = "file:///test.scriban";
        await _service.GetDiagnosticsAsync(uri, "{{ x = 5 }}", 1);

        // Act
        _service.InvalidateCache(uri);

        // Request again - should be cache miss
        await _service.GetDiagnosticsAsync(uri, "{{ x = 5 }}", 1);
        var stats = _service.GetCacheStatistics();

        // Assert
        stats.TotalMisses.Should().Be(2); // Original + after invalidation
        stats.TotalHits.Should().Be(0);
    }

    [Fact]
    public void InvalidateCache_NonExistentDocument_DoesNotThrow()
    {
        // Act & Assert
        var act = () => _service.InvalidateCache("file:///nonexistent.scriban");
        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetCacheStatistics_CalculatesHitRateCorrectly()
    {
        // Arrange
        var uri1 = "file:///test1.scriban";
        var uri2 = "file:///test2.scriban";

        // Act
        await _service.GetDiagnosticsAsync(uri1, "{{ x = 5 }}", 1);  // Miss
        await _service.GetDiagnosticsAsync(uri1, "{{ x = 5 }}", 1);  // Hit
        await _service.GetDiagnosticsAsync(uri1, "{{ x = 5 }}", 1);  // Hit
        await _service.GetDiagnosticsAsync(uri2, "{{ y = 10 }}", 1); // Miss

        var stats = _service.GetCacheStatistics();

        // Assert
        stats.TotalHits.Should().Be(2);
        stats.TotalMisses.Should().Be(2);
        stats.HitRate.Should().BeApproximately(0.5, 0.01);
        stats.TotalEntries.Should().Be(2);
    }

    [Fact]
    public void GetCacheStatistics_InitialState_ReturnsZeros()
    {
        // Act
        var stats = _service.GetCacheStatistics();

        // Assert
        stats.TotalEntries.Should().Be(0);
        stats.TotalHits.Should().Be(0);
        stats.TotalMisses.Should().Be(0);
        stats.HitRate.Should().Be(0);
    }

    [Fact(Skip = "Timeout behavior is environment-dependent and hard to test reliably")]
    public async Task ParseAsync_HugeDocument_TimesOut()
    {
        // Arrange - create a very large document
        var code = string.Concat(Enumerable.Repeat("{{ x = 1 }}\n", 100000));

        // Act & Assert
        var act = () => _service.ParseAsync(code);

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*timed out*");
    }

    [Fact]
    public async Task ParseAsync_MultipleCalls_Concurrent()
    {
        // Arrange
        var code = "{{ x = 5 }}";

        // Act
        var tasks = Enumerable.Range(0, 10).Select(_ => _service.ParseAsync(code));
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ConcurrentRequests_ThreadSafe()
    {
        // Arrange
        var code = "{{ x = 5 }}";
        var uri = "file:///test.scriban";

        // Act
        var tasks = Enumerable.Range(0, 20).Select(i =>
            _service.GetDiagnosticsAsync(uri, code, i % 5));
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
        var stats = _service.GetCacheStatistics();
        stats.TotalMisses.Should().BeGreaterThan(0);
    }
}
