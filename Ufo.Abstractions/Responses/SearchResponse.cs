using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Abstractions.Responses;

public class SearchResponse
{
    public List<FileDto> Files { get; set; } = [];
    public List<FolderDto> Folders { get; set; } = [];
}
