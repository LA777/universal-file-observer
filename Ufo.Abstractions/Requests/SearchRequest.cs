using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests;

public record SearchRequest
{
    private string _query = string.Empty;

    [MinLength(3)] // TODO LA - Add tests to ensure minimum length
    public string Query
    {
        get => _query;
        set => _query = value?.Trim() ?? string.Empty;
    }

    public bool IncludeFolders { get; set; } = true;

    public bool IncludeFiles { get; set; } = true;
}
