using Avalonia.Headless;
using GymQ.Desktop.ViewModels;
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
