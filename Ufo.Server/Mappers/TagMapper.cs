using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Mappers;

public static class TagMapper
{
    public static TagDto ToDto(this TagEntity entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            ColorHex = entity.ColorHex
        };

    public static List<TagDto> ToDtos(this IEnumerable<TagEntity> entities) =>
        entities.Select(ToDto).ToList();
}
