using GymQ.Models;
using GymQ.Persistence;

namespace GymQ.Services;

public partial class UsageSession
{
    // Restoration preserves the recorded identity and timestamps without starting a new session.
    private UsageSession(SessionState state)
    {
        SessionId = state.SessionId;
        EquipmentId = state.EquipmentId;
        MemberId = state.MemberId;
        StartTime = state.StartTime;
        EndTime = state.EndTime;
        EndReason = state.EndReason;
    }

    internal static UsageSession FromState(SessionState state) => new(state);
}

public partial class SessionService
{
    internal SessionState[] ExportState()
    {
        lock (_lockObject)
            return _sessions.Select(s => new SessionState(
                s.SessionId, s.EquipmentId, s.MemberId, s.StartTime, s.EndTime, s.EndReason)).ToArray();
    }

    internal void RestoreState(IEnumerable<SessionState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var restored = states.Select(UsageSession.FromState).ToArray();
        lock (_lockObject)
        {
            _sessions.Clear();
            _sessions.AddRange(restored);
        }
    }

    // Startup reconciliation records the original deadline rather than the restart time.
    internal void EndRestoredSession(string equipmentId, DateTime endTime, SessionEndReason reason)
    {
        lock (_lockObject)
        {
            var equipment = _equipmentRepository.GetById(equipmentId)
                ?? throw new ArgumentException($"No equipment found with ID '{equipmentId}'.", nameof(equipmentId));
            var session = _sessions.FirstOrDefault(s => s.EquipmentId == equipmentId && s.EndTime == null)
                ?? throw new InvalidOperationException($"No active session found for equipment '{equipmentId}'.");

            session.MarkEnded(endTime, reason);
            if (equipment.Status != EquipmentStatus.Unavailable)
                equipment.Status = EquipmentStatus.Available;
        }
    }
}
