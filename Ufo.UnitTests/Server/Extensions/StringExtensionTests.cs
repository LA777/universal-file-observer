using FluentAssertions;
using Ufo.Extensions;

namespace Ufo.UnitTests.Server.Extensions;

public class StringExtensionTests : BaseTest
{
    [Theory]
    // Known SHA-256 test vectors.
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void GetHashSha256_ReturnsKnownVector(string input, string expectedHash)
    {
        input.GetHashSha256().Should().Be(expectedHash);
    }

    [Fact]
    public void GetHashSha256_IsDeterministic()
    {
        var text = "snapshot-content";

        text.GetHashSha256().Should().Be(text.GetHashSha256());
    }

    [Fact]
    public void GetHashSha256_DifferentInputsProduceDifferentHashes()
    {
        "file-a".GetHashSha256().Should().NotBe("file-b".GetHashSha256());
    }

    [Fact]
    public void GetHashSha256_ReturnsLowercaseHexOf64Chars()
    {
        var hash = "anything".GetHashSha256();

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
