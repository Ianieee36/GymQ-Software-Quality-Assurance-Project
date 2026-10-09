using Avalonia.Headless;

namespace GymQ.Tests;

/// <summary>
/// One headless Avalonia session shared by every UI test.
/// Avalonia 11.1.3 has a start-up race in HeadlessUnitTestSession.StartNew that can leave the
/// session's dispatch task null, so Dispose() throws NullReferenceException at random
/// (seen on CI runners). Sharing one session that is never disposed per test avoids it.
/// Each Dispatch still runs in a fresh application scope, so tests stay isolated.
/// </summary>
internal static class UiSession
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(UiTestApp)));

    public static Task Run(Action body, CancellationToken cancellationToken = default) =>
        Session.Value.Dispatch(body, cancellationToken);
}