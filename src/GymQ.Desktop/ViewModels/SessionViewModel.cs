using Avalonia.Media.Imaging;
using GymQ.Desktop.Presentation;
using GymQ.Services;
namespace GymQ.Desktop.ViewModels;
public sealed class SessionViewModel : PageViewModel
{
    public string EquipmentId { get; }
    public string Name => Shell.Gym.Equipment[EquipmentId].Name;
    public Bitmap Image => EquipmentImages.Get(EquipmentId);

    // The session this page belongs to, pinned when the page opens. After it ends the page
    // keeps showing it, and never shows the next member's session (GQ-07).
    private readonly string? _sessionId;
    private SessionStatus Status => SessionStatus.For(Shell.Gym,
        _sessionId == null ? null : Shell.Gym.Sessions.ReadSession(_sessionId)); 
    private UsageSession? MySession =>
        Shell.Gym.Sessions.ReadActiveSession(EquipmentId, Shell.Current.MemberId);
    public bool IsActive => Status.IsActive;
    public string Title => IsActive ? $"Using {Name}" : Name;
    public string Headline => Status.Headline;
    public string Duration => Status.Duration;
    public string Remaining => Status.Detail;
    public ActionCommand End { get; }
    public ActionCommand Report { get; }
    public SessionViewModel(ShellViewModel shell, string id) : base(shell)
    {
        EquipmentId = id;
        _sessionId = shell.Gym.Sessions.ReadLatestSession(id, shell.Current.MemberId)?.SessionId;
        End = new(() => shell.Perform(() => shell.Gym.Finish(id, shell.Current), () => shell.Navigate("equipment")));
        Report = new(() => shell.Navigate("report", id));
    }
    public override void Tick() 
    { 
        Notify(nameof(IsActive)); Notify(nameof(Headline)); 
        Notify(nameof(Duration)); Notify(nameof(Remaining)); 
    }
    public override void Refresh() => Tick();
}
