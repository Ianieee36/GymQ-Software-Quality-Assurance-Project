using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GymQ.Desktop.ViewModels;
using GymQ.Services;
using GymQ_ENSE707_SQA_Project;

namespace GymQ.Tests;

[TestClass]
[DoNotParallelize]
public class ClaimRecoveryUiTests
{
    [TestMethod]
    public async Task GoToCurrentSession_PreservesTurnAndDeadline_ThenAllowsClaimAfterManualFinish()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var (gym, shell, window) = CreateOfferedClaim();
            try
            {
                var notifiedAt = gym.Queue.ReadQueue("E2")[0].NotifiedAt;
                Assert.IsNotNull(notifiedAt);
                Assert.IsInstanceOfType<ClaimOverlay>(shell.Overlay);
                Assert.IsFalse(Button(window, "CLAIM MACHINE").IsEffectivelyEnabled);
                Assert.IsTrue(Button(window, "Go to Current Session").IsEffectivelyVisible);
                Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t =>
                    t.IsEffectivelyVisible && t.Text != null && t.Text.StartsWith("Finish your current session")));
                SaveFrame(window, "gq01-claim-recovery");

                // The service still rejects a second session even if a caller bypasses the disabled button.
                ((ClaimOverlay)shell.Overlay!).Claim.Execute(null);
                Assert.IsNull(gym.Sessions.ReadActiveSession("E2"));
                Assert.AreEqual(1, gym.Queue.GetQueuePosition("E2", "M001"));

                gym.AdvanceDemoTime(1);
                Click(window, "Go to Current Session");
                Assert.IsInstanceOfType<SessionViewModel>(shell.Page);
                Assert.AreEqual("E1", ((SessionViewModel)shell.Page).EquipmentId);
                Assert.IsNull(shell.Overlay);
                Assert.IsTrue(Button(window, "End Session").IsEffectivelyEnabled);
                Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1"));
                Assert.AreEqual(notifiedAt, gym.Queue.ReadQueue("E2")[0].NotifiedAt);
                CollectionAssert.AreEqual(new[] { "M001", "M003" }, gym.Queue.ReadQueue("E2").Select(q => q.MemberId).ToArray());

                // Neither normal timer ticks nor returning to the offered turn restart its timer.
                shell.Tick(); shell.Refresh(); shell.Tick();
                Assert.IsNull(shell.Overlay);
                shell.Navigate("queue", "E2");
                Assert.IsInstanceOfType<ClaimOverlay>(shell.Overlay);
                Assert.AreEqual(notifiedAt, gym.Queue.ReadQueue("E2")[0].NotifiedAt);
                Assert.AreEqual("Claim expires in " + PageViewModel.Time(notifiedAt.Value.AddMinutes(2) - gym.UtcNow),
                    ((ClaimOverlay)shell.Overlay!).Countdown);
                Click(window, "Go to Current Session");
                Click(window, "End Session");

                Assert.IsNull(gym.Sessions.ReadActiveSession("E1"));
                Assert.AreEqual(SessionEndReason.ManualFinish, gym.Sessions.ReadSessions().Single(s => s.EquipmentId == "E1").EndReason);
                Assert.IsInstanceOfType<ClaimOverlay>(shell.Overlay);
                Assert.IsTrue(Button(window, "CLAIM MACHINE").IsEffectivelyEnabled);
                Assert.AreEqual(notifiedAt, gym.Queue.ReadQueue("E2")[0].NotifiedAt);
                Click(window, "CLAIM MACHINE");

                Assert.IsNull(shell.Overlay);
                Assert.AreEqual("E2", ((SessionViewModel)shell.Page).EquipmentId);
                Assert.AreEqual("M001", gym.Sessions.ReadActiveSession("E2")!.MemberId);
                Assert.AreEqual(1, gym.Sessions.ReadSessions().Count(s => s.MemberId == "M001" && s.EndTime == null));
                Assert.IsNull(gym.Queue.GetQueuePosition("E2", "M001"));
                Assert.AreEqual("M003", gym.Queue.ReadQueue("E2").Single().MemberId);
                Assert.IsNull(gym.Queue.ReadQueue("E2").Single().NotifiedAt);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task RecoveryOnCurrentSession_StillExpiresTurnAtOriginalDeadline_AndAdvancesFifo()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var (gym, shell, window) = CreateOfferedClaim();
            try
            {
                var notifiedAt = gym.Queue.ReadQueue("E2")[0].NotifiedAt;
                Click(window, "Go to Current Session");
                gym.AdvanceDemoTime(1); shell.Tick();
                Assert.IsNull(shell.Overlay);
                Assert.AreEqual(1, gym.Queue.GetQueuePosition("E2", "M001"));
                Assert.AreEqual(notifiedAt, gym.Queue.ReadQueue("E2")[0].NotifiedAt);

                gym.AdvanceDemoTime(1); shell.Tick();
                Assert.IsNull(gym.Queue.GetQueuePosition("E2", "M001"));
                Assert.AreEqual("M003", gym.Queue.ReadQueue("E2").Single().MemberId);
                Assert.IsNotNull(gym.Queue.ReadQueue("E2").Single().NotifiedAt);
                Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1"));
                Assert.IsNull(gym.Sessions.ReadActiveSession("E2"));
                Assert.IsNull(shell.Overlay);
                shell.Navigate("queue", "E2");
                Assert.IsNull(shell.Overlay);
                Assert.ThrowsExactly<InvalidOperationException>(() => gym.Claim("E2", gym.FindMember("M001")));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task RecoveryOnCurrentSession_DoesNotSuppressNudgeNotice()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var (gym, shell, window) = CreateOfferedClaim();
            try
            {
                var notifiedAt = gym.Queue.ReadQueue("E2")[0].NotifiedAt;
                Click(window, "Go to Current Session");
                gym.Join("E1", gym.FindMember("M003"));
                gym.SendNudge("E1", gym.FindMember("M003"));
                Assert.IsInstanceOfType<NudgeOverlay>(shell.Overlay);
                Assert.AreEqual("E1", ((NudgeOverlay)shell.Overlay!).EquipmentId);
                Assert.AreEqual(notifiedAt, gym.Queue.ReadQueue("E2")[0].NotifiedAt);
                Assert.AreEqual(1, gym.Queue.GetQueuePosition("E2", "M001"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static (GymSession Gym, ShellViewModel Shell, MainWindow Window) CreateOfferedClaim()
    {
        var gym = new GymSession(false);
        gym.Start("E1", gym.FindMember("M001"));
        gym.Start("E2", gym.FindMember("M002"));
        gym.Join("E2", gym.FindMember("M001"));
        gym.Join("E2", gym.FindMember("M003"));
        var shell = new ShellViewModel(gym);
        shell.SignIn(gym.FindMember("M001"));
        shell.Navigate("queue", "E2");
        var window = new MainWindow(shell);
        window.Show();
        gym.Finish("E2", gym.FindMember("M002"));
        return (gym, shell, window);
    }

    private static Button Button(MainWindow window, string label)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string s && s == label);
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

    private static void SaveFrame(MainWindow window, string name)
    {
        var output = Environment.GetEnvironmentVariable("GYMQ_SCREENSHOTS") ?? Path.Combine(Path.GetTempPath(), "gymq-equipment-ui-screenshots");
        Directory.CreateDirectory(output);
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap);
        bitmap.Save(Path.Combine(output, name + ".png"));
    }
}
