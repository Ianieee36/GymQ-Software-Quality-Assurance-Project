using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GymQ.Desktop.ViewModels;
using GymQ.Models;
using GymQ.Services;
using GymQ_ENSE707_SQA_Project;

namespace GymQ.Tests;

[TestCategory("UI")]
[TestClass]
[DoNotParallelize]
public class NudgeFeedbackUiTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task PendingNudge_KeepsRenderedFeedback_WithoutResettingDeadline()
    {
        await UiSession.Run(() =>
        {
            var (gym, shell, page, window, _) = CreateNudge();
            try
            {
                var notice = gym.Nudges.Single();
                var feedback = page.Feedback;
                AssertFeedbackVisible(window, feedback);
                SaveFrame(window, "gq08-pending-nudge-feedback");
                gym.AdvanceDemoTime(1); shell.Tick(); shell.Refresh();
                Assert.AreSame(notice, gym.Nudges.Single());
                Assert.AreEqual(feedback, page.Feedback);
                AssertFeedbackVisible(window, feedback);
                Assert.IsFalse(page.CanNudge);
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AnsweredNudge_ClearsSenderFeedbackImmediately(bool stillUsing)
    {
        await UiSession.Run(() =>
        {
            var (gym, _, page, sender, _) = CreateNudge();
            var targetShell = new ShellViewModel(gym);
            targetShell.SignIn(gym.FindMember("M001"));
            var target = new MainWindow(targetShell);
            target.Show();
            try
            {
                var feedback = page.Feedback;
                Assert.IsInstanceOfType<NudgeOverlay>(targetShell.Overlay);
                Click(target, stillUsing ? "YES, I'M STILL USING IT" : "END MY SESSION");
                Assert.IsFalse(targetShell.HasError);
                Assert.IsEmpty(gym.Nudges);
                AssertFeedbackCleared(sender, page, feedback);
                Assert.AreEqual(stillUsing, gym.Sessions.ReadActiveSession("E1") != null);
                if (stillUsing)
                {
                    Assert.IsFalse(page.CanNudge); // An answer never resets the existing cooldown.
                    Assert.IsNull(gym.Queue.ReadQueue("E1")[0].NotifiedAt);
                    SaveFrame(sender, "gq08-answered-nudge-feedback-cleared");
                }
                else
                {
                    Assert.AreEqual(SessionEndReason.NudgeResponse, gym.Sessions.ReadSessions().Single().EndReason);
                    Assert.AreEqual("M002", gym.Queue.ReadQueue("E1")[0].MemberId);
                    Assert.IsNotNull(gym.Queue.ReadQueue("E1")[0].NotifiedAt);
                    Assert.IsNull(gym.Queue.ReadQueue("E1")[1].NotifiedAt);
                }
            }
            finally { target.Close(); sender.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ExpiredNudge_ClearsFeedback_AndKeepsNormalQueueHandover()
    {
        await UiSession.Run(() =>
        {
            var (gym, shell, page, window, clock) = CreateNudge();
            try
            {
                var feedback = page.Feedback;
                clock.Advance(NudgeService.ResponseWindow); shell.Tick();
                AssertFeedbackCleared(window, page, feedback);
                Assert.IsEmpty(gym.Nudges);
                Assert.IsNull(gym.Sessions.ReadActiveSession("E1"));
                Assert.AreEqual(SessionEndReason.NudgeTimeout, gym.Sessions.ReadSessions().Single().EndReason);
                Assert.AreEqual("M002", gym.Queue.ReadQueue("E1")[0].MemberId);
                Assert.AreEqual(gym.UtcNow, gym.Queue.ReadQueue("E1")[0].NotifiedAt);
                Assert.IsNull(gym.Queue.ReadQueue("E1")[1].NotifiedAt);
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task MaintenanceWithdrawsNudge_ClearsFeedback_AndPreservesActiveSession()
    {
        await UiSession.Run(() =>
        {
            var (gym, shell, page, window, clock) = CreateNudge();
            try
            {
                var feedback = page.Feedback;
                gym.Report("E1", gym.FindMember("M003"), "Belt slips.");
                gym.Review(gym.Reports.Single().ReportId, gym.FindMember("S001"), true);
                AssertFeedbackCleared(window, page, feedback);
                Assert.IsEmpty(gym.Nudges);
                Assert.IsEmpty(gym.Queue.ReadQueue("E1"));
                Assert.IsTrue(shell.HasNotice);
                Assert.AreEqual(EquipmentStatus.Unavailable, gym.Equipment["E1"].Status);
                clock.Advance(NudgeService.ResponseWindow); shell.Tick();
                Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1", "M001"));
                Assert.IsEmpty(gym.Nudges);
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task RequesterLeaves_WithdrawsNudge_AndClearsFeedback()
    {
        await UiSession.Run(() =>
        {
            var (gym, _, page, window, _) = CreateNudge();
            try
            {
                var feedback = page.Feedback;
                gym.Leave("E1", gym.FindMember("M002"));
                AssertFeedbackCleared(window, page, feedback);
                Assert.IsEmpty(gym.Nudges);
                Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1", "M001"));
                Assert.AreEqual("M003", gym.Queue.ReadQueue("E1").Single().MemberId);
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task EndedNudge_DoesNotClearUnrelatedFeedback()
    {
        await UiSession.Run(() =>
        {
            var (gym, shell, page, window, _) = CreateNudge();
            try
            {
                page.Feedback = "Your queue position is saved.";
                gym.Respond("E1", gym.FindMember("M001"), true); shell.Tick(); shell.Refresh();
                Assert.AreEqual("Your queue position is saved.", page.Feedback);
                AssertFeedbackVisible(window, page.Feedback);
                Assert.IsEmpty(gym.Nudges);
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task LaterNudgeOnSameSession_DoesNotKeepEarlierFeedbackAlive()
    {
        await UiSession.Run(() =>
        {
            var (gym, _, page, window, clock) = CreateNudge();
            try
            {
                var original = gym.Nudges.Single();
                var feedback = page.Feedback;
                // Lower-level service callers can replace a request before the shell's next tick.
                gym.Nudging.Respond("E1", "M001", true);
                clock.Advance(NudgeService.CooldownWindow);
                var replacement = gym.Nudging.Request("E1", "M002");
                Assert.AreEqual(original.SessionId, replacement.SessionId);
                Assert.AreNotSame(original, replacement);
                page.Tick();
                AssertFeedbackCleared(window, page, feedback);
                Assert.AreSame(replacement, gym.Nudges.Single());
                Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1"));
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    private static (GymSession Gym, ShellViewModel Shell, QueueViewModel Page, MainWindow Window, ManualClock Clock) CreateNudge()
    {
        var clock = new ManualClock();
        var gym = new GymSession(false, clock);
        gym.Start("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002")); gym.Join("E1", gym.FindMember("M003"));
        var shell = new ShellViewModel(gym);
        shell.SignIn(gym.FindMember("M002")); shell.Navigate("queue", "E1");
        var page = (QueueViewModel)shell.Page;
        var window = new MainWindow(shell); window.Show();
        try
        {
            Assert.AreEqual("", page.Feedback);
            Click(window, "Nudge User");
            Assert.IsFalse(shell.HasError);
            Assert.HasCount(1, gym.Nudges);
            Assert.AreEqual("Nudge sent. The current user has 120 seconds to respond.", page.Feedback);
            AssertFeedbackVisible(window, page.Feedback);
            return (gym, shell, page, window, clock);
        }
        catch { window.Close(); throw; }
    }

    private static void Click(MainWindow window, string label)
    {
        Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>().Single(b =>
            b.IsEffectivelyVisible && b.Content is string text && text == label);
        Assert.IsTrue(button.IsEffectivelyEnabled);
        button.BringIntoView(); Dispatcher.UIThread.RunJobs(); button.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertFeedbackVisible(MainWindow window, string feedback)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t =>
            t.IsEffectivelyVisible && t.Text == feedback));
    }

    private static void AssertFeedbackCleared(MainWindow window, QueueViewModel page, string previousFeedback)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.AreEqual("", page.Feedback);
        Assert.IsFalse(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == previousFeedback));
    }

    private static void SaveFrame(MainWindow window, string name)
    {
        var output = Environment.GetEnvironmentVariable("GYMQ_SCREENSHOTS");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap); bitmap.Save(Path.Combine(output, name + ".png"));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
