using System.Collections.ObjectModel;
using GymQ.Desktop.Presentation;
using GymQ.Services;
using GymQ.Models;
namespace GymQ.Desktop.ViewModels;
public sealed class ProfileViewModel : PageViewModel
{
    // Account details for the signed-in member. Switching accounts is done by logging out.
    public string Name => Shell.Current.Name;
    public string UserName => Shell.Current.UserName;
    public string AccountRole => Shell.AccountRole;
    public ActionCommand LogOut => Shell.LogOut;
    public ActionCommand AdvanceOne { get; }
    public ActionCommand AdvanceTwo { get; }
    public ActionCommand AdvanceThirty { get; }
    public string DemoTime => $"Demo time advanced: {Shell.Gym.AdvancedBy.TotalMinutes:0} minutes";
    public override void Refresh() => Notify(nameof(DemoTime));
    public ProfileViewModel(ShellViewModel shell) : base(shell)
    {
        AdvanceOne = new(() => shell.Perform(() => shell.Gym.AdvanceDemoTime(1)));
        AdvanceTwo = new(() => shell.Perform(() => shell.Gym.AdvanceDemoTime(2)));
        AdvanceThirty = new(() => shell.Perform(() => shell.Gym.AdvanceDemoTime(30)));
    }
}

public sealed class StaffViewModel : PageViewModel
{
    // Second line of defence: the shell already routes non-staff away from this page,
    // and FaultReportService still checks IsStaff on every review.
    public bool IsStaff => Shell.IsStaff;
    public ActionCommand LogOut => Shell.LogOut;
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
