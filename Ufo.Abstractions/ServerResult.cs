namespace Ufo.Abstractions;

public class ServerResult
{
    public string ActionName { get; set; } = string.Empty;

    public Result Result { get; set; }

    public ActionPriority Priority { get; set; }

    public string Message { get; set; } = string.Empty;
}


public enum Result
{
    Success,
    NotFound,
    Error
}

public enum ActionPriority
{
    Lowest,
    Optional,
    Highest
}