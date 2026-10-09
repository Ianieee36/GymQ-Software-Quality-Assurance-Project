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
public class MaintenanceQueueCancellationUiTests
{
    // Injected by MSTest. Its CancellationToken is cancelled when [Timeout] expires.
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ConfirmedFault_RemovesPendingClaimPopup_AndShowsCancellationBanner()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var gym = new GymSession(false);
            gym.Join("E1", gym.FindMember("M001"));
            gym.Join("E1", gym.FindMember("M002"));
            var shell = new ShellViewModel(gym);
            shell.SignIn(gym.FindMember("M001"));
            shell.Navigate("queue", "E1");
            var window = new MainWindow(shell);
            window.Show();
            try
            {
                Assert.IsInstanceOfType<ClaimOverlay>(shell.Overlay);
                Assert.IsTrue(Button(window, "CLAIM MACHINE").IsEffectivelyVisible);
                ConfirmFault(gym, "E1");

                // The claim popup goes because M001 is no longer queued; no popup replaces it.
                Assert.IsNull(shell.Overlay);
                Assert.IsEmpty(gym.Queue.ReadQueue("E1"));
                Assert.IsFalse(window.GetVisualDescendants().OfType<Button>().Any(b =>
                    b.IsEffectivelyVisible && b.Content is string label && label == "CLAIM MACHINE"));
                AssertBannerShows(window, gym.ReadQueueCancellation("M001")!.Message);
                Assert.IsTrue(Button(window, "Leave Queue").IsEffectivelyEnabled);   // page is not blocked
                SaveFrame(window, "gq02-queue-cancelled");

                DismissBanner(window);
                Assert.IsFalse(shell.HasNotice);
                Assert.IsFalse(Banner(window).IsEffectivelyVisible);
                Assert.IsNull(gym.ReadQueueCancellation("M001"));
                Assert.AreEqual("You're no longer queued", ((QueueViewModel)shell.Page).Position);
                Assert.IsEmpty(((QueueViewModel)shell.Page).People);
                gym.AdvanceDemoTime(2); gym.AdvanceDemoTime(2); shell.Tick();
                Assert.IsNull(shell.Overlay);
                Assert.IsEmpty(gym.Queue.ReadQueue("E1"));
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    /// <summary>
    /// Regression: a cancellation used to replace the nudge popup, hiding the nudge
    /// while its deadline kept running, so the session ended as NudgeTimeout.
    /// </summary>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task CancellationDuringNudge_KeepsNudgePopupVisible_AndBannerOnTop()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var gym = new GymSession(false);
            gym.Start("E1", gym.FindMember("M001"));
            gym.Join("E2", gym.FindMember("M001"));
            var shell = new ShellViewModel(gym);
            shell.SignIn(gym.FindMember("M001"));
            var window = new MainWindow(shell);
            window.Show();
            try
            {
                Click(window, "Go to Current Session");
                Assert.IsInstanceOfType<SessionViewModel>(shell.Page);

                gym.Join("E1", gym.FindMember("M002"));
                gym.SendNudge("E1", gym.FindMember("M002"));
                Dispatcher.UIThread.RunJobs();
                Assert.IsInstanceOfType<NudgeOverlay>(shell.Overlay);

                ConfirmFault(gym, "E2");

                // The nudge stays in front of the member and stays answerable.
                Assert.IsInstanceOfType<NudgeOverlay>(shell.Overlay);
                Assert.AreEqual("E1", ((NudgeOverlay)shell.Overlay!).EquipmentId);
                Assert.IsTrue(Button(window, "YES, I'M STILL USING IT").IsEffectivelyVisible);
                Assert.IsTrue(Button(window, "YES, I'M STILL USING IT").IsEffectivelyEnabled);
                // The cancellation is shown as a banner, and the E2 queue is already gone.
                AssertBannerShows(window, "The queue for Squat Rack #2 has been cancelled");
                Assert.IsEmpty(gym.Queue.ReadQueue("E2"));
                SaveFrame(window, "gq-cancel-banner-over-nudge");

                Click(window, "YES, I'M STILL USING IT");
                Assert.IsNull(shell.Overlay);
                Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1", "M001"));

                // Past the old nudge deadline: E1 must not end as NudgeTimeout.
                gym.AdvanceDemoTime(2); shell.Tick();
                Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1", "M001"));
                Assert.IsTrue(shell.HasNotice);   // the banner was never in the way, so it can wait
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task Cancellation_ReachesEachAffectedMember_AfterSignIn_AndPersistsUntilAcknowledged()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var gym = new GymSession(false);
            gym.Join("E1", gym.FindMember("M001"));
            gym.Join("E1", gym.FindMember("M002"));
            var shell = new ShellViewModel(gym);
            shell.SignIn(gym.FindMember("S001"));
            var window = new MainWindow(shell);
            window.Show();
            try
            {
                ConfirmFault(gym, "E1");
                Assert.IsFalse(shell.HasNotice);      // staff never get the member banner
                shell.SignIn(gym.FindMember("M003"));
                Assert.IsFalse(shell.HasNotice);      // M003 was not queued

                shell.SignIn(gym.FindMember("M001"));
                var notice = shell.Notice!;
                Assert.IsNull(shell.Overlay);
                shell.SignOut();
                Assert.IsFalse(shell.HasNotice);
                Assert.AreSame(notice, gym.ReadQueueCancellation("M001"));   // signing out is not acknowledging
                shell.SignIn(gym.FindMember("M001"));
                Assert.AreSame(notice, shell.Notice);
                DismissBanner(window);
                Assert.IsNotNull(gym.ReadQueueCancellation("M002"));

                shell.SignIn(gym.FindMember("M002"));
                Assert.AreEqual("M002", shell.Notice!.MemberId);
                Assert.AreEqual(notice.Message, shell.NoticeMessage);
                DismissBanner(window);
                shell.SignIn(gym.FindMember("M001")); shell.Tick();
                Assert.IsFalse(shell.HasNotice);
                Assert.IsNull(gym.ReadQueueCancellation("M001"));
                Assert.IsNull(gym.ReadQueueCancellation("M002"));
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task Cancellation_DuringClaimRecovery_LeavesCurrentSessionActive()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var gym = new GymSession(false);
            gym.Start("E1", gym.FindMember("M001"));
            gym.Join("E2", gym.FindMember("M001"));
            var shell = new ShellViewModel(gym);
            shell.SignIn(gym.FindMember("M001"));
            var window = new MainWindow(shell);
            window.Show();
            try
            {
                Click(window, "Go to Current Session");
                Assert.IsNull(shell.Overlay);
                Assert.IsInstanceOfType<SessionViewModel>(shell.Page);
                ConfirmFault(gym, "E2");
                Assert.IsNull(shell.Overlay);
                Assert.IsTrue(shell.HasNotice);
                Assert.IsTrue(Button(window, "End Session").IsEffectivelyEnabled);   // usable without dismissing
                Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1"));
                Assert.IsNull(gym.Sessions.ReadActiveSession("E2"));
                DismissBanner(window);
                Assert.IsFalse(shell.HasNotice);
                Assert.AreEqual(1, gym.Sessions.ReadSessions().Count(s => s.MemberId == "M001" && s.EndTime == null));
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task SeparateCancelledQueues_EachKeepTheirOwnMessageUntilAcknowledged()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var gym = new GymSession(false);
            gym.Join("E1", gym.FindMember("M001"));
            gym.Join("E4", gym.FindMember("M001"));
            var shell = new ShellViewModel(gym);
            shell.SignIn(gym.FindMember("M001"));
            var window = new MainWindow(shell);
            window.Show();
            try
            {
                ConfirmFault(gym, "E1");
                ConfirmFault(gym, "E4");
                Assert.AreEqual("E1", shell.Notice!.EquipmentId);
                DismissBanner(window);
                Assert.AreEqual("E4", shell.Notice!.EquipmentId);
                AssertBannerShows(window, "The queue for Recumbent Bike has been cancelled because the equipment has been marked Out of Service.");
                DismissBanner(window);
                shell.Tick();
                Assert.IsFalse(shell.HasNotice);
                Assert.IsNull(shell.Overlay);
                Assert.IsNull(gym.ReadQueueCancellation("M001"));
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task Banner_AutoHidesAfterNoticeSeconds_AndIsAcknowledged()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var gym = new GymSession(false);
            gym.Join("E1", gym.FindMember("M001"));
            var shell = new ShellViewModel(gym);
            shell.SignIn(gym.FindMember("M001"));
            var window = new MainWindow(shell);
            window.Show();
            try
            {
                ConfirmFault(gym, "E1");
                for (var i = 1; i < ShellViewModel.NoticeSeconds; i++) shell.Tick();
                Assert.IsTrue(shell.HasNotice);
                shell.Tick();
                Assert.IsFalse(shell.HasNotice);
                Assert.IsNull(gym.ReadQueueCancellation("M001"));
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(Banner(window).IsEffectivelyVisible);
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    private static void ConfirmFault(GymSession gym, string equipmentId)
    {
        gym.Report(equipmentId, gym.FindMember("M003"), "Broken handle");
        var staff = new ShellViewModel(gym);
        staff.SignIn(gym.FindMember("S001"));
        ((StaffViewModel)staff.Page).Reports.Single().Confirm.Execute(null);
        Assert.IsFalse(staff.HasError);
        Dispatcher.UIThread.RunJobs();
    }

    private static Border Banner(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "QueueCancelledBanner");
    }

    private static void AssertBannerShows(MainWindow window, string messageStart)
    {
        var banner = Banner(window);
        Assert.IsTrue(banner.IsEffectivelyVisible);
        var title = banner.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NoticeTitle");
        var message = banner.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NoticeText");
        Assert.AreEqual("Queue cancelled", title.Text);
        Assert.StartsWith(messageStart, message.Text!);
    }

    private static void DismissBanner(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DismissNoticeButton");
        Press(window, button);
    }

    private static Button Button(MainWindow window, string label)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string s && s == label);
    }

    private static void Click(MainWindow window, string label) => Press(window, Button(window, label));

    private static void Press(MainWindow window, Button button)
    {
        Assert.IsTrue(button.IsEffectivelyEnabled);
        button.BringIntoView(); Dispatcher.UIThread.RunJobs(); button.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
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
}