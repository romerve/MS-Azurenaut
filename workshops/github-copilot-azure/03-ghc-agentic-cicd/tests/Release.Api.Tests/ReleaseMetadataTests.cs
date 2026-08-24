namespace Release.Api.Tests;

public sealed class ReleaseMetadataTests
{
    [Theory]
    [InlineData("abc123", "abc123")]
    [InlineData("release-1.2.3", "release-1.2.3")]
    [InlineData("contains a secret-shaped value", "unknown")]
    [InlineData("", "unknown")]
    public void Normalize_allows_only_bounded_safe_values(string value, string expected)
    {
        Assert.Equal(expected, ReleaseMetadata.Normalize(value, "unknown"));
    }
}
