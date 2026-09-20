using Avalonia.Controls; using Avalonia.Threading; using GymQ.Desktop.ViewModels; using System.ComponentModel;
namespace GymQ_ENSE707_SQA_Project;
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    public ShellViewModel Model { get; }
    public MainWindow() : this(new ShellViewModel()) { }
    public MainWindow(ShellViewModel model)
    {
        InitializeComponent(); Model = model; DataContext = model;
        model.PropertyChanged += OnPageChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => model.Tick(); _timer.Start();
        Closed += (_, _) => { _timer.Stop(); model.PropertyChanged -= OnPageChanged; };
    }
    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(ShellViewModel.Page)) PageScroll.Offset = new Avalonia.Vector(0, 0); }
}
