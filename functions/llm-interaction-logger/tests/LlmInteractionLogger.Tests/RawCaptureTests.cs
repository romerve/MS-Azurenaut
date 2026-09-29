using System.Text;
using System.Text.Json.Nodes;
using LlmInteractionLogger.Functions;
using LlmInteractionLogger.Models;
using LlmInteractionLogger.Options;
using LlmInteractionLogger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlmInteractionLogger.Tests;

/// <summary>Captures sent one phase at a time, as API Management inbound/outbound policies do.</summary>
public class RawCaptureTests
{
    private readonly FakeBlobArchive _archive = new();
    private readonly FakeTelemetry _telemetry = new();

    private InteractionIngestionService CreateService() => new(
        _archive,
        _telemetry,
        Microsoft.Extensions.Options.Options.Create(new LlmLoggingOptions()),
        TimeProvider.System,
        NullLogger<InteractionIngestionService>.Instance);

    private CaptureLlmInteraction CreateFunction() => new(CreateService(), NullLogger<CaptureLlmInteraction>.Instance);

    private static HttpRequest Request(string? contentType, byte[] body, params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(body);
        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }

        return context.Request;
    }

    private static RawCapture Raw(string phase, string? contentType, int? statusCode = null) => new()
    {
        Phase = phase,
        ContentType = contentType,
        InteractionId = "apim-req-1",
        StatusCode = statusCode,
    };

    private static Stream Utf8(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Request_phase_uses_headers_and_offloads_inline_images()
    {
        var body = $$$"""{"messages":[{"role":"user","content":[{"type":"text","text":"What is this?"},{"type":"image_url","image_url":{"url":"{{{Samples.DataUri("image/png", Samples.Png)}}}"}}]}]}""";
        var request = Request("application/json", Encoding.UTF8.GetBytes(body),
            ("x-llm-interaction-id", "apim-req-1"),
            ("x-llm-provider", "azure-openai"),
            ("x-llm-model", "gpt-4o-deployment"),
            ("x-llm-operation", "chat"),
            ("x-llm-user-id", "user-9"),
            ("x-llm-metadata-apim-subscription", "team-a"));

        var result = Assert.IsType<ObjectResult>(await CreateFunction().CaptureLlmInteractionPhase(request, "Request", CancellationToken.None));

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        var capture = Assert.IsType<CaptureResult>(result.Value);
        Assert.Equal("apim-req-1", capture.InteractionId);
        Assert.Equal("image/png", Assert.Single(capture.Attachments).ContentType);
    }

    [Fact]
    public async Task Request_phase_summary_is_pending_and_not_counted_as_success()
    {
        await CreateService().IngestRawAsync(Utf8("""{"model":"gpt-4o","messages":[{"role":"user","content":"hi"}]}"""), Raw("request", "application/json"), CancellationToken.None);

        var summary = _telemetry.Summary!;
        Assert.Equal("request", summary.Phase);
        Assert.Null(summary.Status);
        Assert.Equal("gpt-4o", summary.Model);
        Assert.Equal("apim-req-1", summary.InteractionId);
        Assert.Contains("\"hi\"", _telemetry.Content("prompt"));
    }

    [Fact]
    public async Task Response_phase_reads_usage_model_and_status_code()
    {
        var raw = Raw("response", "application/json; charset=utf-8", 200) with { LatencyMs = 812.5, Model = "deployment-name" };
        await CreateService().IngestRawAsync(
            Utf8("""{"id":"chatcmpl-1","model":"gpt-4o-2024-08-06","choices":[{"message":{"content":"Hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":2}}"""),
            raw,
            CancellationToken.None);

        var summary = _telemetry.Summary!;
        Assert.Equal("response", summary.Phase);
        Assert.Equal("success", summary.Status);
        Assert.Equal("gpt-4o-2024-08-06", summary.Model);
        Assert.Equal("apim-req-1", summary.InteractionId);
        Assert.Equal(812.5, summary.LatencyMs);
        Assert.Equal(new TokenUsage(10, 2, 12), summary.Usage);
        Assert.Equal("stop", summary.FinishReason);
        Assert.Equal("200", summary.Metadata["http_status_code"]);
    }

    [Fact]
    public async Task Backend_error_response_is_logged_as_error()
    {
        await CreateService().IngestRawAsync(Utf8("""{"error":{"code":"content_filter","message":"blocked"}}"""), Raw("response", "application/json", 400), CancellationToken.None);

        Assert.Equal("error", _telemetry.Summary!.Status);
        Assert.Contains("content_filter", _telemetry.Content("error"));
    }

    [Fact]
    public async Task Streaming_response_is_aggregated()
    {
        var sse = """
            data: {"id":"chatcmpl-9","model":"gpt-4o","choices":[{"delta":{"role":"assistant","content":""}}]}

            data: {"id":"chatcmpl-9","model":"gpt-4o","choices":[{"delta":{"content":"Hel"}}]}

            data: {"id":"chatcmpl-9","model":"gpt-4o","choices":[{"delta":{"content":"lo"},"finish_reason":"stop"}]}

            data: {"id":"chatcmpl-9","model":"gpt-4o","choices":[],"usage":{"prompt_tokens":5,"completion_tokens":2,"total_tokens":7}}

            data: [DONE]

            """;

        await CreateService().IngestRawAsync(Utf8(sse), Raw("response", "text/event-stream", 200), CancellationToken.None);

        var summary = _telemetry.Summary!;
        Assert.Equal(new TokenUsage(5, 2, 7), summary.Usage);
        Assert.Equal("stop", summary.FinishReason);
        Assert.Equal("gpt-4o", summary.Model);
        var response = JsonNode.Parse(_telemetry.Content("response"))!;
        Assert.Equal("Hello", response["output_text"]!.GetValue<string>());
        Assert.Equal(5, response["events"]!.AsArray().Count);
    }

    [Fact]
    public void Anthropic_and_responses_api_streams_are_aggregated()
    {
        var anthropic = ServerSentEventsParser.Parse("""
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_1","model":"claude-x","usage":{"input_tokens":25,"output_tokens":1}}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hi there"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":15}}

            """);
        Assert.Equal("Hi there", anthropic["output_text"]!.GetValue<string>());
        Assert.Equal("end_turn", anthropic["finish_reason"]!.GetValue<string>());
        Assert.Equal(new TokenUsage(25, 15, 40), InteractionSummaryBuilder.Usage(anthropic["usage"]!.AsObject()));
        Assert.Equal("message_start", anthropic["events"]![0]!["event"]!.GetValue<string>());

        var responses = ServerSentEventsParser.Parse("""
            event: response.output_text.delta
            data: {"type":"response.output_text.delta","delta":"Yes"}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_1","model":"gpt-4.1","status":"completed","usage":{"input_tokens":3,"output_tokens":1,"total_tokens":4}}}

            """);
        Assert.Equal("Yes", responses["output_text"]!.GetValue<string>());
        Assert.Equal("completed", responses["finish_reason"]!.GetValue<string>());
        Assert.Equal("resp_1", responses["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Binary_response_body_is_stored_as_attachment()
    {
        byte[] mp3 = [.. "ID3"u8.ToArray(), .. new byte[500]];

        var result = await CreateService().IngestRawAsync(new MemoryStream(mp3), Raw("response", "audio/mpeg", 200), CancellationToken.None);

        var attachment = Assert.Single(result.Attachments);
        Assert.Equal(AttachmentSource.RawBody, attachment.Source);
        Assert.Equal("audio/mpeg", attachment.ContentType);
        Assert.Equal(mp3, _archive.Blobs[$"{attachment.Container}/{attachment.BlobName}"].Data);
        Assert.Contains(AttachmentReference.For(attachment.Id), _telemetry.Content("response"));
    }

    [Fact]
    public async Task Base64_encoded_binary_body_is_decoded()
    {
        var result = await CreateService().IngestRawAsync(
            Utf8(Samples.B64(Samples.Png)),
            Raw("response", "image/png", 200) with { BodyEncoding = "base64" },
            CancellationToken.None);

        var attachment = Assert.Single(result.Attachments);
        Assert.Equal(Samples.Png, _archive.Blobs[$"{attachment.Container}/{attachment.BlobName}"].Data);
    }

    [Fact]
    public async Task Provider_multipart_request_stores_files_and_logs_fields()
    {
        var content = new MultipartFormDataContent("xyz");
        content.Add(new StringContent("whisper-1"), "model");
        content.Add(new StringContent("en"), "language");
        var file = new ByteArrayContent(Samples.Wav);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(file, "file", "speech.wav");
        var body = new MemoryStream(await content.ReadAsByteArrayAsync());

        var result = await CreateService().IngestRawAsync(body, Raw("request", content.Headers.ContentType!.ToString()), CancellationToken.None);

        var attachment = Assert.Single(result.Attachments);
        Assert.Equal("speech.wav", attachment.FileName);
        Assert.Equal("whisper-1", _telemetry.Summary!.Model);
        var prompt = JsonNode.Parse(_telemetry.Content("prompt"))!;
        Assert.Equal("en", prompt["language"]!.GetValue<string>());
        Assert.Equal(AttachmentReference.For(attachment.Id), prompt["file"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("summary", null, StatusCodes.Status404NotFound)]
    [InlineData("request", "gzip", StatusCodes.Status400BadRequest)]
    public async Task Invalid_phase_or_encoding_is_rejected(string phase, string? encoding, int expected)
    {
        var headers = encoding is null ? [] : new[] { ("x-llm-body-encoding", encoding) };
        var request = Request("application/json", "{}"u8.ToArray(), headers);

        var result = Assert.IsType<ObjectResult>(await CreateFunction().CaptureLlmInteractionPhase(request, phase, CancellationToken.None));

        Assert.Equal(expected, result.StatusCode);
    }

    [Fact]
    public async Task Invalid_latency_header_is_rejected()
    {
        var request = Request("application/json", "{}"u8.ToArray(), ("x-llm-latency-ms", "fast"));

        var result = Assert.IsType<ObjectResult>(await CreateFunction().CaptureLlmInteractionPhase(request, "response", CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public void Metadata_headers_are_collected()
    {
        var request = Request("application/json", [], ("x-llm-metadata-Region", "eastus"), ("x-llm-status-code", "429"), ("x-other", "no"));

        var raw = CaptureLlmInteraction.ReadRawCapture(request, "response");

        Assert.Equal("eastus", raw.Metadata["region"]);
        Assert.Single(raw.Metadata);
        Assert.Equal(429, raw.StatusCode);
    }
}
