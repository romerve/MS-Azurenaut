using System.Diagnostics.Metrics;
using LlmInteractionLogger.Models;
using LlmInteractionLogger.Services;
using LlmInteractionLogger.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;

namespace LlmInteractionLogger.Tests;

public class LlmTelemetryTests
{
    [Fact]
    public void Chunk_splits_and_reassembles_without_breaking_surrogate_pairs()
    {
        var text = string.Concat(Enumerable.Repeat("abc😀", 100));

        var chunks = LlmTelemetry.Chunk(text, 100);

        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, c => Assert.True(c.Length <= 100));
        Assert.All(chunks, c => Assert.False(char.IsHighSurrogate(c[^1])));
    }

    [Fact]
    public void Chunk_of_empty_string_is_empty()
    {
        Assert.Empty(LlmTelemetry.Chunk(string.Empty, 100));
    }

    [Fact]
    public void Track_writes_content_chunks_custom_events_and_metrics()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = services.GetRequiredService<IMeterFactory>();
        using var tokens = new MetricCollector<long>(meterFactory, LlmTelemetry.MeterName, "gen_ai.client.token.usage");
        using var bytes = new MetricCollector<long>(meterFactory, LlmTelemetry.MeterName, "llm.attachment.bytes");
        using var interactions = new MetricCollector<long>(meterFactory, LlmTelemetry.MeterName, "llm.interactions");
        var logger = new FakeLogger<LlmTelemetry>();
        var telemetry = new LlmTelemetry(logger, meterFactory, chunkSize: 100);

        var summary = new InteractionSummary
        {
            CaptureId = "cap-1",
            InteractionId = "int-1",
            Phase = "complete",
            Provider = "azure-openai",
            Model = "gpt-4o",
            Status = "success",
            LatencyMs = 1500,
            Usage = new TokenUsage(100, 20, 120),
            Metadata = new Dictionary<string, string> { ["team"] = "search" },
        };
        var attachment = new StoredAttachment
        {
            Id = "att-001",
            Source = AttachmentSource.InlineJson,
            Location = "$.request.x",
            ContentType = "image/png",
            SizeBytes = 42,
            Sha256 = "abc",
            Container = "llm-attachments",
            BlobName = "2026/09/29/cap-1/att-001.png",
            BlobUri = new Uri("https://fake.blob.core.windows.net/llm-attachments/2026/09/29/cap-1/att-001.png"),
        };
        var prompt = new string('p', 250);

        var counts = telemetry.Track(summary, [attachment], [new ContentPart("prompt", prompt), new ContentPart("response", "ok")], null);

        Assert.Equal(3, counts["prompt"]);
        Assert.Equal(1, counts["response"]);

        var records = logger.Collector.GetSnapshot();
        var promptChunks = records.Where(r => r.GetStructuredStateValue("llm.content.kind") == "prompt").ToList();
        Assert.Equal(3, promptChunks.Count);
        Assert.Equal(prompt, string.Concat(promptChunks.OrderBy(r => int.Parse(r.GetStructuredStateValue("llm.content.chunk_index")!)).Select(r => r.GetStructuredStateValue("llm.content"))));
        Assert.All(promptChunks, r => Assert.Equal("cap-1", r.GetStructuredStateValue("llm.capture_id")));

        var interaction = Assert.Single(records, r => r.GetStructuredStateValue(LlmTelemetry.CustomEventAttribute) == LlmTelemetry.InteractionEventName);
        Assert.Equal("gpt-4o", interaction.GetStructuredStateValue("gen_ai.request.model"));
        Assert.Equal("100", interaction.GetStructuredStateValue("gen_ai.usage.input_tokens"));
        Assert.Equal("250", interaction.GetStructuredStateValue("llm.prompt_chars"));
        Assert.Equal("1", interaction.GetStructuredStateValue("llm.attachment_count"));
        Assert.Equal("search", interaction.GetStructuredStateValue("llm.metadata.team"));

        var attachmentEvent = Assert.Single(records, r => r.GetStructuredStateValue(LlmTelemetry.CustomEventAttribute) == LlmTelemetry.AttachmentEventName);
        Assert.Equal("llm-attachment://att-001", attachmentEvent.GetStructuredStateValue("llm.attachment.reference"));
        Assert.Equal(attachment.BlobUri.ToString(), attachmentEvent.GetStructuredStateValue("llm.attachment.blob_uri"));

        Assert.Equal(1, interactions.LastMeasurement!.Value);
        Assert.Equal([100L, 20L], tokens.GetMeasurementSnapshot().Select(m => m.Value));
        Assert.Equal(42, bytes.LastMeasurement!.Value);
        Assert.Equal("image", bytes.LastMeasurement.Tags["llm.attachment.category"]);
    }
}
