using System.Globalization;
using LlmInteractionLogger.Models;
using LlmInteractionLogger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace LlmInteractionLogger.Functions;

public sealed class CaptureLlmInteraction(IInteractionIngestionService ingestion, ILogger<CaptureLlmInteraction> logger)
{
    /// <summary>
    /// Captures one LLM interaction (full prompt, full response, usage and any attachments).
    /// Binary content is stored in Blob Storage; everything else is written to Application Insights.
    /// Accepts <c>application/json</c> (inline base64 / data URIs) or <c>multipart/form-data</c>
    /// (an <c>interaction</c> JSON field plus any number of file parts, streamed straight to blob storage).
    /// </summary>
    [Function(nameof(CaptureLlmInteraction))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "llm/interactions")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            CaptureResult result;
            if (IsJson(request.ContentType))
            {
                result = await ingestion.IngestJsonAsync(request.Body, cancellationToken);
            }
            else if (TryGetBoundary(request.ContentType, out var boundary))
            {
                result = await ingestion.IngestMultipartAsync(request.Body, boundary, cancellationToken);
            }
            else
            {
                return Problem(StatusCodes.Status415UnsupportedMediaType, "Use 'application/json' or 'multipart/form-data'.");
            }

            logger.LogInformation(
                "Captured LLM interaction {CaptureId} ({InteractionId}) with {AttachmentCount} attachment(s).",
                result.CaptureId,
                result.InteractionId,
                result.Attachments.Count);

            return new ObjectResult(result) { StatusCode = StatusCodes.Status201Created };
        }
        catch (InvalidPayloadException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (PayloadTooLargeException ex)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, ex.Message);
        }
    }

    /// <summary>
    /// Captures one half of an interaction as a raw provider payload: <c>/llm/interactions/request</c> for the prompt
    /// and <c>/llm/interactions/response</c> for the response. Designed for API Management inbound/outbound policies,
    /// which can forward the body unchanged and describe the interaction with <c>x-llm-*</c> headers; the two captures
    /// are correlated by <c>x-llm-interaction-id</c> (e.g. <c>context.RequestId</c>).
    /// </summary>
    [Function(nameof(CaptureLlmInteractionPhase))]
    public async Task<IActionResult> CaptureLlmInteractionPhase(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "llm/interactions/{phase}")] HttpRequest request,
        string phase,
        CancellationToken cancellationToken)
    {
        phase = phase.ToLowerInvariant();
        if (phase is not (RawCapture.RequestPhase or RawCapture.ResponsePhase))
        {
            return Problem(StatusCodes.Status404NotFound, "The phase must be 'request' or 'response'.");
        }

        try
        {
            var raw = ReadRawCapture(request, phase);
            var result = await ingestion.IngestRawAsync(request.Body, raw, cancellationToken);
            logger.LogInformation(
                "Captured LLM {Phase} {CaptureId} ({InteractionId}) with {AttachmentCount} attachment(s).",
                phase,
                result.CaptureId,
                result.InteractionId,
                result.Attachments.Count);

            return new ObjectResult(result) { StatusCode = StatusCodes.Status201Created };
        }
        catch (InvalidPayloadException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (PayloadTooLargeException ex)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, ex.Message);
        }
    }

    public const string HeaderPrefix = "x-llm-";
    public const string MetadataHeaderPrefix = "x-llm-metadata-";

    public static RawCapture ReadRawCapture(HttpRequest request, string phase)
    {
        var headers = request.Headers;
        string? Header(string name)
        {
            var value = headers[HeaderPrefix + name].ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        double? latency = null;
        if (Header("latency-ms") is { } latencyText)
        {
            latency = double.TryParse(latencyText, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && ms >= 0
                ? ms
                : throw new InvalidPayloadException("The x-llm-latency-ms header must be a non-negative number.");
        }

        int? statusCode = null;
        if (Header("status-code") is { } statusText)
        {
            statusCode = int.TryParse(statusText, NumberStyles.None, CultureInfo.InvariantCulture, out var code) && code is >= 100 and <= 599
                ? code
                : throw new InvalidPayloadException("The x-llm-status-code header must be an HTTP status code.");
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in headers)
        {
            if (name.Length > MetadataHeaderPrefix.Length && name.StartsWith(MetadataHeaderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                metadata[name[MetadataHeaderPrefix.Length..].ToLowerInvariant()] = value.ToString();
            }
        }

        return new RawCapture
        {
            Phase = phase,
            ContentType = request.ContentType,
            BodyEncoding = Header("body-encoding"),
            InteractionId = Header("interaction-id"),
            ConversationId = Header("conversation-id"),
            UserId = Header("user-id"),
            Application = Header("application"),
            Provider = Header("provider"),
            Model = Header("model"),
            Operation = Header("operation"),
            LatencyMs = latency,
            StatusCode = statusCode,
            Metadata = metadata,
        };
    }

    private static bool IsJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var media)
        && media.MediaType.HasValue
        && (media.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || media.MediaType.Value.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    private static bool TryGetBoundary(string? contentType, out string boundary)
    {
        boundary = string.Empty;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var media)
            || !media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = HeaderUtilities.RemoveQuotes(media.Boundary).Value;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 70)
        {
            return false;
        }

        boundary = value;
        return true;
    }

    private static ObjectResult Problem(int status, string detail) =>
        new(new ProblemDetails { Status = status, Detail = detail }) { StatusCode = status };
}
