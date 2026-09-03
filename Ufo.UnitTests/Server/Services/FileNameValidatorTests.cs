using FluentAssertions;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

/// <summary>
/// The validator answers with the host's own rules, so the tests split into two
/// groups: what every platform agrees on, and what only one of them enforces. The
/// second group is guarded on the running platform rather than skipped, because a
/// Windows-only rule asserted on Linux would be asserting the opposite of the truth.
/// </summary>
public class FileNameValidatorTests : BaseTest
{
    private static FileNameValidator CreateSut() => new();

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("A folder with spaces")]
    [InlineData(".gitignore")]
    [InlineData("report.final.v2.pdf")]
    [InlineData("ünïcödé-名前")]
    public void TryValidate_AcceptsAnOrdinaryName(string name)
    {
        CreateSut().TryValidate(name, out var rejectionReason).Should().BeTrue();

        rejectionReason.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_RejectsAnEmptyName(string? name)
    {
        CreateSut().TryValidate(name, out var rejectionReason).Should().BeFalse();

        rejectionReason.Should().Be("A name is required.");
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void TryValidate_RejectsTheRelativeSegments(string name)
    {
        CreateSut().TryValidate(name, out var rejectionReason).Should().BeFalse();

        rejectionReason.Should().Contain("reserved");
    }

    /// <summary>
    /// The containment check that matters most: everything upstream combines this
    /// name with a folder the path guard has already approved, so a separator
    /// getting through is a write outside that folder.
    /// </summary>
    [Theory]
    [InlineData("nested/child.txt")]
    [InlineData("nested\\child.txt")]
    [InlineData("../escaped.txt")]
    [InlineData("..\\escaped.txt")]
    public void TryValidate_RejectsANameCarryingAPathSeparator(string name)
    {
        CreateSut().TryValidate(name, out var rejectionReason).Should().BeFalse();

        rejectionReason.Should().NotBeEmpty();
    }

    [Fact]
    public void TryValidate_RejectsAControlCharacter()
    {
        CreateSut().TryValidate("bell\u0007name", out var rejectionReason).Should().BeFalse();

        // Named as a class rather than printed: the character itself would render
        // as nothing, leaving the user with an error quoting an empty string.
        rejectionReason.Should().Be("A name may not contain control characters.");
    }

    [Fact]
    public void TryValidate_RejectsANameLongerThanTheHostAccepts()
    {
        var overlongName = new string('a', FileNameValidator.MaximumNameLength + 1);

        CreateSut().TryValidate(overlongName, out var rejectionReason).Should().BeFalse();

        rejectionReason.Should().Contain("255");
    }

    [Fact]
    public void TryValidate_AcceptsANameOfExactlyTheMaximumLength()
    {
        var longestAcceptedName = new string('a', FileNameValidator.MaximumNameLength);

        CreateSut().TryValidate(longestAcceptedName, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("NUL")]
    [InlineData("con.txt")]
    [InlineData("LPT1")]
    public void TryValidate_RejectsAWindowsDeviceName_OnWindowsOnly(string name)
    {
        var isAccepted = CreateSut().TryValidate(name, out _);

        // "NUL" is a perfectly ordinary file name on Linux, and refusing it there
        // would be the validator inventing a rule the host does not have.
        isAccepted.Should().Be(!OperatingSystem.IsWindows());
    }

    [Theory]
    [InlineData("trailing dot.")]
    [InlineData("trailing space ")]
    public void TryValidate_RejectsATrailingDotOrSpace_OnWindowsOnly(string name)
    {
        var isAccepted = CreateSut().TryValidate(name, out _);

        isAccepted.Should().Be(!OperatingSystem.IsWindows());
    }

    [Fact]
    public void Rules_DescribeTheSamePlatformTheValidatorEnforces()
    {
        var sut = CreateSut();

        // The client applies these rather than the validator itself, so a rule the
        // server enforces and does not publish is one the user only meets as a
        // failed request.
        sut.Rules.MaximumLength.Should().Be(FileNameValidator.MaximumNameLength);
        sut.Rules.InvalidCharacters.Should().Contain("/").And.Contain("\\");
        sut.Rules.RejectsTrailingDotOrSpace.Should().Be(OperatingSystem.IsWindows());
        sut.Rules.IsCaseSensitive.Should().Be(OperatingSystem.IsLinux());

        if (OperatingSystem.IsWindows())
        {
            sut.Rules.ReservedNames.Should().Contain("NUL");
        }
        else
        {
            sut.Rules.ReservedNames.Should().BeEmpty();
        }
    }

    [Fact]
    public void Rules_LeaveOutTheCharactersThatCannotBeShown()
    {
        // The list exists to be printed back to the user; a control character in
        // it turns the message into a run of invisible glyphs.
        CreateSut().Rules.InvalidCharacters.Any(char.IsControl).Should().BeFalse();
    }
}
