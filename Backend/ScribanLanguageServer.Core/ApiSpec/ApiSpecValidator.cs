namespace ScribanLanguageServer.Core.ApiSpec;

/// <summary>
/// Validates ApiSpec.json for correctness and consistency
/// </summary>
public static class ApiSpecValidator
{
    public static ValidationResult Validate(ApiSpec spec)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (spec?.Globals == null)
        {
            errors.Add("ApiSpec must have a 'globals' array");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        if (!spec.Globals.Any())
        {
            errors.Add("ApiSpec must have at least one global entry");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        // Check for duplicate global names (case-insensitive)
        var duplicateGlobals = spec.Globals
            .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateGlobals.Any())
        {
            errors.Add($"Duplicate global names: {string.Join(", ", duplicateGlobals)}");
        }

        // Validate each global entry
        foreach (var global in spec.Globals)
        {
            ValidateGlobalEntry(global, errors, warnings);
        }

        // Check for reserved names
        var reservedNames = new[] { "for", "if", "end", "else", "while", "func", "ret" };
        var conflicts = spec.Globals
            .Where(g => reservedNames.Contains(g.Name, StringComparer.OrdinalIgnoreCase))
            .Select(g => g.Name)
            .ToList();

        if (conflicts.Any())
        {
            errors.Add($"Reserved Scriban keywords used as global names: {string.Join(", ", conflicts)}");
        }

        return new ValidationResult
        {
            IsValid = !errors.Any(),
            Errors = errors,
            Warnings = warnings
        };
    }

    private static void ValidateGlobalEntry(
        GlobalEntry entry,
        List<string> errors,
        List<string> warnings)
    {
        var context = $"Global '{entry.Name}'";

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            errors.Add($"{context}: Name cannot be empty");
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Type))
        {
            errors.Add($"{context}: Type is required");
        }
        else if (entry.Type != "object" && entry.Type != "function")
        {
            errors.Add($"{context}: Type must be 'object' or 'function', got '{entry.Type}'");
        }

        if (string.IsNullOrWhiteSpace(entry.Hover))
        {
            warnings.Add($"{context}: Hover documentation is empty");
        }

        // Type-specific validation
        if (entry.Type == "object")
        {
            ValidateObjectEntry(entry, context, errors, warnings);
        }
        else if (entry.Type == "function")
        {
            ValidateFunctionEntry(entry, context, errors, warnings);
        }
    }

    private static void ValidateObjectEntry(
        GlobalEntry entry,
        string context,
        List<string> errors,
        List<string> warnings)
    {
        if (entry.Members == null || !entry.Members.Any())
        {
            errors.Add($"{context}: Objects must have at least one member");
            return;
        }

        // Check for duplicate member names (case-insensitive)
        var duplicateMembers = entry.Members
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateMembers.Any())
        {
            errors.Add($"{context}: Duplicate member names: {string.Join(", ", duplicateMembers)}");
        }

        // Validate each member
        foreach (var member in entry.Members)
        {
            ValidateFunctionEntry(member, $"{context}.{member.Name}", errors, warnings);
        }
    }

    private static void ValidateFunctionEntry(
        dynamic entry,
        string context,
        List<string> errors,
        List<string> warnings)
    {
        if (entry.Parameters == null)
        {
            errors.Add($"{context}: Parameters array is required (use empty array if no parameters)");
            return;
        }

        // Check for duplicate parameter names
        var duplicateParams = ((IEnumerable<dynamic>)entry.Parameters)
            .GroupBy(p => (string)p.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateParams.Any())
        {
            errors.Add($"{context}: Duplicate parameter names: {string.Join(", ", duplicateParams)}");
        }

        // Validate each parameter
        int index = 0;
        foreach (var param in entry.Parameters)
        {
            ValidateParameter(param, $"{context}.param[{index}]({param.Name})", errors, warnings);
            index++;
        }
    }

    private static void ValidateParameter(
        ParameterEntry param,
        string context,
        List<string> errors,
        List<string> warnings)
    {
        // Validate type
        var validTypes = new[] { "path", "constant", "string", "number", "boolean", "any" };
        if (!validTypes.Contains(param.Type))
        {
            errors.Add($"{context}: Invalid type '{param.Type}'. Must be one of: {string.Join(", ", validTypes)}");
        }

        // Validate picker
        var validPickers = new[] { "file-picker", "enum-list", "none" };
        if (!validPickers.Contains(param.Picker))
        {
            errors.Add($"{context}: Invalid picker '{param.Picker}'. Must be one of: {string.Join(", ", validPickers)}");
        }

        // Picker-specific validation
        if (param.Picker == "enum-list")
        {
            if (param.Options == null || !param.Options.Any())
            {
                errors.Add($"{context}: Picker 'enum-list' requires non-empty 'options' array");
            }

            if (param.Type != "constant")
            {
                warnings.Add($"{context}: Picker 'enum-list' typically uses type 'constant', found '{param.Type}'");
            }
        }

        if (param.Picker == "file-picker")
        {
            if (param.Type != "path")
            {
                warnings.Add($"{context}: Picker 'file-picker' typically uses type 'path', found '{param.Type}'");
            }
        }

        // Validate macros
        if (param.Macros != null && param.Macros.Any())
        {
            if (param.Type != "string")
            {
                errors.Add($"{context}: Macros are only valid for type 'string', found '{param.Type}'");
            }
        }

        // Warn about unused options
        if (param.Options != null && param.Options.Any() && param.Picker != "enum-list")
        {
            warnings.Add($"{context}: Options defined but picker is '{param.Picker}' (not 'enum-list')");
        }
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
