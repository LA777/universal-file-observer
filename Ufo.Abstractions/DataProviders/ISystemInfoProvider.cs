using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.DataProviders;

public interface ISystemInfoProvider
{
    SnapshotEntity GetSystemInformation(string path, UserEntity user);
}
