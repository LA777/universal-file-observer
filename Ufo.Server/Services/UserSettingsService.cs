using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Requests;
using Ufo.Server.Mappers;

namespace Ufo.Server.Services;

public interface IUserSettingsService
{
    /// <summary>
    /// The user's settings, falling back to the defaults when they have never
    /// saved any. Never returns <c>null</c>, so the client always has a theme.
    /// </summary>
    Task<UserSettingsDto> GetUserSettingsAsync(Ulid userId, CancellationToken cancellationToken);

    Task<ServerResult> SaveUserSettingsAsync(UserSettingsRequest settings, Ulid userId, CancellationToken cancellationToken);
}

public class UserSettingsService : IUserSettingsService
{
    private readonly IUserSettingsRepository _userSettingsRepository;
    private readonly ILogger<UserSettingsService> _logger;

    public UserSettingsService(IUserSettingsRepository userSettingsRepository, ILogger<UserSettingsService> logger)
    {
        _userSettingsRepository = userSettingsRepository ?? throw new ArgumentNullException(nameof(userSettingsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UserSettingsDto> GetUserSettingsAsync(Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetUserSettingsAsync - UserId: {UserId}", userId);

        var entity = await _userSettingsRepository.GetUserSettingsAsync(userId, cancellationToken);

        return entity?.ToDto() ?? UserSettingsMapper.DefaultsFor(userId);
    }

    public async Task<ServerResult> SaveUserSettingsAsync(UserSettingsRequest settings, Ulid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _logger.LogInformation("SaveUserSettingsAsync - Theme: {Theme}, UserId: {UserId}", settings.Theme, userId);

        if (!UiThemes.IsSupported(settings.Theme))
        {
            // Rejected here rather than by a validation attribute so the client
            // is told which values it may send. Storing an unknown theme would
            // leave the user on a page whose stylesheet does not exist.
            _logger.LogWarning("Rejected unsupported theme '{Theme}' for user: {UserId}", settings.Theme, userId);

            return new ServerResult
            {
                ActionName = "Saving User Settings.",
                Result = Result.Error,
                Priority = ActionPriority.Highest,
                Message = $"Theme '{settings.Theme}' is not supported. Supported themes: {string.Join(", ", UiThemes.All)}."
            };
        }

        var entity = new UserSettingsEntity
        {
            UserId = userId,
            Theme = settings.Theme
        };

        return await _userSettingsRepository.SaveUserSettingsAsync(entity, cancellationToken);
    }
}
