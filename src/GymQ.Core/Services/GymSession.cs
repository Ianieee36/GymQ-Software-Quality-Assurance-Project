using GymQ.Models;
using GymQ.Repository;

namespace GymQ.Services;

/// <summary>
/// Coordinates UI actions through the team's services and shared in-memory repository.
/// Call actions and Tick on the UI thread; this does not add concurrent queue access support.
/// The desktop shell must call Tick regularly to process timeouts.
/// </summary>
public sealed partial class GymSession
{
    public Dictionary<string, Equipment> Equipment { get; } = new()
    {
        ["E1"] = new("E1", "Treadmill #3"), ["E2"] = new("E2", "Squat Rack #2"),
        ["E3"] = new("E3", "Elliptical #1"), ["E4"] = new("E4", "Recumbent Bike")
    };
    public List<Member> Members { get; } = new()
    {
        new("M001","Lorenz_Soriano", "LS123", "Lorenz Soriano", false), 
        new("M002","Christian_Cantos", "CC123", "Christian Cantos", false),
        new("M003","Jayden_Marsh", "JM123", "Jayden Marsh", false), 
        new("S001","Gym_Staff", "GS123", "Gym Staff", true)
    };

    // Credential checks for the login screen. Built from Members in the constructor.
    private readonly IUserService _users;

    /// <summary>Returns the matching account, or null if the username or password is wrong.</summary>
    public Member? Login(string userName, string password) => _users.Login(userName, password);

    public Member FindMember(string memberId) =>
        Members.FirstOrDefault(m => m.MemberId == memberId)
        ?? throw new ArgumentException($"No member found with ID '{memberId}'.", nameof(memberId));

    // Services are initialized with the shared in-memory repository.

    public QueueService Queue { get; }
    public SessionService Sessions { get; }
    public FaultReportService Faults { get; }
    public List<FaultReport> Reports { get; } = new();
    public NudgeService Nudging { get; }
    /// <summary>Open nudges. Kept for the desktop UI and existing tests.</summary>
    public IReadOnlyCollection<NudgeNotice> Nudges => Nudging.Open;
    private readonly List<QueueCancellationNotice> _queueCancellations = new();
    public QueueCancellationNotice? ReadQueueCancellation(string memberId) =>
        _queueCancellations.FirstOrDefault(n => n.MemberId == memberId);
    public void AcknowledgeQueueCancellation(QueueCancellationNotice notice)
    {
        if (_queueCancellations.Remove(notice)) Changed?.Invoke();
    }
    private readonly DemoClock _clock;
    public DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
    public TimeSpan AdvancedBy { get; private set; }
    public void AdvanceDemoTime(int minutes)
    {
        if (minutes is not (1 or 2 or 30)) throw new ArgumentOutOfRangeException(nameof(minutes));
        // Keep timeout processing unchanged, but persist one complete demo-time advance.
        _suppressAutoSave = true;
        try
        {
            for (var second = 0; second < minutes * 60; second++)
            {
                _clock.Advance(TimeSpan.FromSeconds(1));
                Tick();
            }
            AdvancedBy += TimeSpan.FromMinutes(minutes);
        }
        finally { _suppressAutoSave = false; }
        Changed?.Invoke();
    }
    public event Action? Changed;
    public string MemberName(string id) => Members.FirstOrDefault(m => m.MemberId == id)?.Name ?? id;

    // The seed parameter allows the desktop shell to start with a pre-populated session and fault report for demonstration purposes.
    public GymSession(bool seed = true, TimeProvider? clock = null)
    {
        _clock = new(clock);
        _users = new UserService(new InMemoryUserRepository(Members));
        var repository = new InMemoryEquipmentRepository(Equipment);
        Sessions = new(repository, _clock);
        Queue = new(Sessions, _clock);
        Faults = new(repository, _clock);
        Nudging = new(Sessions, Queue, _clock);
        if (seed)
        {
            Sessions.StartSession("E2", "M002");
            // Look accounts up by ID so reordering Members cannot break the seed.
            var report = Faults.SubmitFaultReport("E3", FindMember("M003"), "Resistance mechanism needs inspection.");
            Faults.ReviewFaultReport(report.ReportId, FindMember("S001"), true);
            Reports.Add(report);
        }
    }
    public void Start(string equipmentId, Member member)
    {
        // Don't expose the direct-start shortcut while a queued member has a reserved turn.
        if (Queue.ReadQueue(equipmentId).Count > 0) throw new InvalidOperationException("This machine is reserved for the queue. Join the queue to take a turn.");
        Sessions.StartSession(equipmentId, member.MemberId); Changed?.Invoke();
    }
    public void Join(string equipmentId, Member member)
    {
        if (Equipment[equipmentId].Status == EquipmentStatus.Unavailable) throw new InvalidOperationException("This machine is out of service.");
        Queue.JoinQueue(equipmentId, member);
        OfferNext(equipmentId); Changed?.Invoke();
    }

