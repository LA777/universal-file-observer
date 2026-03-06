using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Responses;

public class SearchResponse
{
    public List<FsFileEntity> Files { get; set; } = [];
    public List<FsFolderEntity> Folders { get; set; } = [];
}
