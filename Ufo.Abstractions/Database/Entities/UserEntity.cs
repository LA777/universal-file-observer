using SQLite;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("Users")]
public class UserEntity : EntityWithNameAndIdBase
{
    [JsonIgnore] // Never expose the hash in API responses
    [MaxLength(128)]
    public string PasswordHash { get; set; } = string.Empty;    

    public string CreatedAt { get; set; } = string.Empty;
}
