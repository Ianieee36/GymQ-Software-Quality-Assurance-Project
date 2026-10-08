namespace GymQ.Services;
public partial class SessionService
{
    /// <summary>
    /// Any active session on this equipment, whoever owns it.
    /// Use for "is anyone using this machine?" (nudging, queue page, timeouts).
    /// </summary>
    public UsageSession? ReadActiveSession(string equipmentId)
    {
        lock (_lockObject)
            return _sessions.FirstOrDefault(s =>
                s.EquipmentId == equipmentId && s.EndTime == null);
    }

    /// <summary>
    /// The given member's own active session on this equipment, or null.
    /// Use for "is this MY session?" (session page, View Session, ending a session).
    /// </summary>
    public UsageSession? ReadActiveSession(string equipmentId, string memberId)
    { 
        lock (_lockObject) 
            return _sessions.FirstOrDefault(s => 
                s.EquipmentId == equipmentId && s.MemberId == memberId && s.EndTime == null); 
    }
    public IReadOnlyList<UsageSession> ReadSessions()
    { 
        lock (_lockObject) 
            return _sessions.ToArray(); 
    }
}
