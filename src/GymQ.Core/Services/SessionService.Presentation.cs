namespace GymQ.Services;
public partial class SessionService
{
    public UsageSession? ReadActiveSession(string equipmentId)
    { lock (_lockObject) return _sessions.FirstOrDefault(s => s.EquipmentId == equipmentId && s.EndTime == null); }
    public IReadOnlyList<UsageSession> ReadSessions()
    { lock (_lockObject) return _sessions.ToArray(); }
}
