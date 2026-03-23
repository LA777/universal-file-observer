using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Responses;

namespace Ufo.Server.Mappers
{
    public static class SnapshotMapper
    {
        // TODO LA - Implement mapping logic here.
        // TODO LA - Cover with Unit tests.

        // TODO LA - implement a method to map SnapshotEntity to SnapshotSummaryDto
        public static SnapshotSummaryDto ToSummaryDto(this SnapshotEntity entity) =>
            new()
            {
                Id = entity.Id,
                Description = entity.Description,               
                Timestamp = entity.Timestamp,
                UserId = entity.UserId,
                Labels = entity.Labels.ToDtoList()
            };

        public static List<SnapshotSummaryDto> ToDtoList(this IList<SnapshotEntity> entities) =>
            entities.Select(ToSummaryDto).ToList();
    }
}
