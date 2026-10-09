namespace GymQ.Services;

/// <summary>Real time plus a local testing offset; never changes the system clock.</summary>
public sealed class DemoClock : TimeProvider
{
    private readonly TimeProvider _source;
    private TimeSpan _offset;
    public DemoClock(TimeProvider? source = null) => _source = source ?? TimeProvider.System;
    public override DateTimeOffset GetUtcNow() => _source.GetUtcNow() + _offset;
    internal TimeSpan Offset => _offset;
    internal void RestoreOffset(TimeSpan offset) => _offset = offset;
    internal void Advance(TimeSpan amount) => _offset += amount;
}
