using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

public sealed class SessionCodesTests : IDisposable
{
    private const string Slot = "reset";
    private const string Subject = "001-orders";
    private const string Code = "k7m2qx";
    private const string WrongCode = "aaaaaa";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-session-codes-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly SessionCodes codes;

    public SessionCodesTests() => codes = new SessionCodes(root.FullName, time, RandomSource.WithCodes(Code, "second"));

    [Fact]
    public void TheRightCodeMatchesWithWhatItWasShownFor()
    {
        codes.Issue(Slot, Subject, [("redgreen.json", "abc")], ["entry"]);

        SessionCodeCheck check = codes.Check(Slot, "  K7M2QX ", AnySubject);

        Assert.Equal(SessionCodeState.Matched, check.State);
        Assert.Equal(Subject, check.Subject);
        Assert.Equal([("redgreen.json", "abc")], check.Bound);
        Assert.Equal(["entry"], check.Payload);
    }

    [Fact]
    public void FourWrongCodesCountDownAndTheFifthDiscardsTheWaitingCode()
    {
        codes.Issue(Slot, Subject, []);

        SessionCodeCheck[] wrong = [.. Enumerable.Range(0, SessionCodes.MaxAttempts - 1).Select(_ => codes.Check(Slot, WrongCode, AnySubject))];
        SessionCodeCheck fifth = codes.Check(Slot, WrongCode, AnySubject);
        SessionCodeCheck afterwards = codes.Check(Slot, Code, AnySubject);

        Assert.All(wrong, check => Assert.Equal(SessionCodeState.WrongCode, check.State));
        Assert.Equal([4, 3, 2, 1], wrong.Select(check => check.AttemptsLeft));
        Assert.Equal(SessionCodeState.TooManyAttempts, fifth.State);
        Assert.Equal(SessionCodeState.NoneWaiting, afterwards.State);
        Assert.False(File.Exists(codes.PathOf(Slot)));
    }

    [Fact]
    public void AWrongCodeDoesNotDiscardTheRightOne()
    {
        codes.Issue(Slot, Subject, []);

        codes.Check(Slot, WrongCode, AnySubject);

        Assert.Equal(SessionCodeState.Matched, codes.Check(Slot, Code, AnySubject).State);
    }

    [Fact]
    public void ACodeExpiresAfterItsLifetime()
    {
        codes.Issue(Slot, Subject, []);
        time.Advance(SessionCodes.Lifetime);

        SessionCodeCheck check = codes.Check(Slot, Code, AnySubject);

        Assert.Equal(SessionCodeState.Expired, check.State);
        Assert.False(File.Exists(codes.PathOf(Slot)));
    }

    [Fact]
    public void ASmallStepBackOfTheClockKeepsTheCode()
    {
        codes.Issue(Slot, Subject, []);
        time.Advance(TimeSpan.FromSeconds(-30));

        Assert.Equal(SessionCodeState.Matched, codes.Check(Slot, Code, AnySubject).State);
    }

    [Fact]
    public void ACodeThatExpiresFurtherAheadThanACodeLivesIsDiscardedAsExpired()
    {
        codes.Issue(Slot, Subject, []);
        time.Advance(TimeSpan.FromMinutes(-2));

        SessionCodeCheck check = codes.Check(Slot, Code, AnySubject);

        Assert.Equal(SessionCodeState.Expired, check.State);
        Assert.False(File.Exists(codes.PathOf(Slot)));
    }

    [Fact]
    public void AForwardDatedExpiryWrittenIntoTheFileIsDiscardedAsExpired()
    {
        codes.Issue(Slot, Subject, []);
        JsonObject entry = (JsonObject)JsonNode.Parse(File.ReadAllText(codes.PathOf(Slot)))!;
        entry["expiresAt"] = time.GetUtcNow().AddDays(1).ToString("O");
        File.WriteAllText(codes.PathOf(Slot), entry.ToJsonString());

        Assert.Equal(SessionCodeState.Expired, codes.Check(Slot, Code, AnySubject).State);
    }

    [Fact]
    public void AWaitingCodeWhoseSubjectIsRejectedIsDiscardedAsIfNoneWereWaiting()
    {
        codes.Issue(Slot, "../outside", []);

        SessionCodeCheck check = codes.Check(Slot, Code, subject => subject == Subject);

        Assert.Equal(SessionCodeState.NoneWaiting, check.State);
        Assert.False(File.Exists(codes.PathOf(Slot)));
    }

    [Fact]
    public void ACodeWrittenForAnotherSlotIsNotAcceptedForThisOne()
    {
        codes.Issue("amend", Subject, []);
        File.Copy(codes.PathOf("amend"), codes.PathOf(Slot));

        Assert.Equal(SessionCodeState.NoneWaiting, codes.Check(Slot, Code, AnySubject).State);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static bool AnySubject(string subject) => true;
}
