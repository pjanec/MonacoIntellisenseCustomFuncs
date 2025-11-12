using ScribanLanguageServer.Server.Hubs;
using ScribanLanguageServer.Core.ApiSpec;
using ScribanLanguageServer.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddSignalR();

// Register core services
builder.Services.AddSingleton<IApiSpecService, ApiSpecService>();
builder.Services.AddSingleton<IScribanParserService, ScribanParserService>();
builder.Services.AddSingleton<IFileSystemService, FileSystemService>();
builder.Services.AddScoped<IDocumentSessionService, DocumentSessionService>();
builder.Services.AddSingleton<IRateLimitService, RateLimitService>();

// Add CORS policy for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Load API spec on startup
var apiSpecService = app.Services.GetRequiredService<IApiSpecService>();
var apiSpecPath = Path.Combine(AppContext.BaseDirectory, "api-spec.json");
var loadResult = await apiSpecService.LoadAsync(apiSpecPath);

if (loadResult.Success)
{
    app.Logger.LogInformation("✅ API Spec loaded successfully from {Path}", apiSpecPath);
    app.Logger.LogInformation("   - Loaded {Count} global entries", apiSpecService.CurrentSpec?.Globals.Count ?? 0);
}
else
{
    app.Logger.LogWarning("⚠️ Failed to load API spec: {Error}", loadResult.ErrorMessage);
    if (loadResult.ValidationErrors.Any())
    {
        foreach (var error in loadResult.ValidationErrors)
        {
            app.Logger.LogWarning("   - {ValidationError}", error);
        }
    }
}

// Use CORS
app.UseCors("AllowFrontend");

// Map SignalR hub
app.MapHub<ScribanHub>("/scribanhub");

app.MapGet("/", () => "Scriban Language Server is running!");

app.Run();
