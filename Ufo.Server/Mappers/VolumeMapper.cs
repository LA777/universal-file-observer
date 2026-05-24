using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Mappers;

public static class VolumeMapper
{
    public static VolumeDto ToDto(this VolumeEntity entity)
    {
        if (entity == null)
        {
            return null!;
        }

        return new VolumeDto
        {
            Id = entity.Id,
            DriveLetter = entity.DriveLetter,
            VolumeName = entity.VolumeName,
            Description = entity.Description,
            VolumeSerialNumber = entity.VolumeSerialNumber,
            VolumeSize = entity.VolumeSize,
            StorageDrive = entity.StorageDrive?.ToDto()
        };
    }
}