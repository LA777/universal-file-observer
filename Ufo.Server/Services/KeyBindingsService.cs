using System.Text.RegularExpressions;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Requests;

namespace Ufo.Server.Services;

public interface IKeyBindingsService
{
    /// <summary>
    /// Every bindable action with the keys in force for this user: their own
    /// where they saved one, the build's default everywhere else. Always the full
    /// list, so the Settings page never has to reconcile two sources itself.
    /// </summary>
    Task<IReadOnlyList<KeyBindingDto>> GetKeyBindingsAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the whole table, rejecting a chord the host cannot express and any
    /// chord claimed by two actions at once.
    /// </summary>
    Task<ServerResult> SaveKeyBindingsAsync(
        KeyBindingsRequest request,
        Ulid userId,
        CancellationToken cancellationToken);
}

public partial class KeyBindingsService : IKeyBindingsService
{
    /// <summary>
    /// The shape of a chord: any number of modifiers, then exactly one key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately a shape rather than a list of every key that exists. Browsers
    /// disagree about names for the long tail - and keyboards disagree with the
    /// browsers - so an allow-list would refuse chords that work perfectly well on
    /// somebody else's machine. What matters is that the string is one line, has
    /// no separators to confuse the comparison, and cannot be a modifier alone.
    /// </para>
    /// <para>
    /// The final key is either a name - "F5", "ArrowLeft", "Space" - or a single
    /// character that is not whitespace and not the separator. Letters and digits
    /// alone were too narrow: the browser reports Ctrl+. as ".", which the client
    /// would record and display quite happily and this would then refuse, taking
    /// every other edit in the table down with it on the same failed save. The
    /// plus key arrives as "Plus" for the same reason it cannot arrive as "+" -
    /// it is what separates the parts.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"^(?:(?:Ctrl|Alt|Shift|Meta)\+)*(?:[A-Za-z0-9]{1,20}|[^\s+])$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ChordPattern { get; }

    /// <summary>
    /// Modifier names, which are never a binding on their own. Holding Shift is
    /// not a shortcut, and storing it as one makes every capital letter fire it.
    /// </summary>
    private static readonly string[] ModifierNames = ["Ctrl", "Alt", "Shift", "Meta"];

    private readonly IUserKeyBindingsRepository _userKeyBindingsRepository;
    private readonly ILogger<KeyBindingsService> _logger;

    public KeyBindingsService(
        IUserKeyBindingsRepository userKeyBindingsRepository,
        ILogger<KeyBindingsService> logger)
    {
        _userKeyBindingsRepository = userKeyBindingsRepository
            ?? throw new ArgumentNullException(nameof(userKeyBindingsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<KeyBindingDto>> GetKeyBindingsAsync(
        Ulid userId,
        CancellationToken cancellationToken)
    {
        var savedRows = await _userKeyBindingsRepository.GetUserKeyBindingsAsync(userId, cancellationToken);

        var savedByActionId = savedRows
            .Where(row => KeyBindingActions.IsKnown(row.ActionId))
            .GroupBy(row => row.ActionId, StringComparer.Ordinal)
            // An id can only appear once - (UserId, ActionId) is UNIQUE - but a
            // row for an action this build has dropped is possible after a
            // downgrade, and it is filtered above rather than allowed to throw.
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return KeyBindingActions.All
            .Select(action => ToDto(action, savedByActionId.GetValueOrDefault(action.ActionId)))
            .ToList();
    }

    public async Task<ServerResult> SaveKeyBindingsAsync(
        KeyBindingsRequest request,
        Ulid userId,
        CancellationToken cancellationToken)
    {
        if (request?.Bindings is null)
        {
            return Rejected("No key bindings were given.");
        }

        var requestedSlots = new Dictionary<string, (string PrimaryKey, string SecondaryKey)>(StringComparer.Ordinal);

        foreach (var binding in request.Bindings)
        {
            if (!KeyBindingActions.IsKnown(binding.ActionId))
            {
                return Rejected($"'{binding.ActionId}' is not an action this version of UFO can bind a key to.");
            }

            var primaryKey = binding.PrimaryKey?.Trim() ?? string.Empty;
            var secondaryKey = binding.SecondaryKey?.Trim() ?? string.Empty;

            var invalidChord = DescribeInvalidChord(primaryKey) ?? DescribeInvalidChord(secondaryKey);

            if (invalidChord is not null)
            {
                return Rejected(invalidChord);
            }

            // The same chord in both slots of one action is not a conflict worth
            // a message - it is a duplicate, and the second one is simply dropped.
            if (string.Equals(primaryKey, secondaryKey, StringComparison.OrdinalIgnoreCase))
            {
                secondaryKey = string.Empty;
            }

            requestedSlots[binding.ActionId!] = (primaryKey, secondaryKey);
        }

        // Judged against the table this save will produce, not against the rows
        // that happen to be in the request. The save replaces everything, so an
        // action left out is not left alone - it goes back to its default, and a
        // request naming only Copy could hand it F8 and silently kill Delete.
        var effectiveSlots = KeyBindingActions.All.ToDictionary(
            action => action.ActionId,
            action => requestedSlots.TryGetValue(action.ActionId, out var requested)
                ? requested
                : (PrimaryKey: action.DefaultPrimaryKey, SecondaryKey: action.DefaultSecondaryKey),
            StringComparer.Ordinal);

        var claimedChords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (actionId, slots) in effectiveSlots)
        {
            var conflict = DescribeConflict(claimedChords, actionId, slots.PrimaryKey)
                ?? DescribeConflict(claimedChords, actionId, slots.SecondaryKey);

            if (conflict is not null)
            {
                return Rejected(conflict);
            }
        }

        // Only what differs from the build is worth a row. An action saved back
        // to its default is stored as no row at all, so a later release that
        // re-keys that default reaches this user too.
        var entities = requestedSlots
            .Where(requested => !IsDefaultFor(requested.Key, requested.Value.PrimaryKey, requested.Value.SecondaryKey))
            .Select(requested => new UserKeyBindingEntity
            {
                ActionId = requested.Key,
                PrimaryKey = requested.Value.PrimaryKey,
                SecondaryKey = requested.Value.SecondaryKey,
                UserId = userId
            })
            .ToList();

        _logger.LogInformation(
            "SaveKeyBindingsAsync - UserId: {UserId}, non-default rows: {Count}",
            userId,
            entities.Count);

        return await _userKeyBindingsRepository.SaveUserKeyBindingsAsync(entities, userId, cancellationToken);
    }

    private static KeyBindingDto ToDto(KeyBindingAction action, UserKeyBindingEntity? savedRow) =>
        new()
        {
            ActionId = action.ActionId,
            Label = action.Label,
            Group = action.Group,
            PrimaryKey = savedRow?.PrimaryKey ?? action.DefaultPrimaryKey,
            SecondaryKey = savedRow?.SecondaryKey ?? action.DefaultSecondaryKey,
            DefaultPrimaryKey = action.DefaultPrimaryKey,
            DefaultSecondaryKey = action.DefaultSecondaryKey,
            IsDefault = savedRow is null
        };

    /// <summary>Why a chord cannot be stored, or null when it can. Empty means "no key".</summary>
    private static string? DescribeInvalidChord(string chord)
    {
        if (chord.Length == 0)
        {
            return null;
        }

        if (!ChordPattern.IsMatch(chord))
        {
            return $"'{chord}' is not a shortcut this server can store.";
        }

        var finalKey = chord.Split('+')[^1];

        return ModifierNames.Contains(finalKey, StringComparer.OrdinalIgnoreCase)
            ? $"'{chord}' is only modifier keys. A shortcut needs a key to go with them."
            : null;
    }

    /// <summary>
    /// Records a chord against the action claiming it, and reports the clash when
    /// somebody else already had it.
    /// </summary>
    private static string? DescribeConflict(
        Dictionary<string, string> claimedChords,
        string actionId,
        string chord)
    {
        if (chord.Length == 0)
        {
            return null;
        }

        if (claimedChords.TryGetValue(chord, out var existingActionId) && existingActionId != actionId)
        {
            return $"'{chord}' is used by more than one action. Each shortcut can only do one thing.";
        }

        claimedChords[chord] = actionId;

        return null;
    }

    private static bool IsDefaultFor(string actionId, string primaryKey, string secondaryKey)
    {
        var action = KeyBindingActions.All.First(candidate => candidate.ActionId == actionId);

        return string.Equals(action.DefaultPrimaryKey, primaryKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(action.DefaultSecondaryKey, secondaryKey, StringComparison.OrdinalIgnoreCase);
    }

    private static ServerResult Rejected(string message) =>
        new()
        {
            ActionName = "Saving Key Bindings.",
            Result = Result.Error,
            Priority = ActionPriority.Highest,
            Message = message
        };
}
