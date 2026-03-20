using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests;

public class SearchRequest
{
    [MinLength(3)] // TODO LA - Add tests to ensure minimum length
    public string Query { get; set; } = string.Empty;
    public bool IncludeFolders { get; set; } = true;
    public bool IncludeFiles { get; set; } = true;
}
