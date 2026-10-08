using Avalonia.Media.Imaging;
using GymQ.Desktop.Presentation;
using GymQ.Services;
namespace GymQ.Desktop.ViewModels;
public sealed class SessionViewModel : PageViewModel
{
    public string EquipmentId { get; }
    public string Name => Shell.Gym.Equipment[EquipmentId].Name;
    public Bitmap Image => EquipmentImages.Get(EquipmentId);
    private UsageSession? MySession =>
        Shell.Gym.Sessions.ReadActiveSession(EquipmentId, Shell.Current.MemberId);
    public bool IsActive => MySession != null;
    public string Headline => IsActive ? "You're using" : "Session complete";
    public string Duration => MySession is { } s ? Time(Shell.Gym.UtcNow - s.StartTime) : "00:00";
    public string Remaining => MySession is { } s 
        ? Time(s.StartTime.AddMinutes(30) - Shell.Gym.UtcNow) + " left of your 30-minute session" 
        : "Your equipment is ready for the next member.";
    public ActionCommand End { get; }
    public ActionCommand Report { get; }
    public SessionViewModel(ShellViewModel shell, string id) : base(shell)
    {
        EquipmentId = id;
        End = new(() => shell.Perform(() => shell.Gym.Finish(id, shell.Current), () => shell.Navigate("equipment")));
        Report = new(() => shell.Navigate("report", id));
    }
    public override void Tick() { Notify(nameof(IsActive)); Notify(nameof(Headline)); Notify(nameof(Duration)); Notify(nameof(Remaining)); }
    public override void Refresh() => Tick();
}
