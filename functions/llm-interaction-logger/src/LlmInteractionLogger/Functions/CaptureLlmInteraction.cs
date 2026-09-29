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