    public void Leave(string equipmentId, Member member)
    {
        Queue.LeaveQueue(equipmentId, member.MemberId);
        Nudging.WithdrawInvalid();   // a nudge this member sent is withdrawn with them
        OfferNext(equipmentId); Changed?.Invoke();
    }

    public void Claim(string equipmentId, Member member)
    {
        if (!Queue.ClaimEquipment(equipmentId, member.MemberId)) throw new InvalidOperationException("This turn has expired or is not yours.");
        Nudging.WithdrawInvalid();
        Changed?.Invoke();
    }

    public void Finish(string equipmentId, Member member)
    {
        RequireCurrentUser(equipmentId, member);
        Sessions.EndSession(equipmentId, SessionEndReason.ManualFinish);
        Nudging.WithdrawInvalid();   // the session has ended, so its nudge has too
        OfferNext(equipmentId); Changed?.Invoke();
    }

    public void SendNudge(string equipmentId, Member member)
    {
        Nudging.Request(equipmentId, member.MemberId);
        Changed?.Invoke();
    }

    public void Respond(string equipmentId, Member member, bool stillUsing)
    {
        RequireCurrentUser(equipmentId, member);
        Nudging.Respond(equipmentId, member.MemberId, stillUsing);
        Changed?.Invoke();
    }

    public void Report(string equipmentId, Member member, string description)
    {
        Reports.Add(Faults.SubmitFaultReport(equipmentId, member, description.Trim())); Changed?.Invoke();
    }

    public void Review(string reportId, Member staff, bool confirm)
    {
        var equipmentId = Faults.GetPendingReports().FirstOrDefault(r => r.ReportId == reportId)?.EquipmentId;
        Faults.ReviewFaultReport(reportId, staff, confirm);
        if (confirm && equipmentId != null)
        {
            foreach (var memberId in Queue.CancelQueue(equipmentId))
                _queueCancellations.Add(new(memberId, equipmentId, Equipment[equipmentId].Name));
            
            Nudging.WithdrawInvalid(); // nobody is waiting any more, so the nudge is withdrawn
        }
        Changed?.Invoke();
    }

    private void RequireCurrentUser(string id, Member member)
    { 
        if (Sessions.ReadActiveSession(id, member.MemberId) == null) 
            throw new UnauthorizedAccessException("Only the current equipment user can end this session."); 
    }

    private void OfferNext(string equipmentId)
    { 
        if (Equipment[equipmentId].Status == EquipmentStatus.Available) 
            Queue.NotifyNextInQueue(equipmentId); 
    }

    // This method is called by the desktop shell on a timer to process timeouts and expired nudges.
    public void Tick()
    {
        // Withdraw first, so a nudge whose requester left can never end a session.
        bool changed = Nudging.WithdrawInvalid();
        foreach (var n in Nudging.TakeExpired())
        {
            // Every remaining nudge belongs to the machine's active session (checked above).
            Sessions.EndSession(n.EquipmentId, SessionEndReason.NudgeTimeout);
            OfferNext(n.EquipmentId); 
            changed = true;
        }

        // Check for expired sessions and queue claims.
        foreach (var e in Equipment.Values)
        {
            var wasActive = Sessions.ReadActiveSession(e.EquipmentId) != null;
            Sessions.EnforceMaxSessionDuration(e.EquipmentId);
            if (wasActive && Sessions.ReadActiveSession(e.EquipmentId) == null)
            { 
                OfferNext(e.EquipmentId); 
                changed = true; 
            }

            foreach (var entry in Queue.ReadQueue(e.EquipmentId).Where(q => q.NotifiedAt.HasValue))
            {
                Queue.EnforceClaimTimeout(e.EquipmentId, entry.MemberId);
                if (Queue.GetQueuePosition(e.EquipmentId, entry.MemberId) == null) changed = true;
            }
        }
        if (Nudging.WithdrawInvalid()) changed = true;   // sessions ended by the time cap
        if (changed) Changed?.Invoke();
    }

}

public record QueueCancellationNotice(string MemberId, string EquipmentId, string EquipmentName)
{
    public string Message => $"The queue for {EquipmentName} has been cancelled because the equipment has been marked Out of Service.";
}
