using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GymQ.Desktop.Presentation;
namespace GymQ.Desktop.ViewModels;
public sealed class QueueViewModel : PageViewModel
{
    public string EquipmentId { get; }
    public string Name => Shell.Gym.Equipment[EquipmentId].Name;
    public Bitmap Image => EquipmentImages.Get(EquipmentId);
    public string Position => Shell.Gym.Queue.GetQueuePosition(EquipmentId, Shell.Current.MemberId) is { } p ? p == 1 ? "You're next" : $"You're #{p} in line" : "You're no longer queued";
    public string CurrentUser => Shell.Gym.Sessions.ReadActiveSession(EquipmentId) is { } s ? Shell.Gym.MemberName(s.MemberId) : "No active session";
    public string Initial => CurrentUser[..1];
    public string CurrentDuration => Shell.Gym.Sessions.ReadActiveSession(EquipmentId) is { } s ? Time(DateTime.UtcNow - s.StartTime) : "00:00";
    public bool CanNudge => Shell.Gym.Queue.GetQueuePosition(EquipmentId, Shell.Current.MemberId) == 1 && Shell.Gym.Sessions.ReadActiveSession(EquipmentId) != null;
    public ObservableCollection<QueuePerson> People { get; } = new();
    public ActionCommand Nudge { get; }
    public ActionCommand Leave { get; }
    private string _feedback = "";
    public string Feedback { get => _feedback; set => Set(ref _feedback, value); }
    public QueueViewModel(ShellViewModel shell, string id) : base(shell)
    {
        EquipmentId = id;
        Nudge = new(() => shell.Perform(() => shell.Gym.SendNudge(id, shell.Current), () => Feedback = "Nudge sent. The current user has 60 seconds to respond."));
        Leave = new(() => shell.Perform(() => shell.Gym.Leave(id, shell.Current), () => shell.Navigate("equipment")));
        Refresh();
    }
    public override void Refresh()
    {
        People.Clear(); int i = 0;
        foreach (var entry in Shell.Gym.Queue.ReadQueue(EquipmentId)) People.Add(new(++i, entry.MemberId == Shell.Current.MemberId ? "You" : Shell.Gym.MemberName(entry.MemberId), entry.MemberId == Shell.Current.MemberId));
        Notify(nameof(Position)); Notify(nameof(CurrentUser)); Notify(nameof(Initial)); Notify(nameof(CanNudge)); Tick();
    }
    public override void Tick() => Notify(nameof(CurrentDuration));
}
public record QueuePerson(int Number, string Name, bool IsMe)
{
    public IBrush Background => new SolidColorBrush(Color.Parse(IsMe ? "#303135" : "#1A1B1E"));
    public string Subtitle => IsMe ? "Your place is saved" : "Waiting in queue";
}
