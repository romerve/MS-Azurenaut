using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LlmInteractionLogger.Services;

/// <summary>
/// Converts a buffered <c>text/event-stream</c> LLM response into a single JSON document that keeps every event and
/// adds the aggregated output text, id, model, finish reason and token usage (OpenAI / Azure OpenAI chat completions
/// and responses APIs, Anthropic messages and Gemini streamGenerateContent).
/// </summary>
public static class ServerSentEventsParser
{
    public static JsonObject Parse(string body)
    {
        var events = new JsonArray();
        var text = new StringBuilder();
        var finishReasons = new List<string>();
        JsonObject? usage = null;
        string? id = null;
        string? model = null;

        foreach (var (eventName, data) in ReadEvents(body))
        {
            JsonNode? node = null;
            if (data != "[DONE]")
            {
                try
                {
                    node = JsonNode.Parse(data);
                }
                catch (JsonException)
                {
                }
            }

            var entry = new JsonObject();
            if (eventName is not null)
            {
                entry["event"] = eventName;
            }

            entry["data"] = node ?? JsonValue.Create(data);
            events.Add(entry);

            if (node is not JsonObject obj)
            {
                continue;
            }

            // Responses API (response.created / response.completed) and Anthropic (message_start) wrap the metadata.
            foreach (var wrapper in new[] { obj["response"] as JsonObject, obj["message"] as JsonObject, obj })
            {
                if (wrapper is null)
                {
                    continue;
                }

                id = Text(wrapper, "id", "responseId") ?? id;
                model = Text(wrapper, "model", "modelVersion") ?? model;
                usage = MergeUsage(usage, wrapper["usage"] as JsonObject ?? wrapper["usageMetadata"] as JsonObject);
            }

            var type = Text(obj, "type");
            if (type is "response.output_text.delta" && Text(obj, "delta") is { } responsesDelta)
            {
                text.Append(responsesDelta);
            }
            else if (type is "response.completed" or "response.failed" or "response.incomplete" && obj["response"] is JsonObject final)
            {
                AddReason(finishReasons, Text(final, "status"));
            }

            if (obj["delta"] is JsonObject delta)
            {
                // Anthropic content_block_delta / message_delta.
                if (Text(delta, "text") is { } anthropicText)
                {
                    text.Append(anthropicText);
                }

                AddReason(finishReasons, Text(delta, "stop_reason"));
            }

            if (obj["choices"] is JsonArray choices)
            {
                foreach (var choice in choices.OfType<JsonObject>())
                {
                    if (choice["delta"] is JsonObject choiceDelta && Text(choiceDelta, "content") is { } content)
                    {
                        text.Append(content);
                    }

                    AddReason(finishReasons, Text(choice, "finish_reason"));
                }
            }

            if (obj["candidates"] is JsonArray candidates)
            {
                foreach (var candidate in candidates.OfType<JsonObject>())
                {
                    if (candidate["content"]?["parts"] is JsonArray parts)
                    {
                        foreach (var part in parts.OfType<JsonObject>())
                        {
                            text.Append(Text(part, "text"));
                        }
                    }

                    AddReason(finishReasons, Text(candidate, "finishReason"));
                }
            }
        }

        var result = new JsonObject { ["stream"] = true };
        if (id is not null)
        {
            result["id"] = id;
        }

        if (model is not null)
        {
            result["model"] = model;
        }

        result["output_text"] = text.ToString();
        if (finishReasons.Count > 0)
        {
            result["finish_reason"] = string.Join(',', finishReasons);
        }

        if (usage is not null)
        {
            result["usage"] = usage;
        }

        result["events"] = events;
        return result;
    }

    private static IEnumerable<(string? Event, string Data)> ReadEvents(string body)
    {
        string? eventName = null;
        var data = new StringBuilder();
        var hasData = false;
        using var reader = new StringReader(body);
        string? line;
        while (true)
        {
            line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                if (hasData)
                {
                    yield return (eventName, data.ToString());
                }

                eventName = null;
                data.Clear();
                hasData = false;
                if (line is null)
                {
                    yield break;
                }

                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            if (field == "data")
            {
                if (hasData)
                {
                    data.Append('\n');
                }

                data.Append(value);
                hasData = true;
            }
            else if (field == "event")
            {
                eventName = value;
            }
        }
    }

    private static JsonObject? MergeUsage(JsonObject? current, JsonObject? update)
    {
        if (update is null)
        {
            return current;
        }

        current ??= new JsonObject();
        foreach (var (key, value) in update)
        {
            // Anthropic's message_delta reports input_tokens as 0; keep the real count from message_start.
            if (value is JsonValue v && v.TryGetValue(out long n) && n == 0 && current[key] is not null)
            {
                continue;
            }

            current[key] = value?.DeepClone();
        }

        return current;
    }

    private static void AddReason(List<string> reasons, string? reason)
    {
        if (!string.IsNullOrEmpty(reason) && !reasons.Contains(reason))
        {
            reasons.Add(reason);
        }
    }

    private static string? Text(JsonObject? obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj?[key] is JsonValue value && value.TryGetValue(out string? s))
            {
                return s;
            }
        }

        return null;
    }
}
