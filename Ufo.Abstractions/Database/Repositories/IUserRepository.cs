using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface IUserRepository
{
    Task<UserEntity?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> UserExistsAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> CreateUserAsync(UserEntity user, CancellationToken cancellationToken = default);
    Task<int> GetUserCountAsync(CancellationToken cancellationToken = default);
    Task<UserEntity> GetUserByIdAsync(Ulid userId, CancellationToken cancellationToken = default);
}
