namespace Lockflight.Cli.Ci;

/// <summary>Where the spec a pull request is traced against was read.</summary>
internal enum SpecOrigin
{
    /// <summary>The pull request's head tree.</summary>
    Head = 1,

    /// <summary>The target branch's tree (spec-first: the spec was approved there before the code).</summary>
    Base = 2,

    /// <summary>A read-only clone of the workspace shell that holds the specs of its units.</summary>
    Shell = 3,
}
