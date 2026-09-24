using GymQ.Application;
using GymQ.Models;
using GymQ.Desktop.Presentation;
using Avalonia.Media;

namespace GymQ.Desktop.ViewModels;
public sealed class ShellViewModel : ObservableObject
{
    public GymSession Gym { get; }
    private Member _current;
    public Member Current { get => _current; set { if (Set(ref _current, value)) { Notify(nameof(MemberName)); Notify(nameof(Initials)); Notify(nameof(AccountRole)); Overlay = null; Error = ""; Navigate("equipment"); } } }
    public string MemberName => Current.Name;
    public string AccountRole => Current.IsStaff ? "BOTANY JUNCTION STAFF" : "BOTANY JUNCTION MEMBER";
    public string Initials => string.Concat(Current.Name.Split(' ').Select(s => s[0]));
    public List<Member> Accounts => Gym.Members;
    private PageViewModel _page = null!;
    public PageViewModel Page { get => _page; private set => Set(ref _page, value); }
    private OverlayViewModel? _overlay;
    public OverlayViewModel? Overlay { get => _overlay; set { if (Set(ref _overlay, value)) Notify(nameof(HasOverlay)); } }
    public bool HasOverlay => Overlay != null;
    private string _error = "";
    public string Error { get => _error; set { if (Set(ref _error, value)) Notify(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;
    public ActionCommand DismissError { get; }
    public ActionCommand Equipment { get; }
    public ActionCommand Profile { get; }
    public IBrush HomeColour => Colour("home");
    public IBrush ClubsColour => Colour("club");
    public IBrush EquipmentColour => Colour("equipment");
    public IBrush ProfileColour => Colour("profile");
    private string _tab = "equipment";
    private IBrush Colour(string name) => new SolidColorBrush(Avalonia.Media.Color.Parse(_tab == name ? "#EF234A" : "#98999E"));
    public ShellViewModel(GymSession? gym = null)
    {
        Gym = gym ?? new(); _current = Gym.Members[0];
        Equipment = new(() => Navigate("equipment")); Profile = new(() => Navigate("profile"));
        DismissError = new(() => Error = "");
        Gym.Changed += Refresh;
        Navigate("equipment");
    }
    public void Navigate(string page, string id = "E1")
    {
        Error = "";
        _tab = page is "home" or "club" or "profile" ? page : page == "staff" ? "profile" : "equipment";
        Page = page switch
        {
            "queue" => new QueueViewModel(this, id), "session" => new SessionViewModel(this, id),
            "report" => new ReportViewModel(this, id), "detail" => new DetailViewModel(this, id),
            "profile" => new ProfileViewModel(this), "staff" => new StaffViewModel(this),
            _ => new EquipmentViewModel(this)
        };
        Notify(nameof(HomeColour)); Notify(nameof(ClubsColour)); Notify(nameof(EquipmentColour)); Notify(nameof(ProfileColour));
        CheckNotices();
    }
    public void Perform(Action action, Action? after = null)
    {
        try { action(); Error = ""; after?.Invoke(); Refresh(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException) { Error = ex.Message; }
    }
    public void Refresh() { Page?.Refresh(); CheckNotices(); }
    public void Tick() { Gym.Tick(); Page?.Tick(); CheckNotices(); Overlay?.Tick(); }
    private void CheckNotices()
    {
        if (Overlay is SuccessOverlay) return;
        var nudge = Gym.Nudges.FirstOrDefault(n => n.MemberId == Current.MemberId);
        if (nudge != null)
        {
            if (Overlay is not NudgeOverlay n || n.EquipmentId != nudge.EquipmentId) Overlay = new NudgeOverlay(this, nudge);
            return;
        }
        var claim = Gym.Equipment.Values.SelectMany(e => Gym.Queue.ReadQueue(e.EquipmentId))
            .FirstOrDefault(q => q.MemberId == Current.MemberId && q.NotifiedAt.HasValue && Gym.Equipment[q.EquipmentId].Status == EquipmentStatus.Available);
        if (claim != null)
        {
            if (Overlay is not ClaimOverlay c || c.EquipmentId != claim.EquipmentId) Overlay = new ClaimOverlay(this, claim);
            return;
        }
        if (Overlay is ClaimOverlay or NudgeOverlay) Overlay = null;
    }
}
public abstract class PageViewModel(ShellViewModel shell) : ObservableObject
{
    protected ShellViewModel Shell { get; } = shell;
    public ActionCommand Back => new(() => Shell.Navigate("equipment"));
    public virtual void Refresh() { }
    public virtual void Tick() { }
    public static string Time(TimeSpan duration) { var total = Math.Max(0, (int)duration.TotalSeconds); return $"{total / 60:00}:{total % 60:00}"; }
}
