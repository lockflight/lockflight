using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

/// <summary>Method bodies the test code scanner reads for production calls, with the engine as the production assembly.</summary>
public static class ProductionCallSamples
{

    public static void CallsDirectly() => Assert.Equal("RED_STUB", RedClassNames.ToName(RedClass.RedStub));

    public static void AssertsOnConstants() => Assert.Equal(1, Array.Empty<int>().FirstOrDefault(1));

    public static void FailsUnconditionally() => Assert.Fail("todo");

    public static void NamesAProductionTypeOnly() => Assert.Equal(nameof(RedClass), typeof(RedClass).Name);

    public static void NamesAProductionTypeAsATypeArgument() => Assert.Empty(Enumerable.Empty<FlakyCheckResult>());

    public static void ReadsAProductionProperty()
    {
        Func<FlakyCheckResult, bool> read = result => result.BudgetExhausted;
        Assert.NotNull(read);
    }

    public static void ConstructsAProductionType() => Assert.NotNull(new FlakyCheckResult(new HashSet<string>(), 0, BudgetExhausted: false));

    public static void CallsThroughALambda()
    {
        Func<string> name = () => RedClassNames.ToName(RedClass.Flaky);
        Assert.Equal("FLAKY", name());
    }

    public static void CallsThroughAHelper() => Assert.Equal("FLAKY", Helper());

    public static void CallsThroughALocalFunction()
    {
        Assert.Equal("FLAKY", Local());

        static string Local() => RedClassNames.ToName(RedClass.Flaky);
    }

    public static async Task CallsAfterAnAwait()
    {
        await Task.Yield();
        Assert.Equal("FLAKY", RedClassNames.ToName(RedClass.Flaky));
    }

    public static async Task AwaitsWithoutProductionCode()
    {
        await Task.Yield();
        Assert.Equal(1, 2);
    }

    public static void CallsAProductionGenericInstance() => Assert.Empty(new GenericSample<RedClass>().Values());

    private static string Helper() => RedClassNames.ToName(RedClass.Flaky);

    public static class Nested
    {
        public static void CallsDirectly() => Assert.Equal("FLAKY", RedClassNames.ToName(RedClass.Flaky));
    }

    public sealed class GenericSample<T>
    {
        public IReadOnlyList<string> Values() => [.. Enum.GetValues<RedClass>().Where(value => value.GetType() != typeof(T)).Select(RedClassNames.ToName)];
    }
}
