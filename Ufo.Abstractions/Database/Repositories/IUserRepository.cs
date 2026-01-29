using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface IUserRepository
{
    Task<UserEntity?> GetUserByUsernameAsync(string username);
    Task<bool> UserExistsAsync(string username);
    Task<bool> CreateUserAsync(UserEntity user);
    Task<int> GetUserCountAsync();
}
