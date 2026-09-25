using GymQ.Models;
using GymQ.Repository;

namespace GymQ.Services;

/// <summary>
/// Coordinates UI actions through the team's services and shared in-memory repository.
/// Call actions and Tick on the UI thread; this does not add concurrent queue access support.
/// The desktop shell must call Tick regularly to process timeouts.
/// </summary>
public sealed class GymSession
{
    public Dictionary<string, Equipment> Equipment { get; } = new()
    {
        ["E1"] = new("E1", "Treadmill #3"), ["E2"] = new("E2", "Squat Rack #2"),
        ["E3"] = new("E3", "Elliptical #1"), ["E4"] = new("E4", "Recumbent Bike")
    };
    public List<Member> Members { get; } = new()
    {
        new("M001","Lorenz_Soriano", "LS123", "Lorenz Soriano", false), 
        new("M002","Christian_Cantos", "CS123", "Christian Cantos", false),
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
    private readonly Dictionary<string, NudgeNotice> _nudges = new();
    public IReadOnlyCollection<NudgeNotice> Nudges => _nudges.Values;
    private readonly DemoClock _clock = new();
    public DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
    public TimeSpan AdvancedBy { get; private set; }
    public void AdvanceDemoTime(int minutes)
    {
        if (minutes is not (1 or 2 or 30)) throw new ArgumentOutOfRangeException(nameof(minutes));
        // Process in seconds so handovers and later deadlines occur in order.
        for (var second = 0; second < minutes * 60; second++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            Tick();
        }
        AdvancedBy += TimeSpan.FromMinutes(minutes);
        Changed?.Invoke();
    }
    public event Action? Changed;
    public string MemberName(string id) => Members.FirstOrDefault(m => m.MemberId == id)?.Name ?? id;

    // The seed parameter allows the desktop shell to start with a pre-populated session and fault report for demonstration purposes.
    public GymSession(bool seed = true)
    {
        _users = new UserService(new InMemoryUserRepository(Members));
        var repository = new InMemoryEquipmentRepository(Equipment);
        Sessions = new(repository, _clock);
        Queue = new(Sessions, _clock);
        Faults = new(repository, _clock);
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
    { Queue.LeaveQueue(equipmentId, member.MemberId); OfferNext(equipmentId); Changed?.Invoke(); }
    public void Claim(string equipmentId, Member member)
    {
        if (!Queue.ClaimEquipment(equipmentId, member.MemberId)) throw new InvalidOperationException("This turn has expired or is not yours.");
        Changed?.Invoke();
    }
    public void Finish(string equipmentId, Member member)
    {
        RequireCurrentUser(equipmentId, member);
        Sessions.EndSession(equipmentId, SessionEndReason.ManualFinish);
        _nudges.Remove(equipmentId); OfferNext(equipmentId); Changed?.Invoke();
    }
    public void SendNudge(string equipmentId, Member member)
    {
        var current = Sessions.ReadActiveSession(equipmentId) ?? throw new InvalidOperationException("There is no active user to nudge.");
        if (!Queue.SendNudge(equipmentId, member.MemberId)) throw new InvalidOperationException("Only the next member can nudge. Please wait 5 minutes between nudges on this machine.");
        _nudges[equipmentId] = new(equipmentId, current.MemberId, UtcNow.AddMinutes(1)); Changed?.Invoke();
    }
    public void Respond(string equipmentId, Member member, bool stillUsing)
    {
        RequireCurrentUser(equipmentId, member);
        if (!_nudges.ContainsKey(equipmentId)) throw new InvalidOperationException("This nudge has already ended.");
        Queue.HandleNudgeResponse(equipmentId, stillUsing);
        _nudges.Remove(equipmentId); Changed?.Invoke();
    }
    public void Report(string equipmentId, Member member, string description)
    {
        Reports.Add(Faults.SubmitFaultReport(equipmentId, member, description.Trim())); Changed?.Invoke();
    }
    public void Review(string reportId, Member staff, bool confirm)
    { Faults.ReviewFaultReport(reportId, staff, confirm); Changed?.Invoke(); }
    private void RequireCurrentUser(string id, Member member)
    { if (Sessions.ReadActiveSession(id)?.MemberId != member.MemberId) throw new UnauthorizedAccessException("Only the current equipment user can end this session."); }
    private void OfferNext(string equipmentId)
    { if (Equipment[equipmentId].Status == EquipmentStatus.Available) Queue.NotifyNextInQueue(equipmentId); }

    // This method is called by the desktop shell on a timer to process timeouts and expired nudges.
    public void Tick()
    {
        // Check for expired nudges and end sessions if the user did not respond in time.
        bool changed = false;
        foreach (var n in _nudges.Values.Where(n => n.ExpiresAt <= UtcNow).ToArray())
        {
            if (Sessions.ReadActiveSession(n.EquipmentId)?.MemberId == n.MemberId)
            { Sessions.EndSession(n.EquipmentId, SessionEndReason.NudgeTimeout); OfferNext(n.EquipmentId); }
            _nudges.Remove(n.EquipmentId); changed = true;
        }

        // Check for expired sessions and queue claims.
        foreach (var e in Equipment.Values)
        {
            var wasActive = Sessions.ReadActiveSession(e.EquipmentId) != null;
            Sessions.EnforceMaxSessionDuration(e.EquipmentId);
            if (wasActive && Sessions.ReadActiveSession(e.EquipmentId) == null)
            { _nudges.Remove(e.EquipmentId); OfferNext(e.EquipmentId); changed = true; }
            foreach (var entry in Queue.ReadQueue(e.EquipmentId).Where(q => q.NotifiedAt.HasValue))
            {
                Queue.EnforceClaimTimeout(e.EquipmentId, entry.MemberId);
                if (Queue.GetQueuePosition(e.EquipmentId, entry.MemberId) == null) changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

}

// data structure for the UI to display nudge notices and their expiration times.
public record NudgeNotice(string EquipmentId, string MemberId, DateTime ExpiresAt);
