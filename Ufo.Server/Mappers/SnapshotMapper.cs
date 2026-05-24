using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Mappers;

public static class SnapshotMapper
{
    // TODO LA - Cover with Unit tests.
    // TODO LA - consider moving to a dedicated Mapper
    public static SnapshotSummaryDto ToSummaryDto(this SnapshotEntity entity) =>
        new()
        {
            Id = entity.Id,
            Description = entity.Description,
            Timestamp = entity.Timestamp,
            Labels = entity.Labels.ToDtoList(),
            RootOnlyFolder = entity.RootFolder?.ToRootOnlyDto(),
            VolumeInfo = entity.VolumeInfo?.ToDto()
        };

    public static List<SnapshotSummaryDto> ToSummaryDtoList(this IList<SnapshotEntity> entities) =>
        entities.Select(ToSummaryDto).ToList();

    public static SnapshotDto ToDto(this SnapshotEntity entity) =>
        new()
        {
            Id = entity.Id,
            Description = entity.Description,
            Timestamp = entity.Timestamp,
            Labels = entity.Labels.ToDtoList(),
            RootFolder = entity.RootFolder?.ToDto(),
            VolumeInfo = entity.VolumeInfo?.ToDto()
        };

    public static List<SnapshotDto> ToDtoList(this IList<SnapshotEntity> entities) =>
        entities.Select(ToDto).ToList();
}
