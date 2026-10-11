using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GymQ.Desktop.ViewModels;
using GymQ.Services;
using GymQ_ENSE707_SQA_Project;

namespace GymQ.Tests;

[TestCategory("UI")]
[TestClass]
[DoNotParallelize]
public class OverlayPriorityUiTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task IncomingNudge_ReplacesReportSuccessImmediately_AndCanBeAnswered(bool stillUsing)
    {
        await UiSession.Run(() =>
        {
            var (gym, shell, window) = CreateReportSuccess();
            try
            {
                var success = shell.Overlay;
                var report = gym.Faults.GetPendingReports().Single();
                gym.Join("E1", gym.FindMember("M002"));
                gym.Join("E1", gym.FindMember("M003"));
                Assert.AreSame(success, shell.Overlay);

                gym.SendNudge("E1", gym.FindMember("M002"));
                var notice = gym.Nudges.Single();
                var originalExpiry = notice.ExpiresAt;

                // Changed must replace the success popup immediately, without a tick or DONE.
                Assert.IsInstanceOfType<NudgeOverlay>(shell.Overlay);
                var nudge = (NudgeOverlay)shell.Overlay!;
                Assert.AreEqual(notice.SessionId, nudge.SessionId);
                Assert.AreEqual("Please respond within 02:00.", nudge.Countdown);
                AssertReportSuccessHidden(window);
                Assert.IsTrue(Button(window, "YES, I'M STILL USING IT").IsEffectivelyEnabled);
                Assert.IsTrue(Button(window, "END MY SESSION").IsEffectivelyEnabled);
                SaveFrame(window, stillUsing ? "report-success-nudge-still-using" : "report-success-nudge-finish");

                gym.AdvanceDemoTime(1); shell.Tick();
                Assert.AreSame(nudge, shell.Overlay);
                Assert.AreEqual(originalExpiry, gym.Nudges.Single().ExpiresAt);
                Assert.AreEqual("Please respond within 01:00.", nudge.Countdown);

                Click(window, stillUsing ? "YES, I'M STILL USING IT" : "END MY SESSION");
                Assert.IsFalse(shell.HasError);
                Assert.IsEmpty(gym.Nudges);
                Assert.IsNull(shell.Overlay); // Option A: the report confirmation stays dismissed.
                AssertReportSuccessHidden(window);
                if (stillUsing)
                {
                    Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1", "M001"));
                    Assert.IsNull(gym.Queue.ReadQueue("E1")[0].NotifiedAt);
                }
                else
                {
                    Assert.IsNull(gym.Sessions.ReadActiveSession("E1"));
                    Assert.AreEqual(SessionEndReason.NudgeResponse, gym.Sessions.ReadSessions().Single().EndReason);
                    Assert.AreEqual("M002", gym.Queue.ReadQueue("E1")[0].MemberId);
                    Assert.IsNotNull(gym.Queue.ReadQueue("E1")[0].NotifiedAt);
                    Assert.IsNull(gym.Queue.ReadQueue("E1")[1].NotifiedAt);
                }

                // Answering the visible nudge prevents the old deadline from ending the session.
                gym.AdvanceDemoTime(2); shell.Tick(); shell.Refresh();
                Assert.AreEqual(stillUsing, gym.Sessions.ReadActiveSession("E1") != null);
                Assert.IsNull(shell.Overlay);
                AssertReportSuccessHidden(window);
                Assert.AreEqual(report.ReportId, gym.Faults.GetPendingReports().Single().ReportId);
                Assert.AreEqual(FaultReportStatus.Pending, report.Status);
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task IncomingClaim_ReplacesReportSuccess_WithoutResettingDeadline()
    {
        await UiSession.Run(() =>
        {
            var (gym, shell, window) = CreateReportSuccess(activeSession: false);
            try
            {
                gym.Start("E2", gym.FindMember("M002"));
                gym.Join("E2", gym.FindMember("M001"));
                gym.Join("E2", gym.FindMember("M003"));
                Assert.IsInstanceOfType<SuccessOverlay>(shell.Overlay);

                gym.Finish("E2", gym.FindMember("M002"));
                Assert.IsInstanceOfType<ClaimOverlay>(shell.Overlay);
                var notifiedAt = gym.Queue.ReadQueue("E2")[0].NotifiedAt;
                Assert.IsNotNull(notifiedAt);
                AssertReportSuccessHidden(window);
                Assert.IsTrue(Button(window, "CLAIM MACHINE").IsEffectivelyEnabled);

                gym.AdvanceDemoTime(1); shell.Tick();
                Assert.AreEqual(notifiedAt, gym.Queue.ReadQueue("E2")[0].NotifiedAt);
                Assert.AreEqual("Claim expires in 01:00", ((ClaimOverlay)shell.Overlay!).Countdown);
                Click(window, "CLAIM MACHINE");

                Assert.IsFalse(shell.HasError);
                Assert.IsNull(shell.Overlay);
                AssertReportSuccessHidden(window);
                Assert.AreEqual("M001", gym.Sessions.ReadActiveSession("E2")!.MemberId);
                Assert.AreEqual("M003", gym.Queue.ReadQueue("E2").Single().MemberId);
                Assert.IsNull(gym.Queue.ReadQueue("E2").Single().NotifiedAt);
                Assert.HasCount(1, gym.Faults.GetPendingReports());
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ReportSuccess_WithoutOwnUrgentNotice_RemainsVisibleUntilDone()
    {
        await UiSession.Run(() =>
        {
            var (gym, shell, window) = CreateReportSuccess(activeSession: false);
            try
            {
                var success = shell.Overlay;
                // Another member's claim notice must not dismiss this member's confirmation.
                gym.Join("E1", gym.FindMember("M002"));
                shell.Refresh(); shell.Tick();
                Assert.AreSame(success, shell.Overlay);
                Assert.IsTrue(Button(window, "DONE").IsEffectivelyEnabled);

                Click(window, "DONE");
                Assert.IsNull(shell.Overlay);
                AssertReportSuccessHidden(window);
                Assert.HasCount(1, gym.Faults.GetPendingReports());
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    private static (GymSession Gym, ShellViewModel Shell, MainWindow Window) CreateReportSuccess(bool activeSession = true)
    {
        var gym = new GymSession(false, new FixedClock());
        if (activeSession) gym.Start("E1", gym.FindMember("M001"));
        var shell = new ShellViewModel(gym);
        shell.SignIn(gym.FindMember("M001"));
        shell.Navigate("report", "E3");
        var window = new MainWindow(shell);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ReportDescription").Text = "Cable slips during use.";
            Click(window, "SUBMIT REPORT");
            Assert.IsFalse(shell.HasError);
            Assert.IsInstanceOfType<SuccessOverlay>(shell.Overlay);
            Assert.IsTrue(Button(window, "DONE").IsEffectivelyEnabled);
            Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t =>
                t.IsEffectivelyVisible && t.Text == "Report submitted"));
            var report = gym.Faults.GetPendingReports().Single();
            Assert.AreEqual("E3", report.EquipmentId);
            Assert.AreEqual("M001", report.SubmittedByMemberId);
            Assert.AreEqual("Cable slips during use.", report.Description);
            return (gym, shell, window);
        }
        catch { window.Close(); throw; }
    }

    private static Button Button(MainWindow window, string label)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Button>().Single(b =>
            b.IsEffectivelyVisible && b.Content is string text && text == label);
    }

    private static void Click(MainWindow window, string label)
    {
        var button = Button(window, label);
        Assert.IsTrue(button.IsEffectivelyEnabled);
        button.BringIntoView(); Dispatcher.UIThread.RunJobs(); button.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertReportSuccessHidden(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.IsFalse(window.GetVisualDescendants().OfType<TextBlock>().Any(t =>
            t.IsEffectivelyVisible && t.Text == "Report submitted"));
        Assert.IsFalse(window.GetVisualDescendants().OfType<Button>().Any(b =>
            b.IsEffectivelyVisible && b.Content is string text && text == "DONE"));
    }

    private static void SaveFrame(MainWindow window, string name)
    {
        var output = Environment.GetEnvironmentVariable("GYMQ_SCREENSHOTS");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap);
        bitmap.Save(Path.Combine(output, name + ".png"));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
    }
}
