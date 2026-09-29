using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using LlmInteractionLogger.Models;
using LlmInteractionLogger.Options;
using LlmInteractionLogger.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlmInteractionLogger.Tests;

public class InteractionIngestionServiceTests
{
    private readonly FakeBlobArchive _archive = new();
    private readonly FakeTelemetry _telemetry = new();

    private InteractionIngestionService CreateService(Action<LlmLoggingOptions>? configure = null)
    {
        var options = new LlmLoggingOptions();
        configure?.Invoke(options);
        return new InteractionIngestionService(
            _archive,
            _telemetry,
            Microsoft.Extensions.Options.Options.Create(options),
            TimeProvider.System,
            NullLogger<InteractionIngestionService>.Instance);
    }

    private static Stream Json(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    private static string MultimodalInteraction => $$$"""
    {
      "interactionId": "int-7",
      "provider": "azure-openai",
      "request": {
        "model": "gpt-4o",
        "messages": [
          { "role": "system", "content": "You are helpful." },
          { "role": "user", "content": [
            { "type": "text", "text": "Describe the image and the clip." },
            { "type": "image_url", "image_url": { "url": "{{{Samples.DataUri("image/png", Samples.Png)}}}" } },
            { "type": "input_audio", "input_audio": { "data": "{{{Samples.B64(Samples.Wav)}}}", "format": "wav" } }
          ]}
        ]
      },
      "response": { "choices": [ { "message": { "role": "assistant", "content": "A cat meowing." }, "finish_reason": "stop" } ],
                    "usage": { "prompt_tokens": 900, "completion_tokens": 12 } },
      "attachments": [ { "name": "clip.mp4", "contentType": "video/mp4", "data": "{{{Samples.B64(Samples.Mp4)}}}" },
                       { "name": "remote.png", "url": "https://example.com/remote.png" } ]
    }
    """;

    [Fact]
    public async Task Json_capture_offloads_binaries_and_logs_text_with_references()
    {
        var result = await CreateService().IngestJsonAsync(Json(MultimodalInteraction), CancellationToken.None);

        Assert.Equal("int-7", result.InteractionId);
        Assert.Equal(3, result.Attachments.Count);
        Assert.Equal(["image/png", "audio/wav", "video/mp4"], result.Attachments.Select(a => a.ContentType));
        Assert.Equal("clip.mp4", result.Attachments[2].FileName);
        Assert.All(result.Attachments, a => Assert.Contains($"/{result.CaptureId}/{a.Id}", a.BlobName));
        Assert.EndsWith("att-003-clip.mp4", result.Attachments[2].BlobName);

        var stored = _archive.Blobs[$"llm-attachments/{result.Attachments[1].BlobName}"];
        Assert.Equal(Samples.Wav, stored.Data);
        Assert.Equal("audio/wav", stored.ContentType);
        Assert.Equal(FakeBlobArchive.Sha256(Samples.Wav), result.Attachments[1].Sha256);
        Assert.Equal(result.CaptureId, stored.Metadata["capture_id"]);

        var prompt = _telemetry.Content("prompt");
        Assert.Contains("Describe the image and the clip.", prompt);
        Assert.Contains("llm-attachment://att-001", prompt);
        Assert.Contains("llm-attachment://att-002", prompt);
        Assert.DoesNotContain(Samples.B64(Samples.Wav), prompt);
        Assert.Contains("A cat meowing.", _telemetry.Content("response"));
        var attachmentsContent = _telemetry.Content("attachments");
        Assert.Contains("llm-attachment://att-003", attachmentsContent);
        Assert.Contains("https://example.com/remote.png", attachmentsContent);

        Assert.Equal(912, _telemetry.Summary!.Usage.TotalTokens);
        Assert.Null(result.ArchivedInteractionUri);
    }

    [Fact]
    public async Task Json_capture_can_archive_sanitized_interaction()
    {
        var result = await CreateService(o => o.ArchiveInteractionJson = true).IngestJsonAsync(Json(MultimodalInteraction), CancellationToken.None);

        Assert.NotNull(result.ArchivedInteractionUri);
        var archived = _archive.Blobs.Single(b => b.Key.StartsWith("llm-interactions/", StringComparison.Ordinal));
        var document = JsonNode.Parse(archived.Value.Data)!;
        Assert.Equal(result.CaptureId, document["captureId"]!.GetValue<string>());
        Assert.Equal(3, document["attachments"]!.AsArray().Count);
        Assert.DoesNotContain(Samples.B64(Samples.Png), document.ToJsonString());
    }

    [Fact]
    public async Task Multipart_capture_streams_files_to_blob_storage()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("""{"interactionId":"mp-1","request":{"messages":[{"role":"user","content":"Summarize this video"}]},"response":{"output_text":"It shows a cat."}}""", Encoding.UTF8, "application/json"), "interaction");
        var video = new ByteArrayContent(Samples.Mp4);
        video.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(video, "files", "clip.mp4");
        var image = new ByteArrayContent(Samples.Png);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "files", "../../etc/cat image.png");

        var boundary = form.Headers.ContentType!.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');
        var result = await CreateService().IngestMultipartAsync(await form.ReadAsStreamAsync(), boundary, CancellationToken.None);

        Assert.Equal("mp-1", result.InteractionId);
        Assert.Equal(2, result.Attachments.Count);
        Assert.Equal("video/mp4", result.Attachments[0].ContentType);
        Assert.Equal("multipart:files", result.Attachments[0].Location);
        Assert.Equal(AttachmentSource.Multipart, result.Attachments[0].Source);
        Assert.EndsWith("/att-002-catimage.png", result.Attachments[1].BlobName);
        Assert.Equal(Samples.Mp4, _archive.Blobs[$"llm-attachments/{result.Attachments[0].BlobName}"].Data);
        Assert.Contains("Summarize this video", _telemetry.Content("prompt"));
    }

    [Fact]
    public async Task Multipart_without_interaction_field_is_rejected_and_cleaned_up()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Samples.Png), "files", "a.png");
        var boundary = form.Headers.ContentType!.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');

        await Assert.ThrowsAsync<InvalidPayloadException>(() =>
            CreateService().IngestMultipartAsync(form.ReadAsStream(), boundary, CancellationToken.None));

        Assert.Empty(_archive.Blobs);
        Assert.Single(_archive.Deleted);
    }

    [Fact]
    public async Task Oversized_multipart_file_is_rejected_and_cleaned_up()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("""{"request":{}}"""), "interaction");
        form.Add(new ByteArrayContent(new byte[2048]), "files", "big.bin");
        var boundary = form.Headers.ContentType!.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');

        await Assert.ThrowsAsync<PayloadTooLargeException>(() =>
            CreateService(o => o.MaxAttachmentBytes = 1024).IngestMultipartAsync(form.ReadAsStream(), boundary, CancellationToken.None));

        Assert.Empty(_archive.Blobs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"model":"gpt-4o"}""")]
    public async Task Invalid_json_payloads_are_rejected(string body)
    {
        await Assert.ThrowsAsync<InvalidPayloadException>(() => CreateService().IngestJsonAsync(Json(body), CancellationToken.None));
    }

    [Fact]
    public async Task Json_over_limit_is_rejected()
    {
        await Assert.ThrowsAsync<PayloadTooLargeException>(() =>
            CreateService(o => o.MaxJsonBytes = 10).IngestJsonAsync(Json("""{"request":{"a":"0123456789"}}"""), CancellationToken.None));
    }
}
