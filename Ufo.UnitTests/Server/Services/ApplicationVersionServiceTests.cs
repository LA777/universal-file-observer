using FluentAssertions;
using System.Reflection;
using System.Text.RegularExpressions;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class ApplicationVersionServiceTests
{
    private static readonly Regex ThreeSegmentVersion = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

    [Fact]
    public void Version_OnTheRunningBuild_IsThreeSegments()
    {
        var applicationVersionService = new ApplicationVersionService();

        applicationVersionService.Version.Should().MatchRegex(ThreeSegmentVersion.ToString());
    }

    [Fact]
    public void Version_OnTheRunningBuild_IsNotTheUnknownPlaceholder()
    {
        // The placeholder only appears when the build stamped no version at all,
        // which would mean <Version> stopped reaching the assembly metadata.
        var applicationVersionService = new ApplicationVersionService();

        applicationVersionService.Version.Should().NotBe(ApplicationVersionService.UnknownVersion);
    }

    [Fact]
    public void Version_MatchesTheStaticCurrent_SoEveryReaderAgrees()
    {
        var applicationVersionService = new ApplicationVersionService();

        applicationVersionService.Version.Should().Be(ApplicationVersionService.Current);
    }

    [Fact]
    public void ReadVersion_MatchesTheAssemblysOwnInformationalVersion()
    {
        var assembly = typeof(ApplicationVersionService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        var version = ApplicationVersionService.ReadVersion(assembly);

        informationalVersion.Should().StartWith(version);
    }

    [Fact]
    public void ReadVersion_WithoutAnAssembly_Throws()
    {
        var readingNothing = () => ApplicationVersionService.ReadVersion(null!);

        readingNothing.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("1.2.5", "1.2.5")]
    [InlineData("10.20.30", "10.20.30")]
    // What an AssemblyVersion always looks like.
    [InlineData("1.2.5.0", "1.2.5")]
    // What the SDK stamps when the commit hash is not suppressed.
    [InlineData("1.2.5+e88fb5f1a6aaddd712d8d67b035047e0e7ccf55b", "1.2.5")]
    [InlineData("1.2.5-beta.1", "1.2.5")]
    [InlineData("1.2.5-rc1+abcdef", "1.2.5")]
    [InlineData("  1.2.5  ", "1.2.5")]
    public void ToThreeSegments_WithAStampedVersion_KeepsMajorMinorPatch(string rawVersion, string expected)
    {
        ApplicationVersionService.ToThreeSegments(rawVersion).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2")]
    [InlineData("1")]
    [InlineData("not.a.version")]
    [InlineData("1.2.x")]
    [InlineData("1.2.-5")]
    [InlineData("v1.2.5")]
    public void ToThreeSegments_WithAnythingElse_AnswersNull(string? rawVersion)
    {
        ApplicationVersionService.ToThreeSegments(rawVersion).Should().BeNull();
    }
}
