using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Mappers;

public static class VolumeInfoMapper
{
    public static VolumeInfoDto ToDto(this VolumeInfoEntity entity)
    {
        if (entity == null)
        {
            return null!;
        }

        return new VolumeInfoDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            FreeSpace = entity.FreeSpace,
            DriveStatus = entity.DriveStatus,
            Volume = entity.Volume?.ToDto()
        };
    }
}
