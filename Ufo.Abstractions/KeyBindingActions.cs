namespace Ufo.Abstractions;

/// <summary>
/// One thing the user can bind a key to.
/// </summary>
/// <param name="ActionId">
/// The stable identifier stored in the database and sent to the client. It
/// outlives the label, which is display text and may be reworded freely.
/// </param>
/// <param name="Label">What the Settings page calls the action.</param>
/// <param name="Group">The heading it is listed under.</param>
/// <param name="DefaultPrimaryKey">The first binding out of the box, or empty for none.</param>
/// <param name="DefaultSecondaryKey">The second binding out of the box, or empty for none.</param>
public readonly record struct KeyBindingAction(
    string ActionId,
    string Label,
    string Group,
    string DefaultPrimaryKey,
    string DefaultSecondaryKey);

/// <summary>
/// The catalogue of bindable actions, and what each is bound to before anybody
/// changes anything.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately server-side. The client renders whatever list it is handed rather
/// than carrying its own copy, so adding an action is one entry here plus the
/// code that performs it - not a change that has to land on both sides at once,
/// with a window in between where the two disagree about what exists.
/// </para>
/// <para>
/// The defaults follow the file-manager conventions the function keys have had
/// since Norton Commander: F5 copies, F6 moves, F7 makes a folder, F8 deletes.
/// Delete keeps Del as a second binding, because it is what everyone's hand does
/// and taking it away to make room for F8 would be a worse page than either.
/// </para>
/// </remarks>
public static class KeyBindingActions
{
    public const string FileOperationsGroup = "File operations";
    public const string NavigationGroup = "Navigation";

    public const string Rename = "files.rename";
    public const string CreateFile = "files.createFile";
    public const string CreateFolder = "files.createFolder";
    public const string Copy = "files.copy";
    public const string Move = "files.move";
    public const string Delete = "files.delete";
    public const string NavigateBackward = "files.navigateBackward";
    public const string NavigateForward = "files.navigateForward";
    public const string NavigateUpward = "files.navigateUpward";

    /// <summary>
    /// Every bindable action, in the order the Settings page lists them.
    /// </summary>
    public static IReadOnlyList<KeyBindingAction> All { get; } =
    [
        new(Rename, "Rename", FileOperationsGroup, "F2", ""),
        new(CreateFile, "New file", FileOperationsGroup, "", ""),
        new(CreateFolder, "Create folder", FileOperationsGroup, "F7", ""),
        new(Copy, "Copy to other panel", FileOperationsGroup, "F5", ""),
        new(Move, "Move to other panel", FileOperationsGroup, "F6", ""),
        new(Delete, "Delete", FileOperationsGroup, "F8", "Delete"),
        new(NavigateBackward, "Back", NavigationGroup, "Alt+ArrowLeft", ""),
        new(NavigateForward, "Forward", NavigationGroup, "Alt+ArrowRight", ""),
        new(NavigateUpward, "Up one folder", NavigationGroup, "Alt+ArrowUp", "")
    ];

    private static readonly HashSet<string> KnownActionIds =
        All.Select(action => action.ActionId).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether an action id is one this build knows about.
    /// </summary>
    /// <remarks>
    /// Checked on the way in, so a stale client - or a hand-written request -
    /// cannot fill the table with rows for actions that will never be performed.
    /// </remarks>
    public static bool IsKnown(string? actionId) =>
        !string.IsNullOrWhiteSpace(actionId) && KnownActionIds.Contains(actionId);
}
