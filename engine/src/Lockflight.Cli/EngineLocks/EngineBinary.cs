namespace Lockflight.Cli.EngineLocks;

/// <summary>One platform's executable in a release: its SHA-256 and its path inside the RID package (<c>tools/any/&lt;rid&gt;/lockflight</c>).</summary>
internal sealed record EngineBinary(string Sha256, string Path);
