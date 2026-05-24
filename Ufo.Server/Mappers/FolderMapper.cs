using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Mappers;
// TODO LA - Cover with Unit tests
public static class FolderMapper
{
    public static FolderDto ToDto(this FolderEntity entity)
    {
        if (entity == null)
        {
            return null!;
        }

        var dto = new FolderDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Size = entity.Size,
            Sha256Hash = entity.Sha256Hash,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            FullPath = Path.Combine(entity.ParentFolders?.FirstOrDefault()?.Name ?? string.Empty, entity.Name), // TODO LA - This is a simplification. The full path should be constructed by traversing all parent folders, not just the first one.
            HasParent = entity.ParentFolders?.Count > 0,
            IsHidden = entity.IsHidden
        };

        foreach (var folder in entity.ChildFolders) // TODO LA - Check this is tests
        {
            dto.ChildFolders.Add(folder.ToDto());
        }

        foreach (var file in entity.Files) // TODO LA - Check this is tests
        {
            dto.Files.Add(file.ToDto());
        }

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

    public static List<FolderDto> ToDtoList(this IList<FolderEntity> entity) =>
        entity.Select(ToDto).ToList();


    public static FolderDto ToRootOnlyDto(this FolderEntity entity)
    {
        if (entity == null)
        {
            return null!;
        }

        var dto = new FolderDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Size = entity.Size,
            Sha256Hash = entity.Sha256Hash,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            FullPath = Path.Combine(entity.ParentFolders?.FirstOrDefault()?.Name ?? string.Empty, entity.Name), // TODO LA - This is a simplification. The full path should be constructed by traversing all parent folders, not just the first one.
            HasParent = entity.ParentFolders?.Count > 0,
            IsHidden = entity.IsHidden
        };

        return dto;
    }

}
