namespace GymQ.Services;

/// <summary>
/// FR-003: a nudge asks the member using a machine whether they are still using it.
///
/// Design rules:
/// - Who may nudge is decided by the queue: only the member at the front of the queue
///   (QueueService.SendNudge also enforces the agreed GQ-04 per-equipment cooldown).
/// - What is nudged is one session, identified by SessionId, never "the machine".
/// - A nudge only stays open while both sides still exist: the target session is active
///   and the requester is still queued for that machine. Otherwise it is withdrawn.
/// </summary>
public sealed partial class NudgeService
{
    /// <summary>How long the target has to answer. FR-003 states 1 minute; the team currently uses 2.</summary>
    public static readonly TimeSpan ResponseWindow = TimeSpan.FromMinutes(2);
     /// <summary>How often the same nudger may nudge the same session.</summary>
    public static readonly TimeSpan CooldownWindow = TimeSpan.FromMinutes(5);

    private readonly SessionService _sessions;
    private readonly QueueService _queue;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, NudgeNotice> _open = new();   // keyed by SessionId
    // Last accepted nudge per (session, nudger). Rejected attempts never extend it.
    private readonly Dictionary<(string SessionId, string RequestedBy), DateTime> _lastNudgeAt = new();

    public NudgeService(SessionService sessions, QueueService queue, TimeProvider clock)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }


    public IReadOnlyCollection<NudgeNotice> Open => _open.Values;

    public NudgeNotice? ReadFor(string sessionId) => _open.GetValueOrDefault(sessionId);

    /// <summary>The open nudge the given member must answer on this machine, if any.</summary>
    public NudgeNotice? ReadForTarget(string equipmentId, string memberId) =>
        _open.Values.FirstOrDefault(n => n.EquipmentId == equipmentId && n.MemberId == memberId);

    /// <summary>Time left before this member may nudge this session again; zero when allowed.</summary>
    public TimeSpan CooldownRemaining(string sessionId, string requesterId)
    {
        if (!_lastNudgeAt.TryGetValue((sessionId, requesterId), out var last)) return TimeSpan.Zero;
        var remaining = last + CooldownWindow - _clock.GetUtcNow().UtcDateTime;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>Sends a nudge from the front-of-queue member to whoever is using the machine.</summary>
    public NudgeNotice Request(string equipmentId, string requesterId)
    {
        var session = _sessions.ReadActiveSession(equipmentId)
            ?? throw new InvalidOperationException("There is no active user to nudge.");
        if (session.MemberId == requesterId)
            throw new InvalidOperationException("You cannot nudge your own session.");
        if (_open.ContainsKey(session.SessionId))
            throw new InvalidOperationException("A nudge is already waiting for a response.");
        if (!_queue.SendNudge(equipmentId, requesterId))
            throw new InvalidOperationException("Only the next member in the queue can nudge.");
        var wait = CooldownRemaining(session.SessionId, requesterId);
        if (wait > TimeSpan.Zero)
            throw new InvalidOperationException(
                $"You can nudge this member again in {(int)wait.TotalMinutes:00}:{wait.Seconds:00}.");

        var now = _clock.GetUtcNow().UtcDateTime;
        _lastNudgeAt[(session.SessionId, requesterId)] = now;
        var nudge = new NudgeNotice(session.SessionId, equipmentId, session.MemberId, requesterId, now + ResponseWindow);
        _open[session.SessionId] = nudge;
        return nudge;
    }

    /// <summary>The target answers. Throws if there is nothing open for them on this machine.</summary>
    public void Respond(string equipmentId, string memberId, bool stillUsing)
    {
        var nudge = ReadForTarget(equipmentId, memberId)
            ?? throw new InvalidOperationException("This nudge has already ended.");
        _open.Remove(nudge.SessionId);
        _queue.HandleNudgeResponse(equipmentId, stillUsing);
    }

    /// <summary>
    /// Removes nudges that no longer have both sides: the session ended (any reason),
    /// or the requester left or lost their place in the queue. Returns true if any were removed.
    /// </summary>
    public bool WithdrawInvalid()
    {
        var invalid = _open.Values.Where(n =>
                _sessions.ReadActiveSession(n.EquipmentId)?.SessionId != n.SessionId ||
                _queue.GetQueuePosition(n.EquipmentId, n.RequestedBy) == null)
            .ToArray();
        foreach (var n in invalid) _open.Remove(n.SessionId);
        // Cooldowns only matter while their session is running.
        foreach (var key in _lastNudgeAt.Keys.Where(k => !IsActive(k.SessionId)).ToArray())
            _lastNudgeAt.Remove(key);
        return invalid.Length > 0;
    }

    private bool IsActive(string sessionId) =>
        _sessions.ReadSessions().Any(s => s.SessionId == sessionId && s.EndTime == null);

    /// <summary>
    /// Returns the nudges whose deadline has passed and removes them. Call WithdrawInvalid
    /// first so a withdrawn nudge can never end a session.
    /// </summary>
    public IReadOnlyList<NudgeNotice> TakeExpired()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var expired = _open.Values.Where(n => n.ExpiresAt <= now).ToArray();
        foreach (var n in expired) _open.Remove(n.SessionId);
        return expired;
    }
}

/// <summary>
/// An open nudge. MemberId is the member being nudged (kept under its old name so the
/// desktop UI and existing tests keep working); RequestedBy is the member who sent it.
/// </summary>
public record NudgeNotice(string SessionId, string EquipmentId, string MemberId, string RequestedBy, DateTime ExpiresAt);

/// <summary>When RequestedBy last nudged SessionId. Saved so the cooldown survives a restart.</summary>
public record NudgeCooldown(string SessionId, string RequestedBy, DateTime LastNudgeAt);