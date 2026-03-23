using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Mappers
{ // TODO LA - Cover with Unit tests  
    public static class FileMapper
    {
        public static FileDto ToDto(this FsFileEntity entity)
        {
            if (entity == null) return null!;

            var dto = new FileDto
            {
                Id = entity.Id,
                UserId = entity.UserId,
                Name = entity.Name,
                Size = entity.Size,
                Sha256Hash = entity.Sha256Hash,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                FullPath = Path.Combine(entity.ParentFolders?.FirstOrDefault()?.Name ?? string.Empty, entity.Name + entity.FileExtension), // TODO LA - This is a simplification. The full path should be constructed by traversing all parent folders, not just the first one.
                HasParent = entity.ParentFolders?.Count > 0,
                IsHidden = entity.IsHidden,
                FileExtension = entity.FileExtension
            };

            // Convert snapshots to SnapshotSummaryDto list
            if (entity.Snapshots != null)
            {
                foreach (var snapshot in entity.Snapshots)
                {
                    dto.Snapshots.Add(snapshot.ToSummaryDto());
                }
            }

            return dto;
        }

        public static List<FileDto> ToDtoList(this List<FsFileEntity> entity) =>
            entity.Select(ToDto).ToList();

    }
}
