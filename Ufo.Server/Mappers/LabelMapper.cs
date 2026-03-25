using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Mappers;

public static class LabelMapper
{ // TODO LA - Cover with Unit tests
    public static LabelDto ToDto(this LabelEntity entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            ColorHex = entity.ColorHex,
            UserId = entity.UserId,
            SnapshotIds = entity.Snapshots.Select(s => s.Id).ToList() // TODO LA - Cover this line with Tests and update existent tests
        };

    public static List<LabelDto> ToDtoList(this List<LabelEntity> entities) =>
        entities.Select(ToDto).ToList();
}
