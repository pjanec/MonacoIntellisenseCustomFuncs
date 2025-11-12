using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Scriban.Syntax;
using ScribanLanguageServer.Core.ApiSpec;

namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Partial class containing semantic analysis and AST traversal functionality
/// </summary>
public partial class ScribanParserService
{
    /// <summary>
    /// Performs semantic validation using the API spec
    /// </summary>
    private async Task<List<Diagnostic>> GetSemanticErrorsAsync(
        ScriptPage ast,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var diagnostics = new List<Diagnostic>();
            var visitor = new SemanticValidationVisitor(_apiSpecService, diagnostics);
            visitor.Visit(ast);
            return diagnostics;
        }, cancellationToken);
    }

    /// <summary>
    /// Finds the most specific AST node at a given position
    /// </summary>
    public ScriptNode? GetNodeAtPosition(ScriptPage ast, Position position)
    {
        if (ast == null) return null;

        var visitor = new NodeFinderVisitor(position);

        // Visit the page itself first
        visitor.CheckNode(ast);

        // Then visit all statements in the page
        if (ast.Body != null)
        {
            foreach (var statement in ast.Body.Statements)
            {
                visitor.Visit(statement);
            }
        }

        return visitor.FoundNode;
    }

    /// <summary>
    /// Visitor that finds the smallest AST node containing a given position
    /// </summary>
    private class NodeFinderVisitor : ScriptVisitor
    {
        private readonly Position _position;
        public ScriptNode? FoundNode { get; private set; }

        public NodeFinderVisitor(Position position)
        {
            _position = position;
        }

        public void CheckNode(ScriptNode? node)
        {
            if (node == null) return;

            if (IsPositionInNode(node))
            {
                FoundNode = node;
            }
        }

        public override void Visit(ScriptNode? node)
        {
            if (node == null) return;

            if (IsPositionInNode(node))
            {
                // This node contains the position - update FoundNode and continue traversing
                FoundNode = node;

                // Continue traversing children to find the smallest/most specific node
                base.Visit(node);
            }
        }

        private bool IsPositionInNode(ScriptNode node)
        {
            // Check if node span contains position
            var span = node.Span;

            // Convert Scriban positions (1-based) to LSP positions (0-based)
            var startLine = span.Start.Line - 1;
            var startColumn = span.Start.Column - 1;
            var endLine = span.End.Line - 1;
            var endColumn = span.End.Column - 1;

            // Check if position is within this node's range
            if (_position.Line < startLine || _position.Line > endLine)
            {
                // Position is outside line range
                return false;
            }

            if (_position.Line == startLine && _position.Line == endLine)
            {
                // Position and node are on the same line
                return _position.Character >= startColumn && _position.Character <= endColumn;
            }
            else if (_position.Line == startLine)
            {
                // Position is on the start line
                return _position.Character >= startColumn;
            }
            else if (_position.Line == endLine)
            {
                // Position is on the end line
                return _position.Character <= endColumn;
            }
            else
            {
                // Position is between start and end lines
                return true;
            }
        }
    }

    /// <summary>
    /// Visitor that validates function calls against the API spec
    /// </summary>
    private class SemanticValidationVisitor : ScriptVisitor
    {
        private readonly IApiSpecService _apiSpec;
        private readonly List<Diagnostic> _diagnostics;

        public SemanticValidationVisitor(
            IApiSpecService apiSpec,
            List<Diagnostic> diagnostics)
        {
            _apiSpec = apiSpec;
            _diagnostics = diagnostics;
        }

        public override void Visit(ScriptFunctionCall functionCall)
        {
            if (functionCall == null)
            {
                base.Visit(functionCall);
                return;
            }

            var functionName = GetFunctionName(functionCall);
            if (string.IsNullOrEmpty(functionName))
            {
                base.Visit(functionCall);
                return;
            }

            // Check if function exists
            GlobalEntry? functionSpec = null;

            // Try as global function
            var globalEntry = _apiSpec.GetGlobal(functionName);
            if (globalEntry?.Type == "function")
            {
                functionSpec = globalEntry;
            }

            // Try as member function (e.g., os.execute)
            if (functionSpec == null && functionCall.Target is ScriptMemberExpression member)
            {
                var objectName = GetObjectName(member);
                if (!string.IsNullOrEmpty(objectName))
                {
                    var memberFunc = _apiSpec.GetObjectMember(objectName, member.Member.Name);
                    if (memberFunc != null)
                    {
                        // Convert FunctionEntry to GlobalEntry for consistent handling
                        functionSpec = new GlobalEntry
                        {
                            Name = $"{objectName}.{member.Member.Name}",
                            Type = "function",
                            Hover = memberFunc.Hover,
                            Parameters = memberFunc.Parameters
                        };
                    }
                }
            }

            if (functionSpec == null)
            {
                // Unknown function
                _diagnostics.Add(new Diagnostic
                {
                    Range = ToLspRange(functionCall.Span),
                    Severity = DiagnosticSeverity.Error,
                    Message = $"Unknown function '{functionName}'",
                    Source = "scriban-semantic"
                });
            }
            else
            {
                // Validate argument count
                var expectedCount = functionSpec.Parameters?.Count ?? 0;
                var actualCount = functionCall.Arguments.Count;

                if (actualCount != expectedCount)
                {
                    _diagnostics.Add(new Diagnostic
                    {
                        Range = ToLspRange(functionCall.Span),
                        Severity = DiagnosticSeverity.Error,
                        Message = $"Function '{functionName}' expects {expectedCount} arguments, but {actualCount} provided",
                        Source = "scriban-semantic"
                    });
                }

                // Validate enum values
                if (functionSpec.Parameters != null)
                {
                    for (int i = 0; i < Math.Min(actualCount, expectedCount); i++)
                    {
                        var param = functionSpec.Parameters[i];
                        if (param.Picker == "enum-list" && param.Options != null)
                        {
                            var argValue = GetConstantValue(functionCall.Arguments[i]);
                            if (argValue != null &&
                                !param.Options.Contains(argValue, StringComparer.OrdinalIgnoreCase))
                            {
                                _diagnostics.Add(new Diagnostic
                                {
                                    Range = ToLspRange(functionCall.Arguments[i].Span),
                                    Severity = DiagnosticSeverity.Error,
                                    Message = $"Invalid value '{argValue}'. Expected one of: {string.Join(", ", param.Options)}",
                                    Source = "scriban-semantic"
                                });
                            }
                        }
                    }
                }
            }

            base.Visit(functionCall);
        }

        private string? GetFunctionName(ScriptFunctionCall functionCall)
        {
            return functionCall.Target switch
            {
                ScriptVariable variable => variable.Name,
                ScriptMemberExpression member => member.Member.Name,
                _ => null
            };
        }

        private string? GetObjectName(ScriptMemberExpression member)
        {
            return member.Target switch
            {
                ScriptVariable variable => variable.Name,
                _ => null
            };
        }

        private string? GetConstantValue(ScriptExpression expression)
        {
            return expression switch
            {
                ScriptLiteral literal => literal.Value?.ToString(),
                ScriptVariable variable => variable.Name,
                _ => null
            };
        }

        private static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range ToLspRange(Scriban.Parsing.SourceSpan span)
        {
            return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range
            {
                Start = new Position(span.Start.Line - 1, span.Start.Column - 1),
                End = new Position(span.End.Line - 1, span.End.Column - 1)
            };
        }
    }
}
