using System.Text;
using LlmInteractionLogger.Functions;
using LlmInteractionLogger.Models;
using LlmInteractionLogger.Options;
using LlmInteractionLogger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlmInteractionLogger.Tests;

public class CaptureLlmInteractionTests
{
    private static CaptureLlmInteraction CreateFunction(Action<LlmLoggingOptions>? configure = null)
    {
        var options = new LlmLoggingOptions();
        configure?.Invoke(options);
        var service = new InteractionIngestionService(
            new FakeBlobArchive(),
            new FakeTelemetry(),
            Microsoft.Extensions.Options.Options.Create(options),
            TimeProvider.System,
            NullLogger<InteractionIngestionService>.Instance);
        return new CaptureLlmInteraction(service, NullLogger<CaptureLlmInteraction>.Instance);
    }

    private static HttpRequest Request(string contentType, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context.Request;
    }

    [Fact]
    public async Task Returns_201_with_capture_result()
    {
        var body = $$$"""{"request":{"messages":[{"role":"user","content":[{"type":"image_url","image_url":{"url":"{{{Samples.DataUri("image/png", Samples.Png)}}}"}}]}]}}""";

        var result = Assert.IsType<ObjectResult>(await CreateFunction().Run(Request("application/json; charset=utf-8", body), CancellationToken.None));

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        var capture = Assert.IsType<CaptureResult>(result.Value);
        Assert.Single(capture.Attachments);
    }

    [Theory]
    [InlineData("text/plain", "hello", StatusCodes.Status415UnsupportedMediaType)]
    [InlineData("application/json", "{", StatusCodes.Status400BadRequest)]
    [InlineData("multipart/form-data; boundary=abc", "garbage", StatusCodes.Status400BadRequest)]
    public async Task Maps_errors_to_status_codes(string contentType, string body, int expectedStatus)
    {
        var result = Assert.IsType<ObjectResult>(await CreateFunction().Run(Request(contentType, body), CancellationToken.None));

        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.IsType<ProblemDetails>(result.Value);
    }

    [Fact]
    public async Task Returns_413_when_payload_too_large()
    {
        var result = Assert.IsType<ObjectResult>(
            await CreateFunction(o => o.MaxJsonBytes = 5).Run(Request("application/json", """{"request":{}}"""), CancellationToken.None));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }
}
