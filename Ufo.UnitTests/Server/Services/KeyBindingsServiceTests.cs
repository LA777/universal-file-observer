using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class KeyBindingsServiceTests : BaseTest
{
    private readonly Mock<ILogger<KeyBindingsService>> _loggerMock = new();
    private readonly Mock<IUserKeyBindingsRepository> _repositoryMock = new();
    private readonly Ulid _userId = Ulid.NewUlid();

    /// <summary>The rows the repository will answer a read with.</summary>
    private void GivenSavedRows(params UserKeyBindingEntity[] rows) =>
        _repositoryMock
            .Setup(repository => repository.GetUserKeyBindingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    /// <summary>Captures what a save would have written, and reports success.</summary>
    private List<UserKeyBindingEntity> CaptureSavedRows()
    {
        var savedRows = new List<UserKeyBindingEntity>();

        _repositoryMock
            .Setup(repository => repository.SaveUserKeyBindingsAsync(
                It.IsAny<IReadOnlyList<UserKeyBindingEntity>>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<UserKeyBindingEntity>, Ulid, CancellationToken>(
                (rows, _, _) => savedRows.AddRange(rows))
            .ReturnsAsync(new ServerResult { Result = Result.Success });

        return savedRows;
    }

    private KeyBindingsService CreateSut() => new(_repositoryMock.Object, _loggerMock.Object);

    private static KeyBindingsRequest RequestFor(params (string ActionId, string Primary, string Secondary)[] bindings) =>
        new()
        {
            Bindings = bindings
                .Select(binding => new KeyBindingRequest
                {
                    ActionId = binding.ActionId,
                    PrimaryKey = binding.Primary,
                    SecondaryKey = binding.Secondary
                })
                .ToList()
        };

    #region Reading

    [Fact]
    public async Task GetKeyBindingsAsync_AnswersWithEveryActionEvenWhenNothingIsSaved()
    {
        GivenSavedRows();

        var keyBindings = await CreateSut().GetKeyBindingsAsync(_userId, CancellationToken.None);

        // The page renders whatever it is handed, so a user who has never saved
        // anything still has to receive the whole table.
        keyBindings.Should().HaveCount(KeyBindingActions.All.Count);
        keyBindings.Should().OnlyContain(keyBinding => keyBinding.IsDefault);
    }

    [Fact]
    public async Task GetKeyBindingsAsync_ShipsTheFileManagerFunctionKeys()
    {
        GivenSavedRows();

        var keyBindings = await CreateSut().GetKeyBindingsAsync(_userId, CancellationToken.None);

        string PrimaryFor(string actionId) =>
            keyBindings.Single(keyBinding => keyBinding.ActionId == actionId).PrimaryKey;

        PrimaryFor(KeyBindingActions.Copy).Should().Be("F5");
        PrimaryFor(KeyBindingActions.Move).Should().Be("F6");
        PrimaryFor(KeyBindingActions.CreateFolder).Should().Be("F7");
        PrimaryFor(KeyBindingActions.Delete).Should().Be("F8");

        // Del stays as the second binding: it is what everyone's hand does, and
        // taking it away to make room for F8 would be a worse page than either.
        keyBindings.Single(keyBinding => keyBinding.ActionId == KeyBindingActions.Delete)
            .SecondaryKey.Should().Be("Delete");
    }

    [Fact]
    public async Task GetKeyBindingsAsync_PrefersASavedKeyOverTheDefault()
    {
        GivenSavedRows(new UserKeyBindingEntity
        {
            ActionId = KeyBindingActions.Copy,
            PrimaryKey = "Ctrl+C",
            SecondaryKey = string.Empty,
            UserId = _userId
        });

        var keyBindings = await CreateSut().GetKeyBindingsAsync(_userId, CancellationToken.None);
        var copy = keyBindings.Single(keyBinding => keyBinding.ActionId == KeyBindingActions.Copy);

        copy.PrimaryKey.Should().Be("Ctrl+C");
        copy.IsDefault.Should().BeFalse();
        // The default still travels, so the page can offer to put it back.
        copy.DefaultPrimaryKey.Should().Be("F5");
    }

    [Fact]
    public async Task GetKeyBindingsAsync_KeepsASavedEmptyKeyRatherThanFillingItBackIn()
    {
        // "No key at all" is a preference. Treating an empty string as "nothing
        // saved" would hand the default straight back and make it unremovable.
        GivenSavedRows(new UserKeyBindingEntity
        {
            ActionId = KeyBindingActions.Delete,
            PrimaryKey = string.Empty,
            SecondaryKey = string.Empty,
            UserId = _userId
        });

        var keyBindings = await CreateSut().GetKeyBindingsAsync(_userId, CancellationToken.None);

        keyBindings.Single(keyBinding => keyBinding.ActionId == KeyBindingActions.Delete)
            .PrimaryKey.Should().BeEmpty();
    }

    [Fact]
    public async Task GetKeyBindingsAsync_IgnoresARowForAnActionThisBuildDoesNotHave()
    {
        // Possible after a downgrade. Skipped rather than allowed to throw, so an
        // old row cannot make the Settings page unopenable.
        GivenSavedRows(new UserKeyBindingEntity
        {
            ActionId = "files.somethingRemoved",
            PrimaryKey = "F9",
            UserId = _userId
        });

        var keyBindings = await CreateSut().GetKeyBindingsAsync(_userId, CancellationToken.None);

        keyBindings.Should().HaveCount(KeyBindingActions.All.Count);
        keyBindings.Should().NotContain(keyBinding => keyBinding.ActionId == "files.somethingRemoved");
    }

    #endregion

    #region Saving

    [Fact]
    public async Task SaveKeyBindingsAsync_StoresOnlyWhatDiffersFromTheDefaults()
    {
        var savedRows = CaptureSavedRows();

        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor(
                (KeyBindingActions.Copy, "Ctrl+C", ""),
                (KeyBindingActions.Move, "F6", "")),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);

        // Move was sent at its default, so it is stored as no row - which is what
        // lets a later release re-key that default and have it reach this user.
        savedRows.Should().ContainSingle()
            .Which.ActionId.Should().Be(KeyBindingActions.Copy);
    }

    [Fact]
    public async Task SaveKeyBindingsAsync_RefusesAChordClaimedByTwoActions()
    {
        CaptureSavedRows();

        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor(
                (KeyBindingActions.Copy, "F5", ""),
                (KeyBindingActions.Delete, "F5", "")),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("more than one action");

        _repositoryMock.Verify(
            repository => repository.SaveUserKeyBindingsAsync(
                It.IsAny<IReadOnlyList<UserKeyBindingEntity>>(),
                It.IsAny<Ulid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveKeyBindingsAsync_AllowsASwapBetweenTwoActions()
    {
        // The reason the whole table is saved at once. Judged row by row, giving
        // Move the key Copy still holds would be a conflict, and the user could
        // never express the swap at all.
        var savedRows = CaptureSavedRows();

        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor(
                (KeyBindingActions.Copy, "F6", ""),
                (KeyBindingActions.Move, "F5", "")),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        savedRows.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveKeyBindingsAsync_JudgesConflictsAgainstTheTableTheSaveProduces()
    {
        CaptureSavedRows();

        // Delete is absent, so the save puts it back on its default of F8 - and
        // this hands F8 to Copy as well. Checked against the request alone the
        // clash cannot be seen; checked against the resulting table it is plain.
        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor((KeyBindingActions.Copy, "F8", "")),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("more than one action");
    }

    [Fact]
    public async Task SaveKeyBindingsAsync_AllowsTakingAKeyAnActionIsBeingMovedOffInTheSameSave()
    {
        var savedRows = CaptureSavedRows();

        // Delete gives up F8 in the same request that Copy claims it. Judged
        // against the resulting table there is no clash, and refusing this would
        // make the key impossible to hand over at all.
        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor(
                (KeyBindingActions.Copy, "F8", ""),
                (KeyBindingActions.Delete, "F9", "")),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        savedRows.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("Ctrl+.")]
    [InlineData("Ctrl+/")]
    [InlineData("Shift+!")]
    [InlineData("Ctrl+Plus")]
    [InlineData("Ctrl+Space")]
    public async Task SaveKeyBindingsAsync_AcceptsThePunctuationKeysABrowserReports(string chord)
    {
        CaptureSavedRows();

        // The browser reports Ctrl+. as ".". Refusing it would fail the whole
        // save and discard every other edit in the table alongside it.
        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor((KeyBindingActions.Copy, chord, "")),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
    }

    [Fact]
    public async Task SaveKeyBindingsAsync_TreatsTheSameChordInBothSlotsAsOneBinding()
    {
        var savedRows = CaptureSavedRows();

        await CreateSut().SaveKeyBindingsAsync(
            RequestFor((KeyBindingActions.Copy, "Ctrl+C", "Ctrl+C")),
            _userId,
            CancellationToken.None);

        // A duplicate rather than a clash: the second slot is simply dropped.
        savedRows.Should().ContainSingle().Which.SecondaryKey.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Shift")]
    [InlineData("Ctrl+Alt")]
    public async Task SaveKeyBindingsAsync_RefusesAChordThatIsOnlyModifiers(string chord)
    {
        CaptureSavedRows();

        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor((KeyBindingActions.Copy, chord, "")),
            _userId,
            CancellationToken.None);

        // Bound to Shift alone, the action would fire on every capital letter.
        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("modifier");
    }

    [Theory]
    [InlineData("Ctrl+")]
    [InlineData("F5 F6")]
    [InlineData("Ctrl++C")]
    public async Task SaveKeyBindingsAsync_RefusesAChordItCannotStore(string chord)
    {
        CaptureSavedRows();

        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor((KeyBindingActions.Copy, chord, "")),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }

    [Fact]
    public async Task SaveKeyBindingsAsync_RefusesAnActionThisBuildDoesNotHave()
    {
        CaptureSavedRows();

        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor(("files.notAThing", "F9", "")),
            _userId,
            CancellationToken.None);

        // A row nothing will ever read is not a preference, it is litter with a
        // foreign key.
        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("files.notAThing");
    }

    [Fact]
    public async Task SaveKeyBindingsAsync_AcceptsAnEmptyChordAsAnActionWithNoKey()
    {
        var savedRows = CaptureSavedRows();

        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor((KeyBindingActions.Delete, "", "")),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        // Delete has defaults, so clearing both slots is a real change and is stored.
        savedRows.Should().ContainSingle()
            .Which.PrimaryKey.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveKeyBindingsAsync_DoesNotCountAnEmptyChordAsAConflict()
    {
        var savedRows = CaptureSavedRows();

        var result = await CreateSut().SaveKeyBindingsAsync(
            RequestFor(
                (KeyBindingActions.Copy, "", ""),
                (KeyBindingActions.Move, "", "")),
            _userId,
            CancellationToken.None);

        // Two actions with no key are not two actions sharing one.
        result.Result.Should().Be(Result.Success);
        savedRows.Should().HaveCount(2);
    }

    #endregion
}
