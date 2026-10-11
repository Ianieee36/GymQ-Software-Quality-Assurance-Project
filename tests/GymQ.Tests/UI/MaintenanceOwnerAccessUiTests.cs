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

[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
public class MaintenanceOwnerAccessUiTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("equipment")]
    [DataRow("detail")]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ConfirmedFault_OwnerCanViewAndFinishSession_FromDashboardAndDetail(string route)
    {
        await UiSession.Run(() =>
        {
            var gym = new GymSession(false);
            var owner = gym.FindMember("M001");
            gym.Start("E1", owner);
            var session = gym.Sessions.ReadActiveSession("E1")!;
            gym.Join("E1", gym.FindMember("M002"));
            var shell = new ShellViewModel(gym);
            shell.SignIn(owner);
            shell.Navigate(route, "E1");
            var window = new MainWindow(shell);
            window.Show();
            try
            {
                ConfirmFault(gym);

                Assert.AreSame(session, gym.Sessions.ReadActiveSession("E1"));
                Assert.AreEqual(EquipmentStatus.Unavailable, gym.Equipment["E1"].Status);
                Assert.IsEmpty(gym.Queue.ReadQueue("E1"));
                Assert.IsNull(shell.Overlay);
                var card = Card(shell);
                Assert.IsTrue(card.CanAct);
                Assert.AreEqual("View Session", card.ActionLabel);
                Assert.AreEqual("Out of service. Please end your session", card.Detail);
                var viewSession = ActionButton(window, card);
                Assert.IsTrue(viewSession.IsEffectivelyVisible);
                Assert.IsTrue(viewSession.IsEffectivelyEnabled);
                SaveFrame(window, $"gq05-owner-{route}-out-of-service");

                Click(window, viewSession);
                Assert.IsInstanceOfType<SessionViewModel>(shell.Page);
                Assert.IsTrue(((SessionViewModel)shell.Page).IsActive);
                Assert.IsNull(shell.Overlay);
                var finish = Button(window, "End Session");
                Assert.IsTrue(finish.IsEffectivelyVisible);
                Assert.IsTrue(finish.IsEffectivelyEnabled);
                SaveFrame(window, $"gq05-owner-{route}-session-reachable");
                Click(window, finish);

                Assert.IsInstanceOfType<EquipmentViewModel>(shell.Page);
                Assert.IsNull(gym.Sessions.ReadActiveSession("E1"));
                Assert.IsNotNull(session.EndTime);
                Assert.AreEqual(SessionEndReason.ManualFinish, session.EndReason);
                Assert.AreEqual(EquipmentStatus.Unavailable, gym.Equipment["E1"].Status);
                Assert.IsEmpty(gym.Queue.ReadQueue("E1"));
                Assert.IsFalse(Card(shell).CanAct);
                Assert.IsFalse(ActionButton(window, Card(shell)).IsEffectivelyVisible);
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ConfirmedFault_NonOwnerCannotStartOrJoin_FromDashboardOrDetail()
    {
        await UiSession.Run(() =>
        {
            var gym = new GymSession(false);
            gym.Start("E1", gym.FindMember("M001"));
            var session = gym.Sessions.ReadActiveSession("E1")!;
            ConfirmFault(gym);
            var nonOwner = gym.FindMember("M003");
            var shell = new ShellViewModel(gym);
            shell.SignIn(nonOwner);
            var window = new MainWindow(shell);
            window.Show();
            try
            {
                foreach (var route in new[] { "equipment", "detail" })
                {
                    shell.Navigate(route, "E1");
                    var card = Card(shell);
                    Assert.IsFalse(card.CanAct);
                    Assert.AreEqual("OUT OF SERVICE", card.Status);
                    Assert.IsFalse(ActionButton(window, card).IsEffectivelyVisible);
                    Assert.IsNull(shell.Overlay);
                    SaveFrame(window, $"gq05-nonowner-{route}-out-of-service");
                }

                Assert.ThrowsExactly<InvalidOperationException>(() => gym.Start("E1", nonOwner));
                Assert.ThrowsExactly<InvalidOperationException>(() => gym.Join("E1", nonOwner));
                Assert.AreSame(session, gym.Sessions.ReadActiveSession("E1"));
                Assert.IsNull(session.EndTime);
                Assert.IsEmpty(gym.Queue.ReadQueue("E1"));
                Assert.AreEqual(EquipmentStatus.Unavailable, gym.Equipment["E1"].Status);
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    private static void ConfirmFault(GymSession gym)
    {
        gym.Report("E1", gym.FindMember("M001"), "The belt slips.");
        gym.Review(gym.Reports.Single().ReportId, gym.FindMember("S001"), confirm: true);
    }

    private static EquipmentCardViewModel Card(ShellViewModel shell) => shell.Page switch
    {
        EquipmentViewModel dashboard => dashboard.Cards.Single(c => c.Id == "E1"),
        DetailViewModel detail => detail.Card,
        _ => throw new InvalidOperationException("Expected equipment dashboard or detail page.")
    };

    private static Button ActionButton(MainWindow window, EquipmentCardViewModel card)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, card.Action));
    }

    private static Button Button(MainWindow window, string label)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string content && content == label);
    }

    private static void Click(MainWindow window, Button button)
    {
        Assert.IsTrue(button.IsEffectivelyVisible);
        Assert.IsTrue(button.IsEffectivelyEnabled);
        button.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        button.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SaveFrame(MainWindow window, string name)
    {
        var output = Environment.GetEnvironmentVariable("GYMQ_SCREENSHOTS") ?? Path.Combine(Path.GetTempPath(), "gymq-equipment-ui-screenshots");
        Directory.CreateDirectory(output);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap);
        bitmap.Save(Path.Combine(output, name + ".png"));
    }
}
