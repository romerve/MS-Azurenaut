using Release.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(ReleaseMetadata.FromEntryAssembly());

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new HealthResponse("healthy")));
app.MapGet("/version", (ReleaseMetadata metadata) => Results.Ok(metadata));

app.Run();

public partial class Program;
