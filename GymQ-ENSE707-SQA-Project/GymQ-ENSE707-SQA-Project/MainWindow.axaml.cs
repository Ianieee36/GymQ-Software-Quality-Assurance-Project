using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using GymQ.Models;
using GymQ.FaultModule;
using System.Linq;

namespace GymQ_ENSE707_SQA_Project;

public partial class MainWindow : Window
{
    private readonly List<Equipment> _equipments = new();
    private readonly Dictionary<string, List<QueueEntry>> _localQueues = new();
    private readonly Member _currentMember;
    private readonly Member _staffMember;
    private readonly FaultReportService _faultService;
    private List<FaultReport> _lastPendingReports = new();

    public MainWindow()
    {
        InitializeComponent();

        // sample data (4 machines)
        _equipments.Add(new Equipment("E1", "Treadmill #1"));
        _equipments.Add(new Equipment("E2", "Preacher Curls #1"));
        _equipments.Add(new Equipment("E3", "Rowing Machine #1"));
        _equipments.Add(new Equipment("E4", "Smith Machine #1"));

        foreach (var e in _equipments)
        {
            _localQueues[e.EquipmentId] = new List<QueueEntry>();
        }

        // demo current member
        _currentMember = new Member("M1", "Demo User", false);

        // demo staff account
        _staffMember = new Member("S1", "Staff User", true);

        // fault service needs an equipment repository; provide a local adapter
        _faultService = new FaultReportService(new LocalEquipmentRepository(_equipments));

        // populate UI list
        foreach (var item in _equipments.Select(e => $"{e.EquipmentId} - {e.Name}"))
        {
            EquipmentListBox.Items.Add(item);
        }

        EquipmentListBox.SelectionChanged += EquipmentListBox_SelectionChanged;
        JoinQueueButton.Click += JoinQueueButton_Click;
        ReportButton.Click += ReportButton_Click;
        ToggleReportButton.Click += ToggleReportButton_Click;
        ToggleStaffButton.Click += ToggleStaffButton_Click;
        PendingReportsListBox.SelectionChanged += PendingReportsListBox_SelectionChanged;
        ConfirmReportButton.Click += ConfirmReportButton_Click;
        RejectReportButton.Click += RejectReportButton_Click;

        // select first by default
        if (_equipments.Count > 0)
        {
            EquipmentListBox.SelectedIndex = 0;
        }
    }

    private void EquipmentListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = EquipmentListBox.SelectedIndex;
        if (idx < 0 || idx >= _equipments.Count)
            return;

        var eq = _equipments[idx];
        NameText.Text = eq.Name;
        StatusText.Text = eq.Status.ToString();
        QueueCountText.Text = _localQueues[eq.EquipmentId].Count.ToString();
        MessageText.Text = string.Empty;
        ReportResultText.Text = string.Empty;

