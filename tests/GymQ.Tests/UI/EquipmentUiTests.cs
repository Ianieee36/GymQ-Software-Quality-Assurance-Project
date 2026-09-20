using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GymQ_ENSE707_SQA_Project;
using GymQ.Desktop.ViewModels;
using GymQ.Application;
namespace GymQ.Tests;
public class UiTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
[TestClass]
[DoNotParallelize]
public class EquipmentUiTests
{
    [TestMethod]
    public async Task EquipmentNavigation_AndMemberStaffCommandsWork()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var shell = new ShellViewModel(new GymSession()); var w = new MainWindow(shell); w.Show();
            var output = Environment.GetEnvironmentVariable("GYMQ_SCREENSHOTS") ?? Path.Combine(Path.GetTempPath(), "gymq-equipment-ui-screenshots"); Directory.CreateDirectory(output);
            void Snap(string name)
            {
                Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = w.CaptureRenderedFrame(); Assert.IsNotNull(bitmap); bitmap.Save(Path.Combine(output, name + ".png"));
            }
            void Click(string label)
            {
                Dispatcher.UIThread.RunJobs();
                var b = w.GetVisualDescendants().OfType<Button>().First(x => x.Content is string s && s == label);
                b.BringIntoView(); Dispatcher.UIThread.RunJobs(); b.Focus();
                w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); w.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            }
            void SwitchAccount(int index)
            {
                shell.Navigate("profile"); Dispatcher.UIThread.RunJobs();
                var selector = w.GetVisualDescendants().OfType<ComboBox>().Single();
                selector.SelectedIndex = index; Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(shell.Gym.Members[index].MemberId, shell.Current.MemberId);
            }
            Snap("01-equipment");
            var cards = ((EquipmentViewModel)shell.Page).Cards;
            Assert.HasCount(4, cards);
            cards[0].Open.Execute(null); Assert.IsInstanceOfType<DetailViewModel>(shell.Page);
            Click("Start Session"); Assert.IsInstanceOfType<SessionViewModel>(shell.Page);
            Assert.AreEqual("M001", shell.Gym.Sessions.ReadActiveSession("E1")!.MemberId);
            Click("End Session"); Assert.IsNull(shell.Gym.Sessions.ReadActiveSession("E1"));
            shell.Navigate("equipment");
            Click("Join Queue"); Assert.IsInstanceOfType<QueueViewModel>(shell.Page); Assert.AreEqual(1, shell.Gym.Queue.GetQueuePosition("E2", "M001"));
            shell.Gym.Join("E2", shell.Gym.Members[2]); shell.Gym.Join("E2", shell.Gym.Members[3]); Snap("02-queue");
            Click("Nudge User"); Assert.HasCount(1, shell.Gym.Nudges.ToList());
            SwitchAccount(1); Assert.IsInstanceOfType<NudgeOverlay>(shell.Overlay); Snap("03-nudge");
            Click("END MY SESSION"); SwitchAccount(0); Assert.IsInstanceOfType<ClaimOverlay>(shell.Overlay); Snap("04-your-turn");
            Click("CLAIM MACHINE"); Assert.IsInstanceOfType<SessionViewModel>(shell.Page); Assert.IsNull(shell.Overlay); Snap("05-session");
            // Reporting must use the explicitly selected machine, even during another active session.
            shell.Navigate("report", "E1"); var report = (ReportViewModel)shell.Page; Snap("06-report");
            var box = w.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "ReportDescription"); box.Text = "The belt slips during a run.";
            Click("SUBMIT REPORT"); Assert.IsInstanceOfType<SuccessOverlay>(shell.Overlay); Assert.AreEqual("E1", shell.Gym.Faults.GetPendingReports().Single().EquipmentId); Snap("07-report-submitted");
            Click("DONE"); SwitchAccount(4); shell.Navigate("staff"); Snap("08-staff");
            Click("Confirm fault"); Assert.AreEqual(GymQ.Models.EquipmentStatus.Unavailable, shell.Gym.Equipment["E1"].Status);
            SwitchAccount(0); shell.Navigate("profile"); Snap("09-profile");
            shell.Navigate("equipment"); w.Width = 370; w.Height = 680; Snap("12-small-window");
            w.Close();
        }, CancellationToken.None);
    }
}
