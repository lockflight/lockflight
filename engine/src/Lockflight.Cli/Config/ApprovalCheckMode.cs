namespace Lockflight.Cli.Config;

/// <summary>Whether the CI judge fails a pull request whose approvals are not in place, or only reports it (<c>approvals.check</c>).</summary>
internal enum ApprovalCheckMode
{
    /// <summary>Missing CODEOWNERS coverage or a spec-first violation fails the judge (<c>fail</c>).</summary>
    Fail = 1,

    /// <summary>They are listed in the result and the comment but do not fail it (<c>report</c>).</summary>
    Report = 2,
}
