using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

/// <summary>A generic contract-test base whose test calls production code; read by the test code scanner, never run.</summary>
public abstract class InheritedProductionCallBase<T>
{
    public void CallsFromAGenericBase() => Assert.Equal("RED_STUB", RedClassNames.ToName(RedClass.RedStub) + typeof(T).Name[..0]);
}
