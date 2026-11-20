using JustInTime.SystemControl;

namespace JustInTime.Tests;

public sealed class SingleInstanceGuardTests
{
    private static string UniqueName() => $"JustInTime.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void Second_instance_is_refused_and_can_wake_the_first()
    {
        var name = UniqueName();
        using var activated = new ManualResetEventSlim();

        // Мьютекс принадлежит потоку, который его взял, поэтому оба экземпляра создаём и держим в разных потоках,
        // как два процесса.
        using var first = new SingleInstanceGuard(name);
        Assert.True(first.TryAcquire());
        first.StartListening(activated.Set);

        bool secondAcquired = true;
        var t = new Thread(() =>
        {
            using var second = new SingleInstanceGuard(name);
            secondAcquired = second.TryAcquire();
            second.SignalExisting();
        });
        t.Start();
        t.Join();

        Assert.False(secondAcquired);
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Name_can_be_taken_again_after_the_first_instance_exits()
    {
        var name = UniqueName();

        var first = new SingleInstanceGuard(name);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var again = new SingleInstanceGuard(name);
        Assert.True(again.TryAcquire());
    }

    [Fact]
    public void Dispose_stops_the_listener_without_hanging()
    {
        var guard = new SingleInstanceGuard(UniqueName());
        Assert.True(guard.TryAcquire());
        guard.StartListening(() => { });

        guard.Dispose();
    }
}
