using System.Text.RegularExpressions;

namespace ScribanLanguageServer.Core.Validation;

/// <summary>
/// Validates user input to prevent injection attacks and resource exhaustion
/// </summary>
public static class InputValidator
{
    private const int MaxDocumentUriLength = 2048;
    private const int MaxLineLength = 10000;
    private const int MaxDocumentSize = 1024 * 1024; // 1MB
    private const int MaxLineNumber = 100000;

    public static void ValidateDocumentUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("Document URI cannot be empty", nameof(uri));
        }

        if (uri.Length > MaxDocumentUriLength)
        {
            throw new ArgumentException(
                $"Document URI too long (max {MaxDocumentUriLength} chars)",
                nameof(uri));
        }

        // Must be a valid URI
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
        {
            throw new ArgumentException("Invalid document URI format", nameof(uri));
        }

        // Only allow specific schemes
        var allowedSchemes = new[] { "file", "untitled", "inmemory" };
        if (!allowedSchemes.Contains(parsedUri.Scheme.ToLowerInvariant()))
        {
            throw new ArgumentException(
                $"URI scheme '{parsedUri.Scheme}' not allowed. Allowed schemes: {string.Join(", ", allowedSchemes)}",
                nameof(uri));
        }
    }

    public static void ValidatePosition(int line, int character)
    {
        if (line < 0 || line > MaxLineNumber)
        {
            throw new ArgumentException(
                $"Invalid line number: {line}. Must be between 0 and {MaxLineNumber}",
                nameof(line));
        }

        if (character < 0 || character > MaxLineLength)
        {
            throw new ArgumentException(
                $"Invalid character position: {character}. Must be between 0 and {MaxLineLength}",
                nameof(character));
        }
    }

    public static void ValidateDocumentSize(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return; // Empty documents are valid
        }

        if (content.Length > MaxDocumentSize)
        {
            throw new ArgumentException(
                $"Document too large: {content.Length} bytes. Maximum allowed: {MaxDocumentSize} bytes",
                nameof(content));
        }
    }

    public static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        // Remove dangerous path traversal patterns
        var sanitized = path
            .Replace("..", string.Empty)
            .Replace("~", string.Empty);

        // Normalize path separators
        sanitized = sanitized.Replace('\\', '/');

        // Remove leading/trailing slashes
        sanitized = sanitized.Trim('/');

        return sanitized;
    }

    public static void ValidateFunctionName(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new ArgumentException("Function name cannot be empty", nameof(functionName));
        }

        if (functionName.Length > 100)
        {
            throw new ArgumentException("Function name too long", nameof(functionName));
        }

        // Only allow alphanumeric and underscore
        if (!Regex.IsMatch(functionName, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
        {
            throw new ArgumentException(
                "Function name must start with letter or underscore and contain only alphanumeric characters and underscores",
                nameof(functionName));
        }
    }

    public static void ValidateParameterIndex(int parameterIndex)
    {
        if (parameterIndex < 0 || parameterIndex > 20)
        {
            throw new ArgumentException(
                $"Invalid parameter index: {parameterIndex}. Must be between 0 and 20",
                nameof(parameterIndex));
        }
    }
}
