using System.Collections.ObjectModel;
using GymQ.Desktop.Presentation;
using GymQ.FaultModule;
using GymQ.Models;
namespace GymQ.Desktop.ViewModels;
public sealed class ProfileViewModel : PageViewModel
{
    public List<Member> Accounts => Shell.Accounts;
    public Member Current { get => Shell.Current; set => Shell.Current = value; }
    public bool IsStaff => Current.IsStaff;
    public ActionCommand Staff { get; }
    public ProfileViewModel(ShellViewModel shell) : base(shell)
    { Staff = new(() => shell.Navigate("staff")); }
}

public sealed class StaffViewModel : PageViewModel
{
    public bool IsStaff => Shell.Current.IsStaff;
    public ObservableCollection<StaffReport> Reports { get; } = new();
    public ObservableCollection<StaffEquipment> Equipment { get; } = new();
    public string PendingLabel => "Pending reports · " + Reports.Count;
    public bool NoReports => Reports.Count == 0;
    public StaffViewModel(ShellViewModel shell) : base(shell) => Refresh();
    public override void Refresh()
    {
        Reports.Clear(); Equipment.Clear(); if (!IsStaff) return;
        foreach (var r in Shell.Gym.Faults.GetPendingReports()) Reports.Add(new(Shell, r));
        foreach (var e in Shell.Gym.Equipment.Values) Equipment.Add(new(Shell, e));
        Notify(nameof(PendingLabel)); Notify(nameof(NoReports));
    }
}
public sealed class StaffReport
{
    public string Name { get; } public string Description { get; } public string Meta { get; }
    public ActionCommand Confirm { get; } public ActionCommand Reject { get; }
    public StaffReport(ShellViewModel shell, FaultReport r)
    {
        Name = shell.Gym.Equipment[r.EquipmentId].Name; Description = r.Description; Meta = r.ReportId + " · " + shell.Gym.MemberName(r.SubmittedByMemberId);
        Confirm = new(() => shell.Perform(() => shell.Gym.Review(r.ReportId, shell.Current, true)));
        Reject = new(() => shell.Perform(() => shell.Gym.Review(r.ReportId, shell.Current, false)));
    }
}
public sealed class StaffEquipment
{
    public EquipmentCardViewModel Card { get; }
    public StaffEquipment(ShellViewModel shell, Equipment e)
    { Card = new(shell, e); }
}
