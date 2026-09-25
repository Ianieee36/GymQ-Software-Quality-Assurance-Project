namespace GymQ.Services;

/// <summary>Real time plus a local testing offset; never changes the system clock.</summary>
public sealed class DemoClock : TimeProvider
{
    private TimeSpan _offset;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
    internal void Advance(TimeSpan amount) => _offset += amount;
}
