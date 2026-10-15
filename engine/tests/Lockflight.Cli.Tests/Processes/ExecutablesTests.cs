using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Processes;

public sealed class ExecutablesTests
{
    [Fact]
    public void AnExplicitPathMustExist()
    {
        string existing = typeof(ExecutablesTests).Assembly.Location;

        Assert.Equal(existing, Executables.Find("anything", existing));
        Assert.Null(Executables.Find("anything", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid()}")));
    }

    [Fact]
    public void AnAbsentToolIsNotFoundOnPath() => Assert.Null(Executables.Find($"lockflight-missing-{Guid.NewGuid():N}", explicitPath: null));
}
