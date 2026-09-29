using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LlmInteractionLogger.Functions;

/// <summary>
/// Called by API Management: the inbound policy posts the prompt to <c>/api/llm-log/request</c> and the outbound
/// policy posts the LLM response to <c>/api/llm-log/response</c>. Both carry the same <c>x-interaction-id</c>.
/// The body is written to Application Insights <c>traces</c> as-is, split into parts when it's large.
/// </summary>
public sealed class LogLlmInteraction(ILogger<LogLlmInteraction> logger)
{
    // The body goes in the trace message, which Application Insights keeps up to 32,768 characters
    // (custom properties are cut at 8,192), so a 1 MB prompt becomes ~33 records.
    public const int ChunkSize = 32_000;

    [Function(nameof(LogLlmInteraction))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "llm-log/{phase:regex(^(request|response)$)}")] HttpRequest request,
        string phase)
    {
        var body = await new StreamReader(request.Body).ReadToEndAsync();
        var parts = Split(body);

        for (var i = 0; i < parts.Count; i++)
        {
            var chunk = parts[i];
            // These become customDimensions; the chunk itself is the trace message.
            List<KeyValuePair<string, object?>> fields =
            [
                new("Phase", phase),
                new("InteractionId", Header(request, "x-interaction-id")),
                new("Model", Header(request, "x-model")),
                new("StatusCode", Header(request, "x-status-code")),
                new("LatencyMs", Header(request, "x-latency-ms")),
                new("Part", i + 1),
                new("Parts", parts.Count),
                new("BodyLength", body.Length),
            ];
            logger.Log(LogLevel.Information, default, fields, null, (_, _) => chunk);
        }

        return new OkResult();
    }

    public static List<string> Split(string body)
    {
        var parts = new List<string>();
        var start = 0;
        do
        {
            var length = Math.Min(ChunkSize, body.Length - start);
            // Don't split a surrogate pair (e.g. an emoji) across parts.
            if (start + length < body.Length && char.IsHighSurrogate(body[start + length - 1]))
            {
                length--;
            }

            parts.Add(body.Substring(start, length));
            start += length;
        }
        while (start < body.Length);

        return parts;
    }

    private static string Header(HttpRequest request, string name) => request.Headers[name].ToString();
}
