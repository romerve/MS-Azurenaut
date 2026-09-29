using System.Text.Json.Nodes;
using LlmInteractionLogger.Models;
using LlmInteractionLogger.Services;

namespace LlmInteractionLogger.Tests;

public class BinaryContentExtractorTests
{
    private static (IReadOnlyList<ExtractedBinary> Found, JsonNode Root) Extract(string json, int maxAttachments = 10, long maxBytes = 1_000_000)
    {
        var root = JsonNode.Parse(json)!;
        var counter = 0;
        var found = new BinaryContentExtractor(256, maxAttachments, maxBytes).Extract(root, () => $"att-{++counter:D3}");
        return (found, root);
    }

    [Fact]
    public void Extracts_OpenAI_image_data_uri_and_replaces_it_with_reference()
    {
        var json = $$$"""
        {"request":{"model":"gpt-4o","messages":[{"role":"user","content":[
          {"type":"text","text":"What is in this image?"},
          {"type":"image_url","image_url":{"url":"{{{Samples.DataUri("image/png", Samples.Png)}}}","detail":"high"}}]}]}}
        """;

        var (found, root) = Extract(json);

        var binary = Assert.Single(found);
        Assert.Equal("att-001", binary.Id);
        Assert.Equal("image/png", binary.ContentType);
        Assert.Equal(Samples.Png, binary.Data);
        Assert.Equal("$.request.messages[0].content[1].image_url.url", binary.JsonPath);
        Assert.Equal("llm-attachment://att-001", root["request"]!["messages"]![0]!["content"]![1]!["image_url"]!["url"]!.GetValue<string>());
        Assert.Equal("What is in this image?", root["request"]!["messages"]![0]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Extracts_OpenAI_input_audio_using_format_hint()
    {
        var json = $$$"""{"request":{"messages":[{"role":"user","content":[{"type":"input_audio","input_audio":{"data":"{{{Samples.B64(Samples.Wav)}}}","format":"wav"}}]}]}}""";

        var binary = Assert.Single(Extract(json).Found);

        Assert.Equal("audio/wav", binary.ContentType);
        Assert.Equal("$.request.messages[0].content[0].input_audio.data", binary.JsonPath);
    }

    [Fact]
    public void Extracts_OpenAI_file_data_with_filename()
    {
        var json = $$$"""{"request":{"messages":[{"role":"user","content":[{"type":"file","file":{"filename":"report.pdf","file_data":"{{{Samples.DataUri("application/pdf", Samples.Pdf)}}}"}}]}]}}""";

        var binary = Assert.Single(Extract(json).Found);

        Assert.Equal("application/pdf", binary.ContentType);
        Assert.Equal("report.pdf", binary.FileName);
    }

    [Fact]
    public void Extracts_Anthropic_base64_source()
    {
        var json = $$$"""{"request":{"messages":[{"role":"user","content":[{"type":"image","source":{"type":"base64","media_type":"image/png","data":"{{{Samples.B64(Samples.Png)}}}"}}]}]}}""";

        var (found, root) = Extract(json);

        Assert.Equal("image/png", Assert.Single(found).ContentType);
        Assert.Equal("base64", root["request"]!["messages"]![0]!["content"]![0]!["source"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Extracts_Gemini_inline_video()
    {
        var json = $$$"""{"request":{"contents":[{"role":"user","parts":[{"text":"Summarize"},{"inlineData":{"mimeType":"video/mp4","data":"{{{Samples.B64(Samples.Mp4)}}}"}}]}]}}""";

        var binary = Assert.Single(Extract(json).Found);

        Assert.Equal("video/mp4", binary.ContentType);
        Assert.Equal("$.request.contents[0].parts[1].inlineData.data", binary.JsonPath);
    }

    [Fact]
    public void Extracts_generated_images_from_response_and_sniffs_type()
    {
        var png = Samples.Png.Concat(new byte[300]).ToArray();
        var json = $$$"""{"response":{"data":[{"b64_json":"{{{Samples.B64(png)}}}","revised_prompt":"a cat"}]}}""";

        var binary = Assert.Single(Extract(json).Found);

        Assert.Equal("image/png", binary.ContentType);
        Assert.Equal("$.response.data[0].b64_json", binary.JsonPath);
    }

    [Fact]
    public void Extracts_data_uri_items_in_arrays()
    {
        var json = $$$"""{"request":{"images":["{{{Samples.DataUri("image/png", Samples.Png)}}}"]}}""";

        var (found, root) = Extract(json);

        Assert.Single(found);
        Assert.Equal("llm-attachment://att-001", root["request"]!["images"]![0]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"request":{"messages":[{"role":"user","content":[{"type":"image_url","image_url":{"url":"https://example.com/cat.png"}}]}]}}""")]
    [InlineData("""{"request":{"data":"short"}}""")]
    [InlineData("""{"request":{"text":"aGVsbG8gd29ybGQ="}}""")]
    [InlineData("""{"request":{"url":"data:text/plain,hello%20world"}}""")]
    public void Leaves_non_binary_content_untouched(string json)
    {
        var (found, root) = Extract(json);

        Assert.Empty(found);
        Assert.Equal(JsonNode.Parse(json)!.ToJsonString(), root.ToJsonString());
    }

    [Fact]
    public void Leaves_long_natural_language_in_binary_named_field()
    {
        var text = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 20));
        var (found, _) = Extract($$$"""{"request":{"data":"{{{text}}}"}}""");

        Assert.Empty(found);
    }

    [Fact]
    public void Enforces_attachment_count_limit()
    {
        var uri = Samples.DataUri("image/png", Samples.Png);
        var json = $$$"""{"request":{"images":["{{{uri}}}","{{{uri}}}","{{{uri}}}"]}}""";

        Assert.Throws<PayloadTooLargeException>(() => Extract(json, maxAttachments: 2));
    }

    [Fact]
    public void Enforces_attachment_size_limit()
    {
        var json = $$$"""{"request":{"images":["{{{Samples.DataUri("video/mp4", Samples.Mp4)}}}"]}}""";

        Assert.Throws<PayloadTooLargeException>(() => Extract(json, maxBytes: 100));
    }
}
