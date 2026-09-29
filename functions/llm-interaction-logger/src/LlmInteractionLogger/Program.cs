using System.Diagnostics.Metrics;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Storage.Blobs;
using LlmInteractionLogger.Options;
using LlmInteractionLogger.Services;
using LlmInteractionLogger.Telemetry;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddOptions<LlmLoggingOptions>()
    .Bind(builder.Configuration.GetSection(LlmLoggingOptions.SectionName))
    .Validate(o => o.ContentChunkSize is >= 100 and <= 8192, "LlmLogging:ContentChunkSize must be between 100 and 8192.")
    .Validate(o => o.MaxAttachments > 0 && o.MaxAttachmentBytes > 0 && o.MaxJsonBytes > 0, "LlmLogging size limits must be positive.")
    .ValidateOnStart();

var managedIdentityClientId = builder.Configuration[$"{LlmLoggingOptions.SectionName}:{nameof(LlmLoggingOptions.ManagedIdentityClientId)}"];
TokenCredential credential = string.IsNullOrWhiteSpace(managedIdentityClientId)
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId));

builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<LlmLoggingOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(o.BlobConnectionString))
    {
        return new BlobServiceClient(o.BlobConnectionString);
    }

    if (!string.IsNullOrWhiteSpace(o.BlobServiceUri))
    {
        return new BlobServiceClient(new Uri(o.BlobServiceUri), credential);
    }

    throw new InvalidOperationException(
        $"Configure {LlmLoggingOptions.SectionName}:BlobServiceUri (managed identity) or {LlmLoggingOptions.SectionName}:BlobConnectionString (local development).");
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBlobArchive, BlobArchive>();
builder.Services.AddSingleton<ILlmTelemetry>(sp => new LlmTelemetry(
    sp.GetRequiredService<ILogger<LlmTelemetry>>(),
    sp.GetRequiredService<IMeterFactory>(),
    sp.GetRequiredService<IOptions<LlmLoggingOptions>>().Value.ContentChunkSize));
builder.Services.AddSingleton<IInteractionIngestionService, InteractionIngestionService>();

// Interaction records are the product of this app; keep them at Information regardless of default log filters.
builder.Logging.AddFilter<OpenTelemetryLoggerProvider>(typeof(LlmTelemetry).FullName, LogLevel.Information);

var otel = builder.Services.AddOpenTelemetry()
    .UseFunctionsWorkerDefaults()
    .WithMetrics(metrics => metrics.AddMeter(LlmTelemetry.MeterName));

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    var useEntraAuth = builder.Configuration["APPLICATIONINSIGHTS_AUTHENTICATION_STRING"]?
        .Contains("Authorization=AAD", StringComparison.OrdinalIgnoreCase) == true;

    otel.UseAzureMonitorExporter(exporter =>
    {
        // The exporter defaults to rate-limited sampling (5 traces/s) and drops logs that belong to unsampled traces.
        // Every prompt/response chunk must reach Application Insights, so sampling is disabled.
        exporter.SamplingRatio = 1.0F;
        exporter.TracesPerSecond = null;
        exporter.EnableTraceBasedLogsSampler = false;
        if (useEntraAuth)
        {
            exporter.Credential = credential;
        }
    });
}

builder.Build().Run();
