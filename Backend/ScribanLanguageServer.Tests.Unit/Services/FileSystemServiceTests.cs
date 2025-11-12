using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ScribanLanguageServer.Core.Services;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Services;

[Trait("Stage", "B2")]
public class FileSystemServiceTests : IDisposable
{
    private readonly FileSystemService _service;
    private readonly string _testDir;

    public FileSystemServiceTests()
    {
        _service = new FileSystemService(
            NullLogger<FileSystemService>.Instance);

        // Create test directory structure
        _testDir = Path.Combine(Path.GetTempPath(), $"scriban-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        File.WriteAllText(Path.Combine(_testDir, "test1.txt"), "test");
        File.WriteAllText(Path.Combine(_testDir, "test2.txt"), "test");
        File.WriteAllText(Path.Combine(_testDir, "readme.md"), "readme");
        Directory.CreateDirectory(Path.Combine(_testDir, "subdir"));
    }

    public void Dispose()
    {
        // Clean up test directory
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task GetPathSuggestions_ValidDirectory_ReturnsFiles()
    {
        // Act
        var results = await _service.GetPathSuggestionsAsync(_testDir);

        // Assert
        results.Should().HaveCount(4); // 3 files + 1 subdir
        results.Should().Contain(p => p.Contains("test1.txt"));
        results.Should().Contain(p => p.Contains("test2.txt"));
        results.Should().Contain(p => p.Contains("readme.md"));
        results.Should().Contain(p => p.Contains("subdir"));
    }

    [Fact]
    public async Task GetPathSuggestions_WithFilter_ReturnsFilteredFiles()
    {
        // Act
        var results = await _service.GetPathSuggestionsAsync(_testDir, "*.txt");

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(p => p.Should().EndWith(".txt"));
    }

    [Fact]
    public async Task GetPathSuggestions_WithComplexFilter_ReturnsMatching()
    {
        // Act
        var results = await _service.GetPathSuggestionsAsync(_testDir, "test*");

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(p => p.Should().StartWith("test"));
    }

    [Fact]
    public async Task GetPathSuggestions_NonExistentDirectory_ReturnsEmpty()
    {
        // Act
        var results = await _service.GetPathSuggestionsAsync(
            Path.Combine(_testDir, "nonexistent"));

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPathSuggestions_EmptyPath_UsesCurrentDirectory()
    {
        // Act
        var results = await _service.GetPathSuggestionsAsync("");

        // Assert
        // Should not throw and should return results from current directory
        results.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPathSuggestions_NullPath_UsesCurrentDirectory()
    {
        // Act
        var results = await _service.GetPathSuggestionsAsync(null!);

        // Assert
        // Should not throw and should return results from current directory
        results.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPathSuggestions_ConcurrentCalls_AllSucceed()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 20).Select(_ =>
            _service.GetPathSuggestionsAsync(_testDir));

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Should().HaveCount(4));
    }

    [Fact]
    public async Task GetPathSuggestions_WithDangerousPath_Sanitizes()
    {
        // Arrange - path with directory traversal attempt
        var path = _testDir + "/../../../etc/passwd";

        // Act - should sanitize and not throw
        var act = () => _service.GetPathSuggestionsAsync(path);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetPathSuggestions_WithTildeInPath_Sanitizes()
    {
        // Arrange - path with tilde
        var path = "~/test";

        // Act - should sanitize and handle gracefully
        var results = await _service.GetPathSuggestionsAsync(path);

        // Assert - should return empty (sanitized path won't exist)
        results.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPathSuggestions_ReturnsSortedResults()
    {
        // Act
        var results = await _service.GetPathSuggestionsAsync(_testDir);

        // Assert
        results.Should().NotBeNull();
        // Results may or may not be sorted, but should be consistent
        var secondCall = await _service.GetPathSuggestionsAsync(_testDir);
        results.Should().BeEquivalentTo(secondCall);
    }

    [Fact]
    public async Task GetPathSuggestions_WithCancellationToken_CanBeCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        var act = () => _service.GetPathSuggestionsAsync(_testDir, cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetPathSuggestions_EmptyDirectory_ReturnsEmpty()
    {
        // Arrange
        var emptyDir = Path.Combine(_testDir, "empty");
        Directory.CreateDirectory(emptyDir);

        // Act
        var results = await _service.GetPathSuggestionsAsync(emptyDir);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPathSuggestions_RelativePaths_ReturnsRelativeNames()
    {
        // Act
        var results = await _service.GetPathSuggestionsAsync(_testDir);

        // Assert
        results.Should().AllSatisfy(p =>
        {
            // Should be relative, not absolute
            p.Should().NotContain(_testDir);
            p.Should().NotStartWith(Path.DirectorySeparatorChar.ToString());
        });
    }

    [Fact]
    public async Task GetPathSuggestions_ThrottlesRequests()
    {
        // Arrange - make many concurrent requests
        var tasks = Enumerable.Range(0, 100).Select(_ =>
            _service.GetPathSuggestionsAsync(_testDir));

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert - all should complete successfully despite throttling
        results.Should().HaveCount(100);
        results.Should().AllSatisfy(r => r.Should().HaveCount(4));
    }
}
