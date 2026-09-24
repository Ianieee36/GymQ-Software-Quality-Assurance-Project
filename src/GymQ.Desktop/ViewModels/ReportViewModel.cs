using Avalonia.Media.Imaging;
using GymQ.Models;
using GymQ.Desktop.Presentation;
namespace GymQ.Desktop.ViewModels;
public sealed class ReportViewModel : PageViewModel
{
    public List<Equipment> Machines => Shell.Gym.Equipment.Values.ToList();
    private Equipment _selected;
    public Equipment Selected { get => _selected; set { if (Set(ref _selected, value)) Notify(nameof(Image)); } }
    public Bitmap Image => EquipmentImages.Get(Selected.EquipmentId);
    private string _description = "";
    public string Description { get => _description; set => Set(ref _description, value); }
    public ActionCommand Submit { get; }
    public ReportViewModel(ShellViewModel shell, string id) : base(shell)
    {
        _selected = shell.Gym.Equipment[id];
        Submit = new(() => shell.Perform(() => shell.Gym.Report(Selected.EquipmentId, shell.Current, Description), () =>
        { shell.Overlay = new SuccessOverlay(shell); shell.Navigate("equipment"); }));
    }
}
