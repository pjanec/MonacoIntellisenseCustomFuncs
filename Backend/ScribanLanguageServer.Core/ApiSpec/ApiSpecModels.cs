using System.ComponentModel.DataAnnotations;

namespace ScribanLanguageServer.Core.ApiSpec;

public class ApiSpec
{
    [Required]
    public List<GlobalEntry> Globals { get; set; } = new();
}

public class GlobalEntry
{
    [Required, MinLength(1)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("^(object|function)$")]
    public string Type { get; set; } = string.Empty;

    [Required]
    public string Hover { get; set; } = string.Empty;

    public List<FunctionEntry>? Members { get; set; }
    public List<ParameterEntry>? Parameters { get; set; }
}

public class FunctionEntry
{
    [Required, MinLength(1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Type { get; set; } = "function";

    [Required]
    public string Hover { get; set; } = string.Empty;

    public List<ParameterEntry> Parameters { get; set; } = new();
}

public class ParameterEntry
{
    [Required, MinLength(1)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("^(path|constant|string|number|boolean|any)$")]
    public string Type { get; set; } = string.Empty;

    [Required, RegularExpression("^(file-picker|enum-list|none)$")]
    public string Picker { get; set; } = "none";

    public List<string>? Options { get; set; }
    public List<string>? Macros { get; set; }
}
