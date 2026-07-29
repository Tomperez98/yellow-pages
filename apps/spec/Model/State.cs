using Microsoft.Accordant;

namespace Spec.Model;

public enum TimerStatus
{
    Active,
    Completed,
}

[State]
public partial class TimerItem : State
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public DateTime Deadline { get; set; }
    public TimerStatus Status { get; set; } = TimerStatus.Active;
}

[State]
public partial class TimerState : State
{
    public List<TimerItem> Items { get; set; } = [];
}
