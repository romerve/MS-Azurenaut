using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Release.Api.Tests;

public sealed class EndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_returns_healthy()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", body?.Status);
    }

    [Fact]
    public async Task Version_returns_only_safe_build_metadata()
    {
        var response = await _client.GetAsync("/version");
        var body = await response.Content.ReadFromJsonAsync<ReleaseMetadata>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Release.Api", body?.Service);
        Assert.Matches(@"^\d+\.\d+\.\d+(?:[-.][A-Za-z0-9.-]+)?$", body?.Version ?? "");
        Assert.Matches(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", body?.Revision ?? "");
    }
}
