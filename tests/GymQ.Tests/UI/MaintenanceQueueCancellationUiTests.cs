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
public class MaintenanceQueueCancellationUiTests
{
    [TestMethod]
    public async Task ConfirmedFault_ReplacesPendingClaimPopup_WithCancellationMessage()
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

                Assert.IsInstanceOfType<QueueCancellationOverlay>(shell.Overlay);
                Assert.IsEmpty(gym.Queue.ReadQueue("E1"));
                Assert.IsFalse(window.GetVisualDescendants().OfType<Button>().Any(b =>
                    b.IsEffectivelyVisible && b.Content is string label && label == "CLAIM MACHINE"));
                Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t =>
                    t.IsEffectivelyVisible && t.Text == gym.ReadQueueCancellation("M001")!.Message));
                SaveFrame(window, "gq02-queue-cancelled");

                Click(window, "OK");
                Assert.IsNull(shell.Overlay);
                Assert.IsNull(gym.ReadQueueCancellation("M001"));
                Assert.AreEqual("You're no longer queued", ((QueueViewModel)shell.Page).Position);
                Assert.IsEmpty(((QueueViewModel)shell.Page).People);
                Assert.IsTrue(Button(window, "Leave Queue").IsEffectivelyEnabled);
                gym.AdvanceDemoTime(2); gym.AdvanceDemoTime(2); shell.Tick();
                Assert.IsNull(shell.Overlay);
                Assert.IsEmpty(gym.Queue.ReadQueue("E1"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [TestMethod]
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
                Assert.IsNull(shell.Overlay);
                shell.SignIn(gym.FindMember("M003"));
                Assert.IsNull(shell.Overlay);

                shell.SignIn(gym.FindMember("M001"));
                var notice = ((QueueCancellationOverlay)shell.Overlay!).Notice;
                shell.SignOut();
                Assert.IsNull(shell.Overlay);
                Assert.AreSame(notice, gym.ReadQueueCancellation("M001"));
                shell.SignIn(gym.FindMember("M001"));
                Assert.AreSame(notice, ((QueueCancellationOverlay)shell.Overlay!).Notice);
                Click(window, "OK");
                Assert.IsNotNull(gym.ReadQueueCancellation("M002"));

                shell.SignIn(gym.FindMember("M002"));
                Assert.AreEqual("M002", ((QueueCancellationOverlay)shell.Overlay!).Notice.MemberId);
                Assert.AreEqual(notice.Message, ((QueueCancellationOverlay)shell.Overlay!).Message);
                Click(window, "OK");
                shell.SignIn(gym.FindMember("M001")); shell.Tick();
                Assert.IsNull(shell.Overlay);
                Assert.IsNull(gym.ReadQueueCancellation("M001"));
                Assert.IsNull(gym.ReadQueueCancellation("M002"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [TestMethod]
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
                Assert.IsInstanceOfType<QueueCancellationOverlay>(shell.Overlay);
                Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1"));
                Assert.IsNull(gym.Sessions.ReadActiveSession("E2"));
                Click(window, "OK");
                Assert.IsNull(shell.Overlay);
                Assert.IsTrue(Button(window, "End Session").IsEffectivelyEnabled);
                Assert.AreEqual(1, gym.Sessions.ReadSessions().Count(s => s.MemberId == "M001" && s.EndTime == null));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [TestMethod]
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
                Assert.AreEqual("E1", ((QueueCancellationOverlay)shell.Overlay!).Notice.EquipmentId);
                Click(window, "OK");
                Assert.AreEqual("E4", ((QueueCancellationOverlay)shell.Overlay!).Notice.EquipmentId);
                Assert.AreEqual("The queue for Recumbent Bike has been cancelled because the equipment has been marked Out of Service.",
                    ((QueueCancellationOverlay)shell.Overlay!).Message);
                Click(window, "OK");
                shell.Tick();
                Assert.IsNull(shell.Overlay);
                Assert.IsNull(gym.ReadQueueCancellation("M001"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
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
