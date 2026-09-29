using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LlmInteractionLogger.Functions;

/// <summary>
/// Called by API Management: the inbound policy posts the prompt to <c>/api/llm-log/request</c> and the outbound
/// policy posts the LLM response to <c>/api/llm-log/response</c>. Both carry the same <c>x-interaction-id</c>.
/// The body is written to Application Insights <c>traces</c> as-is.
/// </summary>
public sealed class LogLlmInteraction(ILogger<LogLlmInteraction> logger)
{
    // Application Insights truncates custom property values at 8,192 characters, so long bodies are split.
    private const int ChunkSize = 8000;

    [Function(nameof(LogLlmInteraction))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "llm-log/{phase:regex(^(request|response)$)}")] HttpRequest request,
        string phase)
    {
        var body = await new StreamReader(request.Body).ReadToEndAsync();
        var interactionId = Header(request, "x-interaction-id");
        var model = Header(request, "x-model");
        var statusCode = Header(request, "x-status-code");
        var latencyMs = Header(request, "x-latency-ms");

        var parts = Math.Max(1, (body.Length + ChunkSize - 1) / ChunkSize);
        for (var i = 0; i < parts; i++)
        {
            var start = i * ChunkSize;
            var chunk = body.Substring(start, Math.Min(ChunkSize, body.Length - start));
            logger.LogInformation(
                "LLM {Phase} {InteractionId} model={Model} status={StatusCode} latencyMs={LatencyMs} part {Part}/{Parts}: {Body}",
                phase, interactionId, model, statusCode, latencyMs, i + 1, parts, chunk);
        }

        return new OkResult();
    }

    private static string Header(HttpRequest request, string name) => request.Headers[name].ToString();
}
