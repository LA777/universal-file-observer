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

    /// <summary>
    /// Grants access to server-scoped settings - currently the TLS certificate
    /// on the Settings page. Set on the first account to register, because that
    /// is the person standing up the installation; every later account is a
    /// plain user unless promoted directly in the database.
    /// </summary>
    /// <remarks>
    /// Carried in the JWT as a claim, but never trusted from there for a write:
    /// the token outlives a demotion by up to its seven-day expiry, so
    /// authorisation for server-scoped changes is re-read from this column.
    /// </remarks>
    public bool IsAdmin { get; set; }
}
