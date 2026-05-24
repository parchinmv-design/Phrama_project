namespace PharmaOrderApp.Models;

public sealed class ActivityLogEntry
{
    public DateTime OccurredAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}
