using System.Text.Json.Nodes;
using LlmInteractionLogger.Services;

namespace LlmInteractionLogger.Tests;

public class InteractionSummaryBuilderTests
{
    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Reads_OpenAI_chat_completion_fields()
    {
        var root = Parse("""
        {
          "provider": "azure-openai",
          "operation": "chat.completions",
          "conversationId": "conv-1",
          "metadata": { "team": "search", "feature flag!": true },
          "request": { "model": "gpt-4o", "user": "user-42", "messages": [] },
          "response": {
            "id": "chatcmpl-123",
            "model": "gpt-4o-2024-08-06",
            "choices": [ { "finish_reason": "stop" }, { "finish_reason": "length" } ],
            "usage": { "prompt_tokens": 120, "completion_tokens": 30, "total_tokens": 150 }
          },
          "startedAt": "2026-09-29T10:00:00Z",
          "completedAt": "2026-09-29T10:00:01.250Z"
        }
        """);

        var summary = InteractionSummaryBuilder.Build("cap", root);

        Assert.Equal("chatcmpl-123", summary.InteractionId);
        Assert.Equal("conv-1", summary.ConversationId);
        Assert.Equal("user-42", summary.UserId);
        Assert.Equal("azure-openai", summary.Provider);
        Assert.Equal("gpt-4o", summary.Model);
        Assert.Equal("success", summary.Status);
        Assert.Equal("stop,length", summary.FinishReason);
        Assert.Equal(1250, summary.LatencyMs);
        Assert.Equal((120L, 30L, 150L), (summary.Usage.InputTokens, summary.Usage.OutputTokens, summary.Usage.TotalTokens));
        Assert.Equal("search", summary.Metadata["team"]);
        Assert.Equal("true", summary.Metadata["featureflag"]);
    }

    [Fact]
    public void Reads_Anthropic_usage_and_stop_reason()
    {
        var root = Parse("""
        {"interactionId":"i-1","latencyMs":812.5,"request":{"model":"claude"},
         "response":{"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":5}}}
        """);

        var summary = InteractionSummaryBuilder.Build("cap", root);

        Assert.Equal("i-1", summary.InteractionId);
        Assert.Equal("end_turn", summary.FinishReason);
        Assert.Equal(812.5, summary.LatencyMs);
        Assert.Equal(15, summary.Usage.TotalTokens);
    }

    [Fact]
    public void Reads_Gemini_usage_metadata()
    {
        var root = Parse("""
        {"request":{},"response":{"modelVersion":"gemini-2.5-pro","candidates":[{"finishReason":"STOP"}],
         "usageMetadata":{"promptTokenCount":7,"candidatesTokenCount":3,"totalTokenCount":10}}}
        """);

        var summary = InteractionSummaryBuilder.Build("cap", root);

        Assert.Equal("gemini-2.5-pro", summary.Model);
        Assert.Equal("STOP", summary.FinishReason);
        Assert.Equal((7L, 3L, 10L), (summary.Usage.InputTokens, summary.Usage.OutputTokens, summary.Usage.TotalTokens));
    }

    [Fact]
    public void Marks_error_when_error_present()
    {
        var summary = InteractionSummaryBuilder.Build("cap", Parse("""{"request":{},"error":{"code":"429"}}"""));

        Assert.Equal("error", summary.Status);
        Assert.Null(summary.Usage.TotalTokens);
    }
}
