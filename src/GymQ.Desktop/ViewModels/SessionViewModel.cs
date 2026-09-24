using Avalonia.Media.Imaging;
using GymQ.Desktop.Presentation;
namespace GymQ.Desktop.ViewModels;
public sealed class SessionViewModel : PageViewModel
{
    public string EquipmentId { get; }
    public string Name => Shell.Gym.Equipment[EquipmentId].Name;
    public Bitmap Image => EquipmentImages.Get(EquipmentId);
    public bool IsActive => Shell.Gym.Sessions.ReadActiveSession(EquipmentId)?.MemberId == Shell.Current.MemberId;
    public string Headline => IsActive ? "You're using" : "Session complete";
    public string Duration => Shell.Gym.Sessions.ReadActiveSession(EquipmentId) is { } s ? Time(Shell.Gym.UtcNow - s.StartTime) : "00:00";
    public string Remaining => Shell.Gym.Sessions.ReadActiveSession(EquipmentId) is { } s ? Time(s.StartTime.AddMinutes(30) - Shell.Gym.UtcNow) + " left of your 30-minute session" : "Your equipment is ready for the next member.";
    public ActionCommand End { get; }
    public ActionCommand Report { get; }
    public SessionViewModel(ShellViewModel shell, string id) : base(shell)
    {
        EquipmentId = id;
        End = new(() => shell.Perform(() => shell.Gym.Finish(id, shell.Current), () => shell.Navigate("equipment")));
        Report = new(() => shell.Navigate("report", id));
    }
    public override void Tick() { Notify(nameof(Duration)); Notify(nameof(Remaining)); }
    public override void Refresh() { Notify(nameof(IsActive)); Notify(nameof(Headline)); Tick(); }
}
