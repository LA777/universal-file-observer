using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Mappers;

public static class StorageDriveMapper
{
    public static StorageDriveDto ToDto(this StorageDriveEntity entity)
    {
        if (entity == null)
        {
            return null!;
        }
        return new StorageDriveDto
        {
            Id = entity.Id,
            DeviceId = entity.DeviceId,
            SerialNumber = entity.SerialNumber,
            TotalSize = entity.TotalSize,
            Description = entity.Description,
            MediaType = entity.MediaType,
            InterfaceType = entity.InterfaceType,
            Pcs = entity.Pcs.ToDtoList()
        };
    }
}
