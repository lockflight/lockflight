namespace Lockflight.Cli.Ci;

/// <summary>The contract ids of <see cref="CheckId"/> in evidence (<c>red</c>, <c>tests.sinceRed</c>, …) and their display titles.</summary>
internal static class CheckIdNames
{
    public static string ToName(CheckId check) => check switch
    {
        CheckId.Red => "red",
        CheckId.TestsSinceRed => "tests.sinceRed",
        CheckId.TestsExistingChanged => "tests.existingChanged",
        CheckId.TestsHostTampering => "tests.hostTampering",
        CheckId.Green => "green",
        CheckId.Trace => "trace",
        CheckId.Gates => "gates",
        CheckId.Review => "review",
        CheckId.Approvals => "approvals",
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown check."),
    };

    /// <summary>The check's row title in the PR comment.</summary>
    public static string Title(CheckId check) => check switch
    {
        CheckId.Red => "Red at red commits",
        CheckId.TestsSinceRed => "Tests since red",
        CheckId.TestsExistingChanged => "Existing tests changed",
        CheckId.TestsHostTampering => "Test host tampering",
        CheckId.Green => "Green at head",
        CheckId.Trace => "Trace",
        CheckId.Gates => "Gates (pr)",
        CheckId.Review => "Review",
        CheckId.Approvals => "Approvals",
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown check."),
    };

    /// <summary>The check whose contract id is <paramref name="name"/>; null when none is.</summary>
    public static CheckId? Parse(string name) => Enum.GetValues<CheckId>().Cast<CheckId?>().FirstOrDefault(check => ToName(check!.Value) == name);
}
