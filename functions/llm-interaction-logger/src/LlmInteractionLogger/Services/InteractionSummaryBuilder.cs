using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmInteractionLogger.Models;

namespace LlmInteractionLogger.Services;

/// <summary>
/// Builds a normalized <see cref="InteractionSummary"/> from the capture envelope, falling back to fields in the raw
/// provider request/response (OpenAI / Azure OpenAI chat + responses APIs, Anthropic messages, Gemini generateContent).
/// </summary>
public static class InteractionSummaryBuilder
{
    private const int MaxIdentifierLength = 256;
    private const int MaxMetadataKeyLength = 64;

    public static InteractionSummary Build(string captureId, JsonObject root)
    {
        var request = root["request"] as JsonObject;
        var response = root["response"] as JsonObject;

        var startedAt = Date(root, "startedAt", "started_at", "requestTimestamp");
        var completedAt = Date(root, "completedAt", "completed_at", "responseTimestamp");
        var latency = Number(root, "latencyMs", "latency_ms", "durationMs", "duration_ms");
        if (latency is null && startedAt is not null && completedAt is not null && completedAt >= startedAt)
        {
            latency = (completedAt.Value - startedAt.Value).TotalMilliseconds;
        }

        return new InteractionSummary
        {
            CaptureId = captureId,
            InteractionId = Id(Text(root, "interactionId", "interaction_id") ?? Text(response, "id")),
            ConversationId = Id(Text(root, "conversationId", "conversation_id", "sessionId", "session_id", "threadId", "thread_id")),
            UserId = Id(Text(root, "userId", "user_id") ?? Text(request, "user")),
            Application = Id(Text(root, "application", "app")),
            Provider = Id(Text(root, "provider", "system")),
            Model = Id(Text(root, "model") ?? Text(request, "model") ?? Text(response, "model", "modelVersion")),
            Operation = Id(Text(root, "operation", "operationName", "operation_name")),
            Phase = Phase(root),
            Status = Id(Text(root, "status") ?? (root["error"] is not null ? "error" : root["response"] is not null ? "success" : null)),
            FinishReason = Id(Text(root, "finishReason", "finish_reason") ?? FinishReason(response)),
            LatencyMs = latency,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Usage = Usage(root["usage"] as JsonObject ?? response?["usage"] as JsonObject ?? response?["usageMetadata"] as JsonObject),
            Metadata = Metadata(root["metadata"] as JsonObject),
        };
    }

    public static string Phase(JsonObject root)
    {
        var hasRequest = root["request"] is not null;
        var hasOutcome = root["response"] is not null || root["error"] is not null;
        return hasRequest && hasOutcome ? "complete" : hasRequest ? "request" : "response";
    }

    public static TokenUsage Usage(JsonObject? usage)
    {
        var input = Integer(usage, "inputTokens", "input_tokens", "promptTokens", "prompt_tokens", "promptTokenCount");
        var output = Integer(usage, "outputTokens", "output_tokens", "completionTokens", "completion_tokens", "candidatesTokenCount");
        var total = Integer(usage, "totalTokens", "total_tokens", "totalTokenCount");
        if (total is null && (input is not null || output is not null))
        {
            total = (input ?? 0) + (output ?? 0);
        }

        return new TokenUsage(input, output, total);
    }

    private static string? FinishReason(JsonObject? response)
    {
        if (response is null)
        {
            return null;
        }

        var reasons = new List<string>();
        foreach (var arrayName in new[] { "choices", "candidates" })
        {
            if (response[arrayName] is JsonArray items)
            {
                reasons.AddRange(items.OfType<JsonObject>()
                    .Select(i => Text(i, "finish_reason", "finishReason"))
                    .OfType<string>());
            }
        }

        if (reasons.Count > 0)
        {
            return string.Join(',', reasons.Distinct());
        }

        return Text(response, "finish_reason", "stop_reason", "stopReason", "status");
    }

    private static IReadOnlyDictionary<string, string> Metadata(JsonObject? metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata is null)
        {
            return result;
        }

        foreach (var (key, value) in metadata)
        {
            var safeKey = SafeKey(key);
            if (safeKey.Length == 0 || value is null)
            {
                continue;
            }

            result[safeKey] = value is JsonValue v && v.TryGetValue(out string? s) ? s : value.ToJsonString();
        }

        return result;
    }

    public static string SafeKey(string key)
    {
        var builder = new StringBuilder(Math.Min(key.Length, MaxMetadataKeyLength));
        foreach (var c in key)
        {
            if (builder.Length == MaxMetadataKeyLength)
            {
                break;
            }

            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string? Id(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= MaxIdentifierLength ? value : value[..MaxIdentifierLength];

    private static string? Text(JsonObject? obj, params string[] keys)
    {
        if (obj is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (obj[key] is JsonValue value)
            {
                if (value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }

                if (value.GetValueKind() is JsonValueKind.Number)
                {
                    return value.ToJsonString();
                }
            }
        }

        return null;
    }

    private static double? Number(JsonObject? obj, params string[] keys)
    {
        if (obj is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (obj[key] is JsonValue value)
            {
                if (value.GetValueKind() is JsonValueKind.Number)
                {
                    return value.GetValue<double>();
                }

                if (value.TryGetValue(out string? text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static long? Integer(JsonObject? obj, params string[] keys)
    {
        var number = Number(obj, keys);
        return number is null ? null : (long)number.Value;
    }

    private static DateTimeOffset? Date(JsonObject obj, params string[] keys)
    {
        var text = Text(obj, keys);
        return text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
