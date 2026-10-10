using GymQ.Models;
using GymQ.Persistence;
using System.Text.Json;

namespace GymQ.Services;

public sealed partial class GymSession
{
    private IGymStateStore? _stateStore;
    private bool _suppressAutoSave;
    private string _persistenceError = "";

    public string PersistenceError => _persistenceError;
    public event Action? PersistenceChanged;

    /// <summary>
    /// Opens the desktop's durable state. Normal constructors remain in-memory only.
    /// Invalid saved data is never replaced with seeds; the caller must handle load failure.
    /// </summary>
    public static GymSession OpenPersistent(IGymStateStore store, bool seedWhenMissing = true, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var state = store.Load();
        var gym = new GymSession(seed: state == null && seedWhenMissing, clock: clock);
        if (state != null)
        {
            GymStateValidator.Validate(state, gym.Members.Select(m => m.MemberId));
            if (state.ClockOffset > DateTimeOffset.MaxValue - gym._clock.GetUtcNow())
                throw new InvalidDataException("The saved demo clock offset is outside the supported date range.");
            gym.RestoreState(state);
            gym.ReconcileRestoredDeadlines();
        }
        gym._stateStore = store;
        gym.Changed += gym.AutoSave;
        gym.SaveState();
        return gym;
    }

    /// <summary>A detached snapshot of business state, without accounts or UI state.</summary>
    public GymStateSnapshot CaptureState() => new()
    {
        SchemaVersion = GymStateSnapshot.CurrentSchemaVersion,
        SavedAtUtc = UtcNow,
        ClockOffset = _clock.Offset,
        AdvancedBy = AdvancedBy,
        Equipment = Equipment.Values.Select(e => new Equipment(e.EquipmentId, e.Name) { Status = e.Status }).ToArray(),
        Sessions = Sessions.ExportState(),
        Reports = Faults.ExportState(),
        NextReportNumber = Faults.NextReportNumber,
        Queue = Queue.ExportQueueState(),
        NudgeCooldowns = Nudging.ExportCooldowns(),
        Nudges = Nudging.ExportState(),
        Cancellations = _queueCancellations.ToArray()
    };

    /// <summary>False means persistence is disabled or the latest state was not saved.</summary>
    public bool SaveState()
    {
        if (_stateStore == null) return false;
        try
        {
            var state = CaptureState();
            GymStateValidator.Validate(state, Members.Select(m => m.MemberId));
            _stateStore.Save(state);
            SetPersistenceError("");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            SetPersistenceError("Your latest changes could not be saved. They remain in this app until it closes. " + ex.Message);
            return false;
        }
    }

    private void AutoSave()
    {
        if (!_suppressAutoSave) SaveState();
    }

    private void SetPersistenceError(string message)
    {
        if (_persistenceError == message) return;
        _persistenceError = message;
        PersistenceChanged?.Invoke();
    }

    private void RestoreState(GymStateSnapshot state)
    {
        _clock.RestoreOffset(state.ClockOffset);
        AdvancedBy = state.AdvancedBy;
        Equipment.Clear();
        foreach (var e in state.Equipment)
            Equipment.Add(e.EquipmentId, new(e.EquipmentId, e.Name) { Status = e.Status });
        Sessions.RestoreState(state.Sessions);
        Faults.RestoreState(state.Reports, state.NextReportNumber);
        Reports.Clear();
        Reports.AddRange(Faults.ReadAllReports());
        Queue.RestoreState(state.Queue);
        // Cooldowns remain after a response even when no popup is pending.
        Nudging.RestoreState(state.Nudges, state.NudgeCooldowns);
        _queueCancellations.AddRange(state.Cancellations);
    }

    // Reconcile downtime before any screen can show an expired reservation or session.
    // Offline expiry records its original deadline; unnotified members get a new offer now.
    private void ReconcileRestoredDeadlines()
    {
        var now = UtcNow;
        // Same rule as Tick: a nudge whose requester left can never time a session out.
        Nudging.WithdrawInvalid();
        foreach (var session in Sessions.ReadSessions().Where(s => s.EndTime == null))
        {
            var deadline = session.StartTime.AddMinutes(30);
            var reason = SessionEndReason.MaxDurationReached;
            if (Nudging.ReadFor(session.SessionId) is { } nudge && nudge.ExpiresAt <= deadline)
            {
                deadline = nudge.ExpiresAt;
                reason = SessionEndReason.NudgeTimeout;
            }
            if (deadline > now) continue;
            Sessions.EndRestoredSession(session.EquipmentId, deadline, reason);
        }
        foreach (var e in Equipment.Values.Where(e => e.Status == EquipmentStatus.Available))
        {
            var front = Queue.ReadQueue(e.EquipmentId).FirstOrDefault();
            if (front?.NotifiedAt is { } notified && notified.AddMinutes(2) <= now)
                Queue.LeaveQueue(e.EquipmentId, front.MemberId);
            OfferNext(e.EquipmentId);
        }
        Nudging.WithdrawInvalid(); // nudges of sessions that ended while the app was closed
    }
}
