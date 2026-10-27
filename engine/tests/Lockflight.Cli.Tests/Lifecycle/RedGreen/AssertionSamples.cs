using System.Diagnostics;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

/// <summary>Method bodies the test code scanner reads for assertions, with the engine as the production assembly.</summary>
public static class AssertionSamples
{
    public static void AssertsWithXunit() => Assert.Equal("RED_STUB", RedClassNames.ToName(RedClass.RedStub));

    public static void AssertsInAHelper() => Check(RedClassNames.ToName(RedClass.RedStub));

    public static void AssertsThroughALambda() => Assert.Throws<FormatException>(() => RedClassNames.Parse("NOPE"));

    public static async Task AssertsAfterAnAwait()
    {
        await Task.Yield();
        Assert.Equal("FLAKY", RedClassNames.ToName(RedClass.Flaky));
    }

    public static void AssertsWithAnAssertMethodOfAnotherLibrary() => Debug.Assert(RedClassNames.ToName(RedClass.Flaky).Length > 0);

    public static void ThrowsWhenTheResultIsWrong()
    {
        if (RedClassNames.ToName(RedClass.Flaky) != "FLAKY")
        {
            throw new InvalidOperationException("wrong name");
        }
    }

    public static void CallsProductionWithoutAsserting() => _ = RedClassNames.ToName(RedClass.RedStub);

    public static void DoesNothing()
    {
    }

    public static async Task AwaitsWithoutAsserting()
    {
        await Task.Yield();
        _ = RedClassNames.ToName(RedClass.Flaky);
    }

    private static void Check(string name) => Assert.NotEmpty(name);
}
