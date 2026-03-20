using SQLite;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("Users")]
public class UserEntity : EntityBase
{
    [JsonPropertyOrder(1)]
    [NotNull]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [JsonIgnore] // Never expose the hash in API responses
    [MaxLength(128)]
    public string PasswordHash { get; set; } = string.Empty;    

    public string CreatedAt { get; set; } = string.Empty;
}
