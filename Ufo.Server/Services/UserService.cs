using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Server.Services;

public interface IUserService
{
    Task<bool> AnyUserExistsAsync(CancellationToken cancellationToken);
    Task<UserEntity?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken);
    Task<UserEntity> GetUserByIdAsync(Ulid userId, CancellationToken cancellationToken);
    Task<bool> UserExistsAsync(string username, CancellationToken cancellationToken);
    Task<bool> CreateUserAsync(UserEntity user, CancellationToken cancellationToken);
}

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(IUserRepository userRepository, ILogger<UserService> logger)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> AnyUserExistsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AnyUserExistsAsync");
        var count = await _userRepository.GetUserCountAsync(cancellationToken);
        return count > 0;
    }

    public async Task<UserEntity?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetUserByUsernameAsync - Username: {Username}", username);
        return await _userRepository.GetUserByUsernameAsync(username, cancellationToken);
    }

    public async Task<UserEntity> GetUserByIdAsync(Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetUserByIdAsync - UserId: {UserId}", userId);
        return await _userRepository.GetUserByIdAsync(userId, cancellationToken);
    }

    public async Task<bool> UserExistsAsync(string username, CancellationToken cancellationToken)
    {
        _logger.LogInformation("UserExistsAsync - Username: {Username}", username);
        return await _userRepository.UserExistsAsync(username, cancellationToken);
    }

    public async Task<bool> CreateUserAsync(UserEntity user, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateUserAsync - Username: {Username}", user.Name);
        return await _userRepository.CreateUserAsync(user, cancellationToken);
    }
}