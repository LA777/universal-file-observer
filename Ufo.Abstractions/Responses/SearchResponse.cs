using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Responses;

public class SearchResponse
{
    public List<FsFileEntity> Files { get; set; } = new List<FsFileEntity>();
    public List<FsFolderEntity> Folders { get; set; } = new List<FsFolderEntity>();
}
