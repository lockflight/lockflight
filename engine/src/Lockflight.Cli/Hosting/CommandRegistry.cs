using Lockflight.Cli.Ci;
using Lockflight.Cli.Docs;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Evidence.Blocked;
using Lockflight.Cli.Facts;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Init;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Mascot;
using Lockflight.Cli.Proposals;
using Lockflight.Cli.Review;
using Lockflight.Cli.Routing;
using Lockflight.Cli.Updating;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Hosting;

/// <summary>
/// The one place a command is registered: one line per command line, in the order the catalog lists them. Dispatch, the catalog
/// and the human-only refusal are derived from it; the help text is written by hand and tested against it.
/// </summary>
internal static class CommandRegistry
{
    public static IReadOnlyList<CommandDescriptor> All { get; } =
    [
        new(["version"], CommandAudience.Anyone, [], [], CommandOperands.None, OptionParsing.Own, context => new VersionCommand(context)),
        new(["link"], CommandAudience.Anyone, [], [LinkCommand.InstalledFlag], CommandOperands.Options, OptionParsing.Shared, context => new LinkCommand(context)),
        new(["engine", "verify-lock"], CommandAudience.Anyone, ["--source"], [VerifyLockCommand.AttestationFlag], CommandOperands.AtLeastOne, OptionParsing.Own, context => new VerifyLockCommand(context)),
        new(["engine", "install"], CommandAudience.Human, ["--source"], [], CommandOperands.Options, OptionParsing.Shared, context => new EngineInstallCommand(context)),
        new(["init"], CommandAudience.Anyone, ["--profile", "--preset", "--ci", "--ci-platform", "--unit-of", "--unit-path", "--speckit-preset"], ["--quality", "--architecture-tests", "--dry-run", "--native", "--ci-only", "--no-link", "--no-sandbox"], CommandOperands.Options, OptionParsing.Own, context => new InitCommand(context)),
        new(["doctor"], CommandAudience.Anyone, [], [DoctorCommand.SelfTestFlag, DoctorCommand.CanariesFlag, DoctorCommand.FullCanariesFlag], CommandOperands.Options, OptionParsing.Shared, context => new DoctorCommand(context)),
        new(["statusline"], CommandAudience.Anyone, [], [], CommandOperands.None, OptionParsing.Own, context => new StatusLineCommand(context)),
        new(["update"], CommandAudience.Human, ["--source"], ["--project", "--dry-run"], CommandOperands.Options, OptionParsing.Own, context => new UpdateCommand(context)),
        new(["docs", "check"], CommandAudience.Anyone, [], [], CommandOperands.Options, OptionParsing.Shared, context => new DocsCheckCommand(context)),
        new(["facts"], CommandAudience.Anyone, [], [FactsCommand.JsonFlag], CommandOperands.Options, OptionParsing.Shared, context => new FactsCommand(context)),
        new(["hook"], CommandAudience.Anyone, [], [], CommandOperands.ExactlyOne, OptionParsing.Own, context => new HookCommand(context)),
        new(["baseline", "build"], CommandAudience.Human, ["--path"], [], CommandOperands.Options, OptionParsing.Shared, context => new BaselineCommand(context)),
        new(["baseline", "tests"], CommandAudience.Human, ["--path"], [], CommandOperands.Options, OptionParsing.Shared, context => new BaselineCommand(context)),
        new(["baseline", "deps"], CommandAudience.Human, ["--path"], [], CommandOperands.Options, OptionParsing.Shared, context => new BaselineCommand(context)),
        new(["baseline", "suppressions"], CommandAudience.Human, [], [], CommandOperands.Options, OptionParsing.Shared, context => new BaselineCommand(context)),
        new(["gate", "run"], CommandAudience.Anyone, ["--profile", "--path", "--unit", "--base", "--result", GateCommand.ShellOption], ["--full"], CommandOperands.Options, OptionParsing.Shared, context => new GateCommand(context)),
        new(["phase", "check"], CommandAudience.Anyone, ["--feature"], [], CommandOperands.AtLeastOne, OptionParsing.Shared, context => new PhaseCommand(context)),
        new(["redgreen", "start"], CommandAudience.Anyone, ["--feature"], [], CommandOperands.Options, OptionParsing.Shared, context => new RedGreenCommand(context)),
        new(["redgreen", "red"], CommandAudience.Anyone, ["--feature"], ["--verbose"], CommandOperands.Options, OptionParsing.Shared, context => new RedGreenCommand(context)),
        new(["redgreen", "green"], CommandAudience.Anyone, ["--feature"], [], CommandOperands.Options, OptionParsing.Shared, context => new RedGreenCommand(context)),
        new(["redgreen", "status"], CommandAudience.Anyone, ["--feature"], [], CommandOperands.Options, OptionParsing.Shared, context => new RedGreenCommand(context)),
        new(["redgreen", "reset"], CommandAudience.Human, ["--feature"], [], CommandOperands.Options, OptionParsing.Shared, context => new RedGreenCommand(context)),
        new(["redgreen", "amend"], CommandAudience.Human, ["--feature", "--reason"], [], CommandOperands.Any, OptionParsing.Shared, context => new RedGreenCommand(context)),
        new(["redgreen"], CommandAudience.Anyone, ["--feature"], [], CommandOperands.AtLeastOne, OptionParsing.Shared, context => new RedGreenCommand(context))
        {
            Listing = CommandListing.Unlisted,
        },
        new(["review"], CommandAudience.Anyone, ["--feature", "--base", "--model", "--budget"], [], CommandOperands.Options, OptionParsing.Shared, context => new ReviewCommand(context)),
        new(["review", "status"], CommandAudience.Anyone, ["--feature"], [], CommandOperands.Options, OptionParsing.Shared, context => new ReviewStatusCommand(context)),
        new(["review", "dismiss"], CommandAudience.Human, ["--reason", "--feature"], [], CommandOperands.Any, OptionParsing.Shared, context => new ReviewDismissCommand(context)),
        new(["propose"], CommandAudience.Anyone, ["--from"], ["--delete"], CommandOperands.Any, OptionParsing.Own, context => new ProposeCommand(context)),
        new(["trace"], CommandAudience.Anyone, ["--feature"], [], CommandOperands.Options, OptionParsing.Shared, context => new TraceCommand(context)),
        new(["blocked", "validate"], CommandAudience.Anyone, [], [], CommandOperands.ExactlyOne, OptionParsing.Own, context => new BlockedValidateCommand(context)),
        new(["approve", "spec"], CommandAudience.Human, ["--feature"], [], CommandOperands.Options, OptionParsing.Shared, context => new ApproveCommand(context)),
        new(["approve", "tasks"], CommandAudience.Human, ["--feature"], [], CommandOperands.Options, OptionParsing.Shared, context => new ApproveCommand(context)),
        new(["approve", "verify"], CommandAudience.Anyone, ["--feature", "--base", "--require"], [], CommandOperands.Options, OptionParsing.Shared, context => new ApproveCommand(context)),
        new(["approve"], CommandAudience.Human, ["--feature"], [], CommandOperands.AtLeastOne, OptionParsing.Shared, context => new ApproveCommand(context))
        {
            Listing = CommandListing.Unlisted,
        },
        new(["ci"], CommandAudience.Anyone, [JudgeInputsReader.HeadOption, JudgeInputsReader.BaseOption, JudgeInputsReader.BranchOption, JudgeInputsReader.PlatformOption, JudgeInputsReader.EventOption, JudgeInputsReader.EvidenceOption, JudgeInputsReader.ShellOption], [JudgeInputsReader.NoReviewFlag, CiCommand.PlanFlag], CommandOperands.Options, OptionParsing.Shared, context => new CiCommand(context)),
        new(["ci", "report"], CommandAudience.Anyone, [CiReportCommand.EvidenceOption, CiReportCommand.ReviewOption, CiReportCommand.HeadOption, CiReportCommand.ContextOption, CiReportCommand.OutOption], [], CommandOperands.Options, OptionParsing.Shared, context => new CiReportCommand(context)),
        new(["ci", "review"], CommandAudience.Anyone, [JudgeInputsReader.HeadOption, JudgeInputsReader.BaseOption, JudgeInputsReader.BranchOption, JudgeInputsReader.PlatformOption, JudgeInputsReader.OutOption], [], CommandOperands.Options, OptionParsing.Shared, context => new CiReviewCommand(context)),
        new(["route", "show"], CommandAudience.Anyone, [RouteShowCommand.BaseOption, RouteShowCommand.HeadOption], [RouteShowCommand.JsonFlag], CommandOperands.Options, OptionParsing.Shared, context => new RouteShowCommand(context)),
        new(["route", "record"], CommandAudience.Anyone, [RouteRecordCommand.FeatureOption, RouteRecordCommand.BaseOption], [], CommandOperands.Options, OptionParsing.Shared, context => new RouteRecordCommand(context)),
        new(["workspace", "sync"], CommandAudience.Anyone, [], ["--check"], CommandOperands.Options, OptionParsing.Shared, context => new WorkspaceSyncCommand(context)),
        new(["workspace", "clone"], CommandAudience.Anyone, [WorkspaceCloneCommand.IntoOption], [], CommandOperands.Options, OptionParsing.Shared, context => new WorkspaceCloneCommand(context)),
    ];

    /// <summary>Command groups reserved for a person even with a verb no descriptor names (<c>approve xyz</c>), except their agent verbs.</summary>
    public static IReadOnlyList<ReservedGroup> ReservedGroups { get; } = [new("baseline", []), new("approve", ["verify"]), new("update", []), new("engine", ["verify-lock"])];

    /// <summary>The descriptor whose words begin <paramref name="args"/>, the longest when several do; null when none does.</summary>
    public static CommandDescriptor? Match(IReadOnlyList<string> args)
    {
        CommandDescriptor? longest = null;
        foreach (CommandDescriptor command in All)
        {
            if (command.Begins(args) && (longest is null || command.Words.Count > longest.Words.Count))
            {
                longest = command;
            }
        }

        return longest;
    }
}
