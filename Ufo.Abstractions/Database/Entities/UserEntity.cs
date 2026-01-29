using SQLite;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("Users")]
public class UserEntity : EntityBase
{
    [JsonIgnore] // Never expose the hash in API responses
    [MaxLength(128)]
    public string PasswordHash { get; set; } = string.Empty;    
}
