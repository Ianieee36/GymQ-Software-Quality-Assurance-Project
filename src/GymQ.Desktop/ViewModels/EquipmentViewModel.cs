using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GymQ.Models;
using GymQ.Desktop.Presentation;
namespace GymQ.Desktop.ViewModels;
public sealed class EquipmentViewModel : PageViewModel
{
    public ObservableCollection<EquipmentCardViewModel> Cards { get; } = new();
    public string Summary => $"{Shell.Gym.Equipment.Values.Count(e => e.Status == EquipmentStatus.Available)} available · {Shell.Gym.Equipment.Count} machines";
    public EquipmentViewModel(ShellViewModel shell) : base(shell) => Refresh();
    public override void Refresh() { Cards.Clear(); foreach (var e in Shell.Gym.Equipment.Values) Cards.Add(new(Shell, e)); Notify(nameof(Summary)); }
}
public sealed class EquipmentCardViewModel
{
    public string Id { get; }
    public string Name { get; }
    public Bitmap Image => EquipmentImages.Get(Id);
    public string Status { get; }
    public IBrush BadgeColour { get; }
    public string Detail { get; }
    public string ActionLabel { get; }
    public bool CanAct { get; }
    public ActionCommand Action { get; }
    public ActionCommand Open { get; }

    // The constructor takes a ShellViewModel and an Equipment object, and initializes the properties based on the equipment's status and the current user's position in the queue.
    public EquipmentCardViewModel(ShellViewModel shell, Equipment e)
    {
        Id = e.EquipmentId; Name = e.Name;
        Status = e.Status switch { EquipmentStatus.Available => "AVAILABLE", EquipmentStatus.InUse => "IN USE", _ => "OUT OF SERVICE" };
        BadgeColour = new SolidColorBrush(Color.Parse(e.Status switch { EquipmentStatus.Available => "#6DD49D", EquipmentStatus.InUse => "#FFB665", _ => "#B4B5B9" }));
        var position = shell.Gym.Queue.GetQueuePosition(Id, shell.Current.MemberId);
        bool own = shell.Gym.Sessions.ReadActiveSession(Id)?.MemberId == shell.Current.MemberId;
        var count = shell.Gym.Queue.ReadQueue(Id).Count;
        CanAct = e.Status != EquipmentStatus.Unavailable;
        Detail = e.Status == EquipmentStatus.Unavailable ? "Staff review completed" : own ? "Your session is active" : position.HasValue ? $"You're #{position} of {count}" : count > 0 ? $"{count} member{(count == 1 ? "" : "s")} waiting" : e.Status == EquipmentStatus.Available ? "Ready for your next set" : "Currently in a session";
        ActionLabel = own ? "View Session" : position.HasValue ? "View Queue" : e.Status == EquipmentStatus.Available && count == 0 ? "Start Session" : "Join Queue";
        Open = new(() => shell.Navigate("detail", Id));
        Action = new(() =>
        {
            if (own) shell.Navigate("session", Id);
            else if (position.HasValue) shell.Navigate("queue", Id);
            else if (e.Status == EquipmentStatus.Available && count == 0) shell.Perform(() => shell.Gym.Start(Id, shell.Current), () => shell.Navigate("session", Id));
            else shell.Perform(() => shell.Gym.Join(Id, shell.Current), () => shell.Navigate("queue", Id));
        });
    }
}

// This view model is used for the equipment detail page, which shows a single piece of equipment and allows the user to report a fault.
public sealed class DetailViewModel : PageViewModel
{
    public EquipmentCardViewModel Card { get; private set; }
    public ActionCommand Report { get; }
    private readonly string _id;
    public DetailViewModel(ShellViewModel shell, string id) : base(shell) { _id = id; Card = new(shell, shell.Gym.Equipment[id]); Report = new(() => shell.Navigate("report", id)); }
    public override void Refresh() { Card = new(Shell, Shell.Gym.Equipment[_id]); Notify(nameof(Card)); }
}
