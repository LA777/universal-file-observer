using Ufo.Abstractions;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Server.Services;

/// <summary>
/// The Settings page's danger zone: deleting one kind of the user's data at a
/// time. Each call is scoped to the calling user; there is no way through here
/// to anyone else's rows.
/// </summary>
public interface IUserDataService
{
    /// <summary>Every snapshot, its tree and machine identity, and the labels.</summary>
    Task<ServerResult> DeleteSnapshotsAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>Every flag, rating and tag the user has put on paths on disk.</summary>
    Task<ServerResult> DeleteFileSystemDataAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>The theme, the rebound shortcuts and the locked folder tabs.</summary>
    Task<ServerResult> DeleteSettingsAsync(Ulid userId, CancellationToken cancellationToken);
}

public class UserDataService : IUserDataService
{
    private readonly IUserDataRepository _userDataRepository;
    private readonly ILogger<UserDataService> _logger;

    public UserDataService(IUserDataRepository userDataRepository, ILogger<UserDataService> logger)
    {
        _userDataRepository = userDataRepository ?? throw new ArgumentNullException(nameof(userDataRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServerResult> DeleteSnapshotsAsync(Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteSnapshotsAsync - UserId: {UserId}", userId);

        var deletedSnapshots = await _userDataRepository.DeleteSnapshotsAsync(userId, cancellationToken);

        return Succeeded("Deleting Snapshots.", $"Deleted {deletedSnapshots} snapshot(s).");
    }

    public async Task<ServerResult> DeleteFileSystemDataAsync(Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteFileSystemDataAsync - UserId: {UserId}", userId);

        var deletedRows = await _userDataRepository.DeleteFileSystemDataAsync(userId, cancellationToken);

        return Succeeded("Deleting File System Data.", $"Deleted {deletedRows} flag(s), rating(s) and tag(s).");
    }

    public async Task<ServerResult> DeleteSettingsAsync(Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteSettingsAsync - UserId: {UserId}", userId);

        var deletedRows = await _userDataRepository.DeleteSettingsAsync(userId, cancellationToken);

        return Succeeded("Deleting Settings.", $"Deleted {deletedRows} setting(s).");
    }

    // Nothing to delete is still success: the state the user asked for is the
    // state they are in. A count of zero is reported, never an error.
    private static ServerResult Succeeded(string actionName, string message) => new()
    {
        ActionName = actionName,
        Result = Result.Success,
        Priority = ActionPriority.Highest,
        Message = message
    };
}
