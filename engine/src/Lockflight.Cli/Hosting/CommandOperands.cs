namespace Lockflight.Cli.Hosting;

/// <summary>What may follow a command's words on the command line; dispatch checks it before the command is constructed.</summary>
internal enum CommandOperands
{
    /// <summary>Nothing, not even an option (<c>link</c>, <c>version</c>); anything more is an unknown command.</summary>
    None = 0,

    /// <summary>Options only; the command's parser refuses anything else.</summary>
    Options = 1,

    /// <summary>Exactly one operand and nothing else (<c>hook &lt;event&gt;</c>).</summary>
    ExactlyOne = 2,

    /// <summary>At least one operand, then options (<c>phase check &lt;phase&gt;</c>); without one it is an unknown command.</summary>
    AtLeastOne = 3,

    /// <summary>Operands and options in any number; the command checks them itself (<c>propose &lt;path&gt; …</c>).</summary>
    Any = 4,
}
