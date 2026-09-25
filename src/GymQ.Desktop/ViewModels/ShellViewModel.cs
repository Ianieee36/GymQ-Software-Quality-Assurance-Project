using GymQ.Services;
using GymQ.Models;
using GymQ.Desktop.Presentation;
using Avalonia.Media;

namespace GymQ.Desktop.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    public GymSession Gym { get; }

    // =============================================================
    // SIGNED-IN ACCOUNT
    // null means nobody is logged in and only the login page is shown.
    // =============================================================

    private Member? _current;

    /// <summary>
    /// The signed-in account. Pages other than Login only exist while someone is
    /// logged in, so they can use this safely.
    /// </summary>
    public Member Current => _current ?? throw new InvalidOperationException("No account is logged in.");

    public bool IsLoggedIn => _current != null;
    public bool IsStaff => _current?.IsStaff == true;
    public bool IsMember => _current != null && !_current.IsStaff;

    // The bottom navigation (Equipment / Profile) belongs to the member dashboard only.
    public bool ShowMemberNav => IsMember;

    public string MemberName => _current?.Name ?? "";
    public string AccountRole => _current == null ? "" : _current.IsStaff ? "BOTANY JUNCTION STAFF" : "BOTANY JUNCTION MEMBER";
    public string Initials => _current == null ? "" :
        string.Concat(_current.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s[0]));

    // =============================================================
    // PAGE, OVERLAY AND ERROR STATE
    // =============================================================

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
    public ActionCommand LogOut { get; }

    public IBrush HomeColour => Colour("home");
    public IBrush ClubsColour => Colour("club");
    public IBrush EquipmentColour => Colour("equipment");
    public IBrush ProfileColour => Colour("profile");
    private string _tab = "equipment";
    private IBrush Colour(string name) => new SolidColorBrush(Avalonia.Media.Color.Parse(_tab == name ? "#EF234A" : "#98999E"));

    public ShellViewModel(GymSession? gym = null)
    {
        Gym = gym ?? new();
        Equipment = new(() => Navigate("equipment"));
        Profile = new(() => Navigate("profile"));
        LogOut = new(SignOut);
        DismissError = new(() => Error = "");
        Gym.Changed += Refresh;
        Navigate("login");
    }

    // =============================================================
    // LOGIN / LOGOUT
    // =============================================================

    /// <summary>
    /// Called by LoginViewModel after GymSession.Login has verified the credentials.
    /// Members land on the equipment dashboard; staff land on the staff dashboard.
    /// </summary>
    public void SignIn(Member account)
    {
        _current = account ?? throw new ArgumentNullException(nameof(account));
        Overlay = null;
        Error = "";
        NotifyAccountChanged();
        Navigate(account.IsStaff ? "staff" : "equipment");
    }

    public void SignOut()
    {
        _current = null;
        Overlay = null;
        Error = "";
        NotifyAccountChanged();
        Navigate("login");
    }

    private void NotifyAccountChanged()
    {
        Notify(nameof(IsLoggedIn)); Notify(nameof(IsStaff)); Notify(nameof(IsMember)); Notify(nameof(ShowMemberNav));
        Notify(nameof(MemberName)); Notify(nameof(Initials)); Notify(nameof(AccountRole));
    }

    // =============================================================
    // NAVIGATION WITH ROLE GUARD
    // Every navigation request passes through ResolvePage, so a button, command
    // or test cannot open a page that the signed-in role is not allowed to see.
    // =============================================================

    private string ResolvePage(string requested)
    {
        if (_current == null) return "login";                        // logged out: login only
        if (_current.IsStaff) return "staff";                          // staff: staff dashboard only
        return requested is "staff" or "login" ? "equipment" : requested; // members: never staff
    }

    public void Navigate(string page, string id = "E1")
    {
        Error = "";
        page = ResolvePage(page);
        _tab = page is "home" or "club" or "profile" ? page : "equipment";
        Page = page switch
        {
            "login" => new LoginViewModel(this),
            "staff" => new StaffViewModel(this),
            "queue" => new QueueViewModel(this, id), "session" => new SessionViewModel(this, id),
            "report" => new ReportViewModel(this, id), "detail" => new DetailViewModel(this, id),
            "profile" => new ProfileViewModel(this),
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

    // Claim and nudge pop-ups are member-only: staff never queue or use equipment.
    private void CheckNotices()
    {
        if (Overlay is SuccessOverlay) return;
        if (!IsMember)
        {
            if (Overlay is ClaimOverlay or NudgeOverlay) Overlay = null;
            return;
        }
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
