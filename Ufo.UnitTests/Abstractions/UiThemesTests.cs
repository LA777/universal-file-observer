using FluentAssertions;
using Ufo.Abstractions;

namespace Ufo.UnitTests.Abstractions;

/// <summary>
/// These values cross three boundaries verbatim — SQLite TEXT, JSON, and the CSS
/// class the Angular client builds from them — so the exact strings, and the
/// exactness of the comparison, are part of the contract.
/// </summary>
public class UiThemesTests : BaseTest
{
    [Theory]
    [InlineData(UiThemes.Light)]
    [InlineData(UiThemes.Dark)]
    public void IsSupported_AcceptsEveryThemeInAll(string theme)
    {
        UiThemes.IsSupported(theme).Should().BeTrue();
    }

    [Fact]
    public void All_HoldsExactlyTheTwoShippedThemes()
    {
        UiThemes.All.Should().BeEquivalentTo([UiThemes.Light, UiThemes.Dark]);
    }

    [Fact]
    public void ThemeNames_AreTheLowercaseStringsTheClientUsesAsCssClasses()
    {
        // styles.css keys off :root.theme-light, so a change here silently
        // unstyles the application.
        UiThemes.Light.Should().Be("light");
        UiThemes.Dark.Should().Be("dark");
    }

    [Fact]
    public void Default_IsDark_AndIsItselfSupported()
    {
        UiThemes.Default.Should().Be(UiThemes.Dark);
        UiThemes.IsSupported(UiThemes.Default).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("solarized")]
    public void IsSupported_RejectsAbsentAndUnknownThemes(string? theme)
    {
        UiThemes.IsSupported(theme).Should().BeFalse();
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("DARK")]
    [InlineData("Dark")]
    [InlineData(" light")]
    [InlineData("light ")]
    public void IsSupported_IsOrdinal_SoNearMissesAreRejectedRatherThanNormalised(string theme)
    {
        // Deliberate: the value is written to the database and handed back to the
        // client as a CSS class, so accepting "Light" would store a string that
        // no stylesheet matches.
        UiThemes.IsSupported(theme).Should().BeFalse();
    }
}
