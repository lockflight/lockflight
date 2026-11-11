using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class FindingDismissalsTests : IDisposable
{
    private readonly DirectoryInfo record = Directory.CreateTempSubdirectory("lockflight-dismissals-");

    [Theory]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("{ \"finding\": ")]
    public void AFileThatIsNotADismissalClosesNothing(string content)
    {
        string directory = Directory.CreateDirectory(Path.Combine(ReviewEvidence.DirectoryOf(record.FullName), "dismissals")).FullName;
        File.WriteAllText(Path.Combine(directory, "planted.json"), content);

        Assert.Empty(FindingDismissals.Read(record.FullName));
    }

    public void Dispose() => record.Delete(recursive: true);
}
