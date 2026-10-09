using Avalonia.Headless;
using GymQ.Desktop.ViewModels;
using GymQ.Models;
using GymQ.Persistence;
using GymQ.Services;

namespace GymQ.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PersistenceUiTests
{
    [TestMethod]
    public async Task SaveFailure_RemainsVisibleAcrossNavigationAndLogout_UntilSuccessfulSave()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var store = new FailingStore();
            var gym = GymSession.OpenPersistent(store, seedWhenMissing: false);
            var shell = new ShellViewModel(gym);
            shell.SignIn(gym.Members[0]);
            store.Fail = true;
            shell.Perform(() => gym.Start("E1", shell.Current));

            Assert.IsTrue(shell.HasError);
            StringAssert.Contains(shell.Error, "could not be saved");
            Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1"));
            shell.Navigate("detail", "E1");
            Assert.IsTrue(shell.HasError);
            shell.SignOut();
            Assert.IsTrue(shell.HasError);

            store.Fail = false;
            Assert.IsTrue(gym.SaveState());
            Assert.IsFalse(shell.HasError);
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task LoadFailureWarning_RemainsVisibleWhenTheMemoryOnlyRunChangesPages()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            const string warning = "Saved data could not be opened. This run uses memory only.";
            var gym = new GymSession(seed: false);
            var shell = new ShellViewModel(gym, warning);
            Assert.IsFalse(shell.IsLoggedIn);
            Assert.AreEqual(warning, shell.Error);
            shell.SignIn(gym.Members[0]);
            shell.Navigate("profile");
            shell.DismissError.Execute(null);
            Assert.IsTrue(shell.HasError);
            Assert.AreEqual(warning, shell.Error);
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Restart_EndedSessionPage_PreservesOwnDurationAndHistory_AfterAnotherMemberClaims()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() => WithTemporaryStateFile(path =>
        {
            var clock = new FixedClock();
            SessionState[] history;
            string ownSessionId;
            string currentOtherMemberSessionId;
            using (var store = new JsonGymStateStore(path))
            {
                var gym = GymSession.OpenPersistent(store, seedWhenMissing: false, clock: clock);
                gym.Start("E1", gym.FindMember("M001"));
                ownSessionId = gym.Sessions.ReadActiveSession("E1")!.SessionId;
                gym.Join("E1", gym.FindMember("M002"));
                gym.AdvanceDemoTime(2);
                gym.Finish("E1", gym.FindMember("M001"));
                gym.Claim("E1", gym.FindMember("M002"));
                currentOtherMemberSessionId = gym.Sessions.ReadActiveSession("E1")!.SessionId;
                gym.AdvanceDemoTime(1);
                history = gym.CaptureState().Sessions;
            }

            using (var store = new JsonGymStateStore(path))
            {
                var restored = GymSession.OpenPersistent(store, seedWhenMissing: false, clock: clock);
                CollectionAssert.AreEquivalent(history, restored.CaptureState().Sessions);
                Assert.AreEqual(ownSessionId, restored.Sessions.ReadLatestSession("E1", "M001")!.SessionId);
                Assert.AreEqual(currentOtherMemberSessionId, restored.Sessions.ReadActiveSession("E1", "M002")!.SessionId);
                Assert.IsNull(restored.Sessions.ReadActiveSession("E1", "M001"));
                var shell = new ShellViewModel(restored);
                Assert.IsFalse(shell.IsLoggedIn);
                shell.SignIn(restored.FindMember("M001"));
                shell.Navigate("session", "E1");

                var page = (SessionViewModel)shell.Page;
                Assert.IsFalse(page.IsActive);
                Assert.AreEqual("Session complete", page.Headline);
                Assert.AreEqual("02:00", page.Duration);
                StringAssert.Contains(page.Remaining, "You ended your session.");
                StringAssert.Contains(page.Remaining, "Another member is now using this machine.");
                shell.Tick();
                Assert.AreEqual("02:00", page.Duration);
                CollectionAssert.AreEquivalent(history, restored.CaptureState().Sessions);
            }
        }), CancellationToken.None);
    }

    [TestMethod]
    public async Task Restart_PendingCancellationReachesSignedInMember_AndBannerAutoAcknowledgementPersists()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() => WithTemporaryStateFile(path =>
        {
            var clock = new FixedClock();
            string message;
            using (var store = new JsonGymStateStore(path))
            {
                var gym = GymSession.OpenPersistent(store, seedWhenMissing: false, clock: clock);
                gym.Join("E1", gym.FindMember("M001"));
                gym.Report("E1", gym.FindMember("M003"), "Broken treadmill belt");
                gym.Review(gym.Reports.Single().ReportId, gym.FindMember("S001"), true);
                message = gym.ReadQueueCancellation("M001")!.Message;
            }

            using (var store = new JsonGymStateStore(path))
            {
                var restored = GymSession.OpenPersistent(store, seedWhenMissing: false, clock: clock);
                var shell = new ShellViewModel(restored);
                Assert.IsFalse(shell.IsLoggedIn);
                Assert.IsFalse(shell.HasNotice);
                Assert.IsNotNull(restored.ReadQueueCancellation("M001"));
                shell.SignIn(restored.FindMember("M001"));

                Assert.IsTrue(shell.HasNotice);
                Assert.AreEqual(message, shell.NoticeMessage);
                Assert.IsNull(shell.Overlay);
                Assert.AreEqual(EquipmentStatus.Unavailable, restored.Equipment["E1"].Status);
                Assert.IsEmpty(restored.Queue.ReadQueue("E1"));
                for (var tick = 1; tick < ShellViewModel.NoticeSeconds; tick++) shell.Tick();
                Assert.IsTrue(shell.HasNotice);
                shell.Tick();
                Assert.IsFalse(shell.HasNotice);
                Assert.IsNull(restored.ReadQueueCancellation("M001"));
            }

            using (var store = new JsonGymStateStore(path))
            {
                var restored = GymSession.OpenPersistent(store, seedWhenMissing: false, clock: clock);
                Assert.IsNull(restored.ReadQueueCancellation("M001"));
                Assert.AreEqual(EquipmentStatus.Unavailable, restored.Equipment["E1"].Status);
                Assert.IsEmpty(restored.Queue.ReadQueue("E1"));
                var shell = new ShellViewModel(restored);
                shell.SignIn(restored.FindMember("M001"));
                shell.Tick();
                Assert.IsFalse(shell.HasNotice);
                Assert.IsNull(shell.Overlay);
            }
        }), CancellationToken.None);
    }

    private static void WithTemporaryStateFile(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "gymq-persistence-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(Path.Combine(directory, "state.json")); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FailingStore : IGymStateStore
    {
        public bool Fail { get; set; }
        public GymStateSnapshot? Load() => null;
        public void Save(GymStateSnapshot state)
        {
            if (Fail) throw new IOException("Storage is full.");
        }
    }
}
