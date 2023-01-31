namespace TimeSync.Data;

public sealed class TimeLogEntry
{
    public DateOnly Date { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public string Service { get; set; } = null!;
    public string Description { get; set; } = null!;
}