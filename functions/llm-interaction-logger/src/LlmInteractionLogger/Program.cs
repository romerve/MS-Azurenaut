using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

var otel = builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    otel.UseAzureMonitorExporter(exporter =>
    {
        // Log every prompt/response: the exporter otherwise rate-limits to 5 traces/s and drops logs of unsampled traces.
        exporter.SamplingRatio = 1.0F;
        exporter.TracesPerSecond = null;
        exporter.EnableTraceBasedLogsSampler = false;

        // In Azure the app authenticates to Application Insights with its managed identity (local auth is disabled).
        var clientId = builder.Configuration["AZURE_CLIENT_ID"];
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            exporter.Credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
        }
    });
}

builder.Build().Run();
