namespace Lockflight.Cli.Config.Migration;

/// <summary>A switched gate step's key the migration writes: <c>gates.&lt;Section&gt;.&lt;Key&gt;: &lt;Value&gt;</c>, with how to make it judge.</summary>
internal sealed record GateToggle(string Section, string Key, string Value, string Note);
