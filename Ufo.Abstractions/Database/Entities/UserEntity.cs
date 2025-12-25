using SQLite;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("Users")]
public class UserEntity : EntityBase
{
    [JsonIgnore] // Never expose the hash in API responses
    public string PasswordHash { get; set; } = string.Empty;    
}
