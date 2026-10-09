namespace GymQ.Services;

public sealed partial class NudgeService
{
    internal NudgeNotice[] ExportState() => _open.Values.ToArray();

    internal NudgeCooldown[] ExportCooldowns() =>
        _lastNudgeAt.Select(c => new NudgeCooldown(c.Key.SessionId, c.Key.RequestedBy, c.Value)).ToArray();

    /// <summary>
    /// Restores saved nudges by SessionId. The caller must call WithdrawInvalid afterwards,
    /// so a nudge whose session ended or whose requester left is not kept.
    /// </summary>
    internal void RestoreState(IEnumerable<NudgeNotice> nudges, IEnumerable<NudgeCooldown> cooldowns)
    {
        ArgumentNullException.ThrowIfNull(nudges);
        ArgumentNullException.ThrowIfNull(cooldowns);
        var restored = nudges.ToArray();
        var restoredCooldowns = cooldowns.ToArray();
        _open.Clear();
        foreach (var n in restored) _open.Add(n.SessionId, n);
        _lastNudgeAt.Clear();
        foreach (var c in restoredCooldowns) _lastNudgeAt.Add((c.SessionId, c.RequestedBy), c.LastNudgeAt);
    }
}