using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

using GymQ.Models;
using GymQ.QueueModule;
using GymQ.SessionModule;
using GymQ.FaultModule;

using System;
using System.Collections.Generic;
using System.Linq;

namespace GymQ_ENSE707_SQA_Project;

public partial class MainWindow : Window
{
    // =============================================================
    // DATA / SERVICES
    // =============================================================

    private readonly Dictionary<string, Equipment> _equipmentStore = new();

    private readonly Member _currentMember;
    private readonly Member _staffMember;

    private readonly SessionService _sessionService;
    private readonly QueueService _queueService;
    private readonly FaultReportService _faultService;

    private List<FaultReport> _lastPendingReports = new();

    private Equipment? _selectedEquipment;

    // Tracks the session shown in the UI.
    // ClaimEquipment starts the session internally, so the UI keeps its own
    // start timestamp for the live timer.
    private string? _activeEquipmentId;
    private DateTime? _activeSessionStartedAt;

    // Claim countdown shown in the modal.
    private string? _claimEquipmentId;
    private DateTime? _claimExpiresAt;

    // Simple UI refresh timer.
    private readonly DispatcherTimer _uiTimer;

    // =============================================================
    // COLOURS
    // =============================================================

    private static readonly IBrush GreenBrush =
        new SolidColorBrush(Color.Parse("#37D67A"));

    private static readonly IBrush OrangeBrush =
        new SolidColorBrush(Color.Parse("#FF9F43"));

    private static readonly IBrush RedBrush =
        new SolidColorBrush(Color.Parse("#EF4765"));

    private static readonly IBrush WhiteBrush =
        new SolidColorBrush(Color.Parse("#FFFFFF"));

    private static readonly IBrush MutedBrush =
        new SolidColorBrush(Color.Parse("#A0A6AF"));

    // =============================================================
    // CONSTRUCTOR
    // =============================================================

    public MainWindow()
    {
        InitializeComponent();

        // ---------------------------------------------------------
        // Demo accounts
        // ---------------------------------------------------------

        _currentMember = new Member(
            "M001",
            "Lorenz Soriano",
            isStaff: false);

        _staffMember = new Member(
            "S001",
            "Gym Staff",
            isStaff: true);

        // ---------------------------------------------------------
        // Equipment
        // ---------------------------------------------------------

        AddEquipment(new Equipment("E1", "Treadmill #1"));
        AddEquipment(new Equipment("E2", "Preacher Curls #1"));
        AddEquipment(new Equipment("E3", "Rowing Machine #1"));
        AddEquipment(new Equipment("E4", "Smith Machine #1"));

        // ---------------------------------------------------------
        // Services
        // ---------------------------------------------------------

        _sessionService = new SessionService(_equipmentStore);

        // Uses your updated QueueService integration constructor.
        _queueService = new QueueService(_sessionService);

        _faultService =
            new FaultReportService(
                new LocalEquipmentRepository(_equipmentStore));

        // ---------------------------------------------------------
        // Demo state
        //
        // E2 begins InUse so the queue/nudge/claim flow can actually
        // be demonstrated without manually creating another user.
        // ---------------------------------------------------------

        try
        {
            _sessionService.StartSession("E2", "M900");
        }
        catch
        {
            // Ignore demo-state setup failure.
        }

        // ---------------------------------------------------------
        // Button events
        // ---------------------------------------------------------

        OpenStaffButton.Click += OpenStaffButton_Click;
        ExitStaffButton.Click += ExitStaffButton_Click;

        DetailBackButton.Click += DetailBackButton_Click;
        QueueBackButton.Click += QueueBackButton_Click;

        StartSessionButton.Click += StartSessionButton_Click;
        JoinQueueButton.Click += JoinQueueButton_Click;
        ViewQueueButton.Click += ViewQueueButton_Click;

        NudgeButton.Click += NudgeButton_Click;

        EndSessionButton.Click += EndSessionButton_Click;

        OpenReportButton.Click += OpenReportButton_Click;
        SessionReportButton.Click += OpenReportButton_Click;
        ReportBackButton.Click += ReportBackButton_Click;
        SubmitReportButton.Click += SubmitReportButton_Click;
        ReportDoneButton.Click += ReportDoneButton_Click;

        ClaimMachineButton.Click += ClaimMachineButton_Click;

        StillUsingButton.Click += StillUsingButton_Click;
        FinishFromNudgeButton.Click += FinishFromNudgeButton_Click;

        PendingReportsListBox.SelectionChanged +=
            PendingReportsListBox_SelectionChanged;

        ConfirmReportButton.Click += ConfirmReportButton_Click;
        RejectReportButton.Click += RejectReportButton_Click;

        HomeNavButton.Click += PrototypeNavigation_Click;
        ClubsNavButton.Click += PrototypeNavigation_Click;
        ProfileNavButton.Click += PrototypeNavigation_Click;

        EquipmentNavButton.Click += EquipmentNavButton_Click;

        // ---------------------------------------------------------
        // Timer
        // ---------------------------------------------------------

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        // ---------------------------------------------------------
        // Initial screen
        // ---------------------------------------------------------

        RefreshEquipmentCards();
        ShowDashboard();
    }

    // =============================================================
    // EQUIPMENT SETUP
    // =============================================================

    private void AddEquipment(Equipment equipment)
    {
        _equipmentStore[equipment.EquipmentId] = equipment;
    }

    // =============================================================
    // SCREEN NAVIGATION
    // =============================================================

    private void HideAllScreens()
    {
        DashboardView.IsVisible = false;
        EquipmentDetailView.IsVisible = false;
        QueueView.IsVisible = false;
        ActiveSessionView.IsVisible = false;
        ReportView.IsVisible = false;
        ReportSuccessView.IsVisible = false;
        StaffView.IsVisible = false;
    }

    private void ShowDashboard()
    {
        HideAllScreens();

        DashboardView.IsVisible = true;
        BottomNavigation.IsVisible = true;

        ClaimOverlay.IsVisible = false;
        NudgeOverlay.IsVisible = false;

        RefreshEquipmentCards();
    }

    private void ShowEquipmentDetails(Equipment equipment)
    {
        _selectedEquipment = equipment;

        HideAllScreens();

        EquipmentDetailView.IsVisible = true;
        BottomNavigation.IsVisible = true;

        RefreshEquipmentDetails();
    }

    private void ShowQueueView()
    {
        if (_selectedEquipment == null)
            return;

        HideAllScreens();

        QueueView.IsVisible = true;
        BottomNavigation.IsVisible = true;

        RefreshQueueView();
    }

    private void ShowActiveSession()
    {
        if (_activeEquipmentId == null)
            return;

        HideAllScreens();

        ActiveSessionView.IsVisible = true;
        BottomNavigation.IsVisible = true;

        if (_equipmentStore.TryGetValue(
                _activeEquipmentId,
                out var equipment))
        {
            ActiveSessionHeaderText.Text =
                $"Using {equipment.Name}";

            ActiveEquipmentNameText.Text =
                equipment.Name;
        }

        RefreshSessionDuration();
    }

    private void ShowReportView()
    {
        if (_selectedEquipment == null)
            return;

        HideAllScreens();

        ReportView.IsVisible = true;
        BottomNavigation.IsVisible = true;

        ReportEquipmentNameText.Text =
            _selectedEquipment.Name;

        ReportDescriptionBox.Text = string.Empty;
    }

    private void ShowReportSuccess()
    {
        HideAllScreens();

        ReportSuccessView.IsVisible = true;
        BottomNavigation.IsVisible = true;
    }

    private void ShowStaffView()
    {
        HideAllScreens();

        StaffView.IsVisible = true;
        BottomNavigation.IsVisible = false;

        RefreshStaffEquipmentStatus();
        RefreshPendingReports();
    }

    // =============================================================
    // EQUIPMENT DASHBOARD
    // =============================================================

    private void RefreshEquipmentCards()
    {
        EquipmentCardsPanel.Children.Clear();

        foreach (var equipment in _equipmentStore.Values)
        {
            var card = CreateEquipmentCard(equipment);
            EquipmentCardsPanel.Children.Add(card);
        }
    }

    private Control CreateEquipmentCard(Equipment equipment)
    {
        var statusText = new TextBlock
        {
            Text = GetStatusDisplayName(equipment.Status),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = GetStatusBrush(equipment.Status)
        };

        var nameText = new TextBlock
        {
            Text = equipment.Name,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = WhiteBrush
        };

        var idText = new TextBlock
        {
            Text = equipment.EquipmentId,
            FontSize = 11,
            Foreground = MutedBrush
        };

        var information = new StackPanel
        {
            Spacing = 3
        };

        information.Children.Add(idText);
        information.Children.Add(nameText);
        information.Children.Add(statusText);

        var button = new Button
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment =
                Avalonia.Layout.HorizontalAlignment.Left,

            Background =
                new SolidColorBrush(Color.Parse("#15181D")),

            BorderBrush =
                new SolidColorBrush(Color.Parse("#2B3038")),

            BorderThickness = new Thickness(1),

            Padding = new Thickness(16, 13),

            Content = information
        };

        button.Click += (_, _) =>
        {
            ShowEquipmentDetails(equipment);
        };

        return button;
    }

    // =============================================================
    // EQUIPMENT DETAILS
    // =============================================================

    private void RefreshEquipmentDetails()
    {
        if (_selectedEquipment == null)
            return;

        var equipment = _selectedEquipment;

        DetailEquipmentNameText.Text =
            equipment.Name;

        DetailEquipmentIdText.Text =
            equipment.EquipmentId;

        DetailStatusText.Text =
            GetStatusDisplayName(equipment.Status);

        DetailStatusText.Foreground =
            GetStatusBrush(equipment.Status);

        var position =
            _queueService.GetQueuePosition(
                equipment.EquipmentId,
                _currentMember.MemberId);

        DetailQueuePositionText.Text =
            position.HasValue
                ? $"#{position.Value}"
                : "Not queued";

        bool currentSession =
            _activeEquipmentId == equipment.EquipmentId;

        DetailSessionText.Text =
            currentSession
                ? "Active"
                : "No active session";

        // Available = user can immediately start.
        StartSessionButton.IsVisible =
            equipment.Status == EquipmentStatus.Available &&
            !position.HasValue &&
            !currentSession;

        // InUse = user can wait in queue.
        JoinQueueButton.IsVisible =
            equipment.Status == EquipmentStatus.InUse &&
            !position.HasValue &&
            !currentSession;

        // Existing queue membership.
        ViewQueueButton.IsVisible =
            position.HasValue &&
            !currentSession;

        // Unavailable equipment cannot be started or queued.
        if (equipment.Status == EquipmentStatus.Unavailable)
        {
            StartSessionButton.IsVisible = false;
            JoinQueueButton.IsVisible = false;
            ViewQueueButton.IsVisible = false;
        }
    }

    // =============================================================
    // START SESSION
    // =============================================================

    private void StartSessionButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_selectedEquipment == null)
            return;

        try
        {
            var session =
                _sessionService.StartSession(
                    _selectedEquipment.EquipmentId,
                    _currentMember.MemberId);

            _activeEquipmentId =
                _selectedEquipment.EquipmentId;

            _activeSessionStartedAt =
                session.StartTime;

            ShowMessage(
                $"Session started on {_selectedEquipment.Name}.");

            RefreshEquipmentCards();

            ShowActiveSession();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
            RefreshEquipmentDetails();
        }
    }

    // =============================================================
    // JOIN QUEUE — FR-001
    // =============================================================

    private void JoinQueueButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_selectedEquipment == null)
            return;

        if (_selectedEquipment.Status ==
            EquipmentStatus.Unavailable)
        {
            ShowMessage(
                "This equipment is currently unavailable.");

            return;
        }

        try
        {
            var position =
                _queueService.JoinQueue(
                    _selectedEquipment.EquipmentId,
                    _currentMember);

            ShowMessage(
                $"Joined {_selectedEquipment.Name}. " +
                $"Queue position: #{position}.");

            RefreshEquipmentDetails();
            ShowQueueView();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
    }

    private void ViewQueueButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        ShowQueueView();
    }

    // =============================================================
    // QUEUE POSITION
    // =============================================================

    private void RefreshQueueView()
    {
        if (_selectedEquipment == null)
            return;

        var equipment = _selectedEquipment;

        QueueEquipmentNameText.Text =
            $"Waiting for {equipment.Name}";

        var position =
            _queueService.GetQueuePosition(
                equipment.EquipmentId,
                _currentMember.MemberId);

        if (!position.HasValue)
        {
            QueuePositionHeadlineText.Text =
                "Not in queue";

            QueuePositionSubText.Text =
                "You are no longer waiting.";

            NudgeButton.IsEnabled = false;
        }
        else
        {
            QueuePositionHeadlineText.Text =
                position.Value == 1
                    ? "You're next"
                    : $"You're #{position.Value}";

            QueuePositionSubText.Text =
                $"Current queue position: {position.Value}";

            // Only front-of-queue member may nudge.
            NudgeButton.IsEnabled =
                position.Value == 1 &&
                equipment.Status ==
                EquipmentStatus.InUse;
        }

        QueueEquipmentStatusText.Text =
            GetStatusDisplayName(equipment.Status);

        QueueEquipmentStatusText.Foreground =
            GetStatusBrush(equipment.Status);
    }

    // =============================================================
    // NUDGE — FR-003
    // =============================================================

    private void NudgeButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_selectedEquipment == null)
            return;

        var sent =
            _queueService.SendNudge(
                _selectedEquipment.EquipmentId,
                _currentMember.MemberId);

        if (!sent)
        {
            ShowMessage(
                "Nudge could not be sent. " +
                "You must be next in queue and the " +
                "5-minute cooldown must have expired.");

            return;
        }

        NudgeEquipmentNameText.Text =
            $"Still using {_selectedEquipment.Name}?";

        NudgeOverlay.IsVisible = true;

        ShowMessage(
            "Nudge sent to the current equipment user.");
    }

    private void StillUsingButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_selectedEquipment == null)
            return;

        _queueService.HandleNudgeResponse(
            _selectedEquipment.EquipmentId,
            stillUsing: true);

        NudgeOverlay.IsVisible = false;

        ShowMessage(
            "Current member confirmed they are still using the equipment.");

        RefreshQueueView();
    }

    private void FinishFromNudgeButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_selectedEquipment == null)
            return;

        var equipmentId =
            _selectedEquipment.EquipmentId;

        try
        {
            /*
             * Your updated QueueService should already call
             * SessionService.EndSession() when stillUsing == false.
             */
            _queueService.HandleNudgeResponse(
                equipmentId,
                stillUsing: false);

            /*
             * Defensive fallback:
             * if the current QueueService version has not yet been
             * integrated, end the session here.
             */
            if (_selectedEquipment.Status ==
                EquipmentStatus.InUse)
            {
                _sessionService.EndSession(
                    equipmentId,
                    SessionEndReason.NudgeResponse);
            }

            /*
             * NotifyNextInQueue is idempotent because it only sets
             * NotifiedAt if it is currently null.
             */
            _queueService.NotifyNextInQueue(equipmentId);

            NudgeOverlay.IsVisible = false;

            RefreshEquipmentCards();
            RefreshQueueView();

            // Current demo user should now be first and notified.
            var position =
                _queueService.GetQueuePosition(
                    equipmentId,
                    _currentMember.MemberId);

            if (position == 1)
            {
                BeginClaimWindow(
                    _selectedEquipment);
            }
        }
        catch (Exception ex)
        {
            NudgeOverlay.IsVisible = false;
            ShowMessage(ex.Message);
        }
    }

    // =============================================================
    // CLAIM — FR-004 SUCCESS PATH
    // =============================================================

    private void BeginClaimWindow(
        Equipment equipment)
    {
        _claimEquipmentId =
            equipment.EquipmentId;

        _claimExpiresAt =
            DateTime.UtcNow.AddMinutes(2);

        ClaimEquipmentNameText.Text =
            $"{equipment.Name} is ready";

        ClaimCountdownText.Text =
            "Claim expires in 02:00";

        ClaimOverlay.IsVisible = true;
    }

    private void ClaimMachineButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_claimEquipmentId == null)
            return;

        try
        {
            var success =
                _queueService.ClaimEquipment(
                    _claimEquipmentId,
                    _currentMember.MemberId);

            if (!success)
            {
                ShowMessage(
                    "The equipment could not be claimed. " +
                    "Your turn may have expired.");

                ClaimOverlay.IsVisible = false;
                return;
            }

            _activeEquipmentId =
                _claimEquipmentId;

            _activeSessionStartedAt =
                DateTime.UtcNow;

            _claimEquipmentId = null;
            _claimExpiresAt = null;

            ClaimOverlay.IsVisible = false;

            RefreshEquipmentCards();

            ShowMessage(
                "Machine claimed. Your session has started.");

            ShowActiveSession();
        }
        catch (Exception ex)
        {
            ClaimOverlay.IsVisible = false;
            ShowMessage(ex.Message);
        }
    }

    // =============================================================
    // CLAIM TIMEOUT — FR-004 FAILURE PATH
    // =============================================================

    private void CheckClaimTimeout()
    {
        if (_claimEquipmentId == null ||
            !_claimExpiresAt.HasValue)
        {
            return;
        }

        var remaining =
            _claimExpiresAt.Value -
            DateTime.UtcNow;

        if (remaining > TimeSpan.Zero)
        {
            ClaimCountdownText.Text =
                $"Claim expires in " +
                $"{remaining.Minutes:00}:" +
                $"{remaining.Seconds:00}";

            return;
        }

        try
        {
            _queueService.EnforceClaimTimeout(
                _claimEquipmentId,
                _currentMember.MemberId);
        }
        catch
        {
            // Keep UI stable for prototype.
        }

        ClaimOverlay.IsVisible = false;

        _claimEquipmentId = null;
        _claimExpiresAt = null;

        ShowMessage(
            "Your 2-minute claim window expired.");

        RefreshEquipmentCards();

        if (_selectedEquipment != null)
        {
            ShowEquipmentDetails(_selectedEquipment);
        }
        else
        {
            ShowDashboard();
        }
    }

    // =============================================================
    // END SESSION
    // =============================================================

    private void EndSessionButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_activeEquipmentId == null)
            return;

        var equipmentId =
            _activeEquipmentId;

        try
        {
            _sessionService.EndSession(
                equipmentId,
                SessionEndReason.ManualFinish);

            // Equipment is now available. Notify queue head.
            _queueService.NotifyNextInQueue(
                equipmentId);

            _activeEquipmentId = null;
            _activeSessionStartedAt = null;

            ShowMessage(
                "Session ended.");

            RefreshEquipmentCards();

            if (_equipmentStore.TryGetValue(
                    equipmentId,
                    out var equipment))
            {
                ShowEquipmentDetails(equipment);
            }
            else
            {
                ShowDashboard();
            }
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
    }

    // =============================================================
    // LIVE SESSION TIMER / 30 MINUTE ENFORCEMENT
    // =============================================================

    private void RefreshSessionDuration()
    {
        if (_activeSessionStartedAt == null)
        {
            SessionDurationText.Text =
                "Session duration: 00:00";

            return;
        }

        var duration =
            DateTime.UtcNow -
            _activeSessionStartedAt.Value;

        SessionDurationText.Text =
            $"Session duration: " +
            $"{(int)duration.TotalMinutes:00}:" +
            $"{duration.Seconds:00}";
    }

    private void CheckMaxSessionDuration()
    {
        if (_activeEquipmentId == null)
            return;

        try
        {
            _sessionService.EnforceMaxSessionDuration(
                _activeEquipmentId);

            if (_equipmentStore.TryGetValue(
                    _activeEquipmentId,
                    out var equipment) &&
                equipment.Status != EquipmentStatus.InUse)
            {
                _activeEquipmentId = null;
                _activeSessionStartedAt = null;

                RefreshEquipmentCards();

                ShowMessage(
                    "Maximum 30-minute session duration reached.");

                ShowDashboard();
            }
        }
        catch
        {
            // Prototype UI should remain responsive.
        }
    }

    // =============================================================
    // FAULT REPORTING — FR-005
    // =============================================================

    private void OpenReportButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        /*
         * If user enters report from ActiveSessionView,
         * update selected equipment from active equipment.
         */
        if (_activeEquipmentId != null &&
            _equipmentStore.TryGetValue(
                _activeEquipmentId,
                out var activeEquipment))
        {
            _selectedEquipment = activeEquipment;
        }

        if (_selectedEquipment == null)
        {
            ShowMessage(
                "Please select equipment first.");

            return;
        }

        ShowReportView();
    }

    private void SubmitReportButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_selectedEquipment == null)
            return;

        var description =
            ReportDescriptionBox.Text?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(description))
        {
            ShowMessage(
                "Please describe the equipment problem.");

            return;
        }

        try
        {
            var report =
                _faultService.SubmitFaultReport(
                    _selectedEquipment.EquipmentId,
                    _currentMember,
                    description);

            ShowMessage(
                $"Report {report.ReportId} submitted.");

            ReportDescriptionBox.Text =
                string.Empty;

            ShowReportSuccess();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
    }

    // =============================================================
    // STAFF REPORT REVIEW — FR-006 / FR-007
    // =============================================================

    private void RefreshPendingReports()
    {
        _lastPendingReports =
            _faultService.GetPendingReports();

        PendingReportsListBox.SelectedIndex =
            -1;

        PendingReportDetailsText.Text =
            string.Empty;

        ConfirmReportButton.IsEnabled =
            false;

        RejectReportButton.IsEnabled =
            false;

        if (_lastPendingReports.Count == 0)
        {
            PendingReportsListBox.ItemsSource =
                new List<string>
                {
                    "No pending reports"
                };

            return;
        }

        PendingReportsListBox.ItemsSource =
            _lastPendingReports
                .Select(r =>
                    $"{r.ReportId}  •  " +
                    $"{r.EquipmentId}\n" +
                    $"{r.Description}")
                .ToList();
    }

    private void PendingReportsListBox_SelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        var index =
            PendingReportsListBox.SelectedIndex;

        if (index < 0 ||
            index >= _lastPendingReports.Count)
        {
            PendingReportDetailsText.Text =
                string.Empty;

            ConfirmReportButton.IsEnabled =
                false;

            RejectReportButton.IsEnabled =
                false;

            return;
        }

        var report =
            _lastPendingReports[index];

        PendingReportDetailsText.Text =
            $"Report: {report.ReportId}\n" +
            $"Equipment: {report.EquipmentId}\n" +
            $"Submitted by: {report.SubmittedByMemberId}\n" +
            $"Submitted: {report.SubmittedAt:g}\n\n" +
            $"{report.Description}";

        ConfirmReportButton.IsEnabled =
            true;

        RejectReportButton.IsEnabled =
            true;
    }

    private void ConfirmReportButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        ReviewSelectedReport(confirm: true);
    }

    private void RejectReportButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        ReviewSelectedReport(confirm: false);
    }

    private void ReviewSelectedReport(
        bool confirm)
    {
        var index =
            PendingReportsListBox.SelectedIndex;

        if (index < 0 ||
            index >= _lastPendingReports.Count)
        {
            return;
        }

        var report =
            _lastPendingReports[index];

        try
        {
            _faultService.ReviewFaultReport(
                report.ReportId,
                _staffMember,
                confirm);

            ShowMessage(
                confirm
                    ? $"Report {report.ReportId} confirmed."
                    : $"Report {report.ReportId} rejected.");

            RefreshPendingReports();
            RefreshStaffEquipmentStatus();
            RefreshEquipmentCards();

            if (_selectedEquipment != null)
            {
                RefreshEquipmentDetails();
            }
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
    }

    // =============================================================
    // STAFF EQUIPMENT STATUS — FR-009 SUPPORT
    // =============================================================

    private void RefreshStaffEquipmentStatus()
    {
        StaffEquipmentPanel.Children.Clear();

        var equipmentList =
            _sessionService.GetAllEquipmentStatus();

        foreach (var equipment in equipmentList)
        {
            var row =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions("*,Auto"),
                    Margin =
                        new Thickness(0, 4)
                };

            var name =
                new TextBlock
                {
                    Text = equipment.Name,
                    Foreground = WhiteBrush,
                    FontSize = 14
                };

            var status =
                new TextBlock
                {
                    Text =
                        GetStatusDisplayName(
                            equipment.Status),

                    Foreground =
                        GetStatusBrush(
                            equipment.Status),

                    FontWeight =
                        FontWeight.SemiBold,

                    FontSize = 13
                };

            Grid.SetColumn(name, 0);
            Grid.SetColumn(status, 1);

            row.Children.Add(name);
            row.Children.Add(status);

            StaffEquipmentPanel.Children.Add(row);
        }
    }

    // =============================================================
    // TIMER
    // =============================================================

    private void UiTimer_Tick(
        object? sender,
        EventArgs e)
    {
        RefreshSessionDuration();
        CheckClaimTimeout();
        CheckMaxSessionDuration();
    }

    // =============================================================
    // HEADER / BACK BUTTONS
    // =============================================================

    private void DetailBackButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        ShowDashboard();
    }

    private void QueueBackButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_selectedEquipment != null)
        {
            ShowEquipmentDetails(
                _selectedEquipment);
        }
        else
        {
            ShowDashboard();
        }
    }

    private void ReportBackButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_activeEquipmentId != null)
        {
            ShowActiveSession();
        }
        else if (_selectedEquipment != null)
        {
            ShowEquipmentDetails(
                _selectedEquipment);
        }
        else
        {
            ShowDashboard();
        }
    }

    private void ReportDoneButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        ShowDashboard();
    }

    // =============================================================
    // STAFF NAVIGATION
    // =============================================================

    private void OpenStaffButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        ShowStaffView();
    }

    private void ExitStaffButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        ShowDashboard();
    }

    // =============================================================
    // BOTTOM NAVIGATION
    // =============================================================

    private void EquipmentNavButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        ShowDashboard();
    }

    private void PrototypeNavigation_Click(
        object? sender,
        RoutedEventArgs e)
    {
        ShowMessage(
            "This navigation item is outside the GymQ prototype scope.");
    }

    // =============================================================
    // GLOBAL MESSAGE
    // =============================================================

    private void ShowMessage(
        string message)
    {
        GlobalMessageText.Text =
            message;

        MessageBar.IsVisible =
            !string.IsNullOrWhiteSpace(message);
    }

    // =============================================================
    // STATUS HELPERS
    // =============================================================

    private static string GetStatusDisplayName(
        EquipmentStatus status)
    {
        return status switch
        {
            EquipmentStatus.Available =>
                "AVAILABLE",

            EquipmentStatus.InUse =>
                "IN USE",

            EquipmentStatus.Unavailable =>
                "OUT OF SERVICE",

            _ =>
                status.ToString().ToUpperInvariant()
        };
    }

    private static IBrush GetStatusBrush(
        EquipmentStatus status)
    {
        return status switch
        {
            EquipmentStatus.Available =>
                GreenBrush,

            EquipmentStatus.InUse =>
                OrangeBrush,

            EquipmentStatus.Unavailable =>
                RedBrush,

            _ =>
                WhiteBrush
        };
    }

    // =============================================================
    // FAULT SERVICE REPOSITORY ADAPTER
    // =============================================================

    private sealed class LocalEquipmentRepository
        : IEquipmentRepository
    {
        private readonly Dictionary<string, Equipment>
            _equipment;

        public LocalEquipmentRepository(
            Dictionary<string, Equipment> equipment)
        {
            _equipment = equipment;
        }

        public Equipment? GetById(
            string equipmentId)
        {
            return _equipment.TryGetValue(
                equipmentId,
                out var equipment)
                    ? equipment
                    : null;
        }
    }
}