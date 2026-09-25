using Avalonia.Media.Imaging;
using GymQ.Services;
using GymQ.Models;
using GymQ.Desktop.Presentation;
namespace GymQ.Desktop.ViewModels;
public abstract class OverlayViewModel : ObservableObject { public virtual void Tick() { } }
public sealed class ClaimOverlay : OverlayViewModel
{
    public string EquipmentId { get; }
    public string Name { get; }
    public Bitmap Image => EquipmentImages.Get(EquipmentId);
    private readonly GymSession _gym;
    private readonly DateTime _expiry;
    public string Countdown => "Claim expires in " + PageViewModel.Time(_expiry - _gym.UtcNow);
    public ActionCommand Claim { get; }
    public ActionCommand Leave { get; }
    public ClaimOverlay(ShellViewModel shell, QueueEntry entry)
    {
        _gym = shell.Gym;
        EquipmentId = entry.EquipmentId; Name = shell.Gym.Equipment[EquipmentId].Name; _expiry = entry.NotifiedAt!.Value.AddMinutes(2);
        Claim = new(() => shell.Perform(() => shell.Gym.Claim(EquipmentId, shell.Current), () => { shell.Overlay = null; shell.Navigate("session", EquipmentId); }));
        Leave = new(() => shell.Perform(() => shell.Gym.Leave(EquipmentId, shell.Current), () => shell.Overlay = null));
    }
    public override void Tick() => Notify(nameof(Countdown));
}
public sealed class NudgeOverlay : OverlayViewModel
{
    public string EquipmentId { get; }
    public string Name { get; }
    public Bitmap Image => EquipmentImages.Get(EquipmentId);
    private readonly GymSession _gym;
    private readonly DateTime _expiry;
    public string Countdown => "Please respond within " + PageViewModel.Time(_expiry - _gym.UtcNow) + ".";
    public ActionCommand StillUsing { get; }
    public ActionCommand Finish { get; }
    public NudgeOverlay(ShellViewModel shell, NudgeNotice notice)
    {
        _gym = shell.Gym;
        EquipmentId = notice.EquipmentId; Name = shell.Gym.Equipment[EquipmentId].Name; _expiry = notice.ExpiresAt;
        StillUsing = new(() => shell.Perform(() => shell.Gym.Respond(EquipmentId, shell.Current, true), () => shell.Overlay = null));
        Finish = new(() => shell.Perform(() => shell.Gym.Respond(EquipmentId, shell.Current, false), () => { shell.Overlay = null; shell.Navigate("equipment"); }));
    }
    public override void Tick() => Notify(nameof(Countdown));
}
public sealed class SuccessOverlay : OverlayViewModel
{
    public ActionCommand Done { get; }
    public SuccessOverlay(ShellViewModel shell) => Done = new(() => { shell.Overlay = null; shell.Refresh(); });
}