        // hide report panel when switching selection
        ReportPanel.IsVisible = false;
        ToggleReportButton.Content = "Report an Issue";
    }

    private void ToggleReportButton_Click(object? sender, RoutedEventArgs e)
    {
        var visible = ReportPanel.IsVisible;
        ReportPanel.IsVisible = !visible;
        ToggleReportButton.Content = ReportPanel.IsVisible ? "Cancel Report" : "Report an Issue";
        if (!ReportPanel.IsVisible)
        {
            ReportDescriptionBox.Text = string.Empty;
            ReportResultText.Text = string.Empty;
        }
    }

    private void ToggleStaffButton_Click(object? sender, RoutedEventArgs e)
    {
        var show = !StaffPanel.IsVisible;
        StaffPanel.IsVisible = show;

        // hide member controls when staff view is open
        JoinQueueButton.IsVisible = !show;
        ToggleReportButton.IsVisible = !show;
        ReportPanel.IsVisible = false;

        ToggleStaffButton.Content = show ? "Exit Staff View" : "Staff View";

        if (show)
        {
            RefreshPendingReports();
        }
        else
        {
            // clear staff selection
            PendingReportsListBox.SelectedIndex = -1;
            PendingReportDetailsText.Text = string.Empty;
        }
    }

    private void RefreshPendingReports()
    {
        _lastPendingReports = _faultService.GetPendingReports();

        // clear existing items
        while (PendingReportsListBox.Items.Count > 0)
            PendingReportsListBox.Items.RemoveAt(0);

        if (_lastPendingReports.Count == 0)
        {
            PendingReportsListBox.Items.Add("(no pending reports)");
            ConfirmReportButton.IsEnabled = false;
            RejectReportButton.IsEnabled = false;
            PendingReportDetailsText.Text = string.Empty;
            return;
        }

        foreach (var r in _lastPendingReports)
        {
            PendingReportsListBox.Items.Add($"{r.ReportId} - {r.EquipmentId} - {r.Description}");
        }

        ConfirmReportButton.IsEnabled = false;
        RejectReportButton.IsEnabled = false;
        PendingReportDetailsText.Text = string.Empty;
    }

    private void PendingReportsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = PendingReportsListBox.SelectedIndex;
        if (idx < 0 || idx >= _lastPendingReports.Count)
        {
            PendingReportDetailsText.Text = string.Empty;
            ConfirmReportButton.IsEnabled = false;
            RejectReportButton.IsEnabled = false;
            return;
        }

        var r = _lastPendingReports[idx];
        PendingReportDetailsText.Text = $"Report {r.ReportId}\nEquipment: {r.EquipmentId}\nSubmittedBy: {r.SubmittedByMemberId}\nWhen: {r.SubmittedAt}\n\n{r.Description}";
        ConfirmReportButton.IsEnabled = true;
        RejectReportButton.IsEnabled = true;
    }

    private void ConfirmReportButton_Click(object? sender, RoutedEventArgs e)
    {
        var idx = PendingReportsListBox.SelectedIndex;
        if (idx < 0 || idx >= _lastPendingReports.Count)
            return;

        var report = _lastPendingReports[idx];
        try
        {
            _faultService.ReviewFaultReport(report.ReportId, _staffMember, true);
            MessageText.Text = $"Report {report.ReportId} confirmed.";
        }
        catch (System.Exception ex)
        {
            MessageText.Text = ex.Message;
        }

        RefreshPendingReports();

        // if the confirmed report affects the currently selected equipment, update its status text
        var selIdx = EquipmentListBox.SelectedIndex;
        if (selIdx >= 0 && selIdx < _equipments.Count)
        {
            var eq = _equipments[selIdx];
            if (eq.EquipmentId == report.EquipmentId)
            {
                StatusText.Text = eq.Status.ToString();
            }
        }
    }

    private void RejectReportButton_Click(object? sender, RoutedEventArgs e)
    {
        var idx = PendingReportsListBox.SelectedIndex;
        if (idx < 0 || idx >= _lastPendingReports.Count)
            return;

        var report = _lastPendingReports[idx];
        try
        {
            _faultService.ReviewFaultReport(report.ReportId, _staffMember, false);
            MessageText.Text = $"Report {report.ReportId} rejected.";
        }
        catch (System.Exception ex)
        {
            MessageText.Text = ex.Message;
        }

        RefreshPendingReports();
    }

    private void JoinQueueButton_Click(object? sender, RoutedEventArgs e)
    {
        var idx = EquipmentListBox.SelectedIndex;
        if (idx < 0 || idx >= _equipments.Count)
        {
            MessageText.Text = "Please select a machine first.";
            return;
        }

        var eq = _equipments[idx];
        var queue = _localQueues[eq.EquipmentId];

        // prevent duplicate join for demo user
        if (queue.Any(q => q.MemberId == _currentMember.MemberId))
        {
            MessageText.Text = "You are already in the queue for this machine.";
            return;
        }

        var entry = new QueueEntry(eq.EquipmentId, _currentMember.MemberId);
        queue.Add(entry);
        QueueCountText.Text = queue.Count.ToString();
        MessageText.Text = $"Joined queue for {eq.Name}. Position: {queue.Count}";
    }

    private void ReportButton_Click(object? sender, RoutedEventArgs e)
    {
        var idx = EquipmentListBox.SelectedIndex;
        if (idx < 0 || idx >= _equipments.Count)
        {
            MessageText.Text = "Please select a machine first.";
            return;
        }

        var eq = _equipments[idx];
        var desc = ReportDescriptionBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(desc))
        {
            ReportResultText.Text = "Please enter a description for the report.";
            return;
        }

        try
        {
            var report = _faultService.SubmitFaultReport(eq.EquipmentId, _currentMember, desc);
            ReportResultText.Text = $"Report submitted: {report.ReportId}";
            ReportDescriptionBox.Text = string.Empty;
            MessageText.Text = $"Report {report.ReportId} submitted for {eq.Name}. Status: {report.Status}";
        }
        catch (System.Exception ex)
        {
            ReportResultText.Text = "Failed to submit report.";
            MessageText.Text = ex.Message;
        }
    }

    // Simple adapter to satisfy FaultReportService dependency
    private class LocalEquipmentRepository : IEquipmentRepository
    {
        private readonly List<Equipment> _list;
        public LocalEquipmentRepository(List<Equipment> list) => _list = list;
        public Equipment GetById(string equipmentId) => _list.FirstOrDefault(e => e.EquipmentId == equipmentId);
    }
}
