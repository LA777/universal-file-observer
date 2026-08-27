using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Mappers;

public static class PcMapper
{
    public static PcDto ToDto(this PcEntity entity)
    {
        if (entity == null)
        {
            return null!;
        }
        return new PcDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Name = entity.Name,
            MachineId = entity.MachineId,
            HardwareUuid = entity.HardwareUuid,
            HardwareSerialNumber = entity.HardwareSerialNumber
        };
    }

    public static List<PcDto> ToDtoList(this IList<PcEntity> entities) =>
        [.. entities.Select(e => e.ToDto())];
}
