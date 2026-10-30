using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.EngineLocks;

public sealed class EnginePackageFeedTests : IDisposable
{
    private const string Version = "0.0.0-ci.7";
    private const string Rid = "linux-x64";
    private static readonly byte[] Engine = Encoding.UTF8.GetBytes("stand-in engine");
    private static readonly string EngineHash = Convert.ToHexStringLower(SHA256.HashData(Engine));

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-feed-");

    private string Feed => Path.Combine(root.FullName, "feed");

    private static EnvironmentReader NoVariables => EnvironmentReader.Of(new Dictionary<string, string>());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheEngineComesFromAFlatContainerOrAFolderOfPackagesWhenItsHashMatches(bool flatContainer)
    {
        WritePackage(Rid, flatContainer, ($"tools/any/{Rid}/lockflight", Engine), ($"tools/any/{Rid}/DotnetToolSettings.xml", "<x/>"u8.ToArray()));

        EngineFetch fetch = await new EnginePackageFeed(flatContainer ? new Uri(Feed + "/").AbsoluteUri : Feed, NoVariables)
            .FetchEngineAsync(Version, Rid, EngineHash, TestContext.Current.CancellationToken);

        Assert.Equal(EngineFetchStatus.Fetched, fetch.Status);
        Assert.Equal(Engine, fetch.Content);
    }

    [Fact]
    public async Task AnEngineWithAnotherHashIsRefused()
    {
        WritePackage(Rid, flatContainer: true, ($"tools/any/{Rid}/lockflight", Engine));

        EngineFetch fetch = await new EnginePackageFeed(Feed, NoVariables).FetchEngineAsync(Version, Rid, new string('a', 64), TestContext.Current.CancellationToken);

        Assert.Equal(EngineFetchStatus.HashMismatch, fetch.Status);
        Assert.Null(fetch.Content);
        Assert.Contains(EngineHash, fetch.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tools/../linux-x64/lockflight")]
    [InlineData("other/linux-x64/lockflight")]
    public void AnEntryOutsideToolsOrLeavingItsDirectoryIsNotTheEngine(string entry)
    {
        EngineFetch fetch = EnginePackageFeed.Extract(Zip((entry, Engine)), Rid);

        Assert.Equal(EngineFetchStatus.Invalid, fetch.Status);
    }

    [Fact]
    public void TwoExecutablesAreRefused() =>
        Assert.Equal(EngineFetchStatus.Invalid, EnginePackageFeed.Extract(Zip(($"tools/any/{Rid}/lockflight", Engine), ($"tools/net10.0/{Rid}/lockflight", Engine)), Rid).Status);

    [Fact]
    public async Task AMissingPackageIsNotFound() =>
        Assert.Equal(EngineFetchStatus.NotFound, (await new EnginePackageFeed(Feed, NoVariables).FetchEngineAsync(Version, Rid, EngineHash, TestContext.Current.CancellationToken)).Status);

    [Fact]
    public async Task TheReleaseHashesComeFromThePointerPackage()
    {
        string hashes = $$"""
            { "schema": "lockflight.engine-hashes/v1", "package": "Lockflight.Cli", "version": "{{Version}}", "commit": "{{new string('1', 40)}}",
              "binaries": { {{string.Join(", ", EngineRids.Released.Select(rid => $"\"{rid}\": {{ \"sha256\": \"{EngineHash}\", \"path\": \"tools/any/{rid}/x\" }}"))}} } }
            """;
        WritePackage(rid: null, flatContainer: true, ("engine-hashes.json", Encoding.UTF8.GetBytes(hashes)));

        (EngineHashes? read, EngineFetch fetch) = await new EnginePackageFeed(Feed, NoVariables).FetchHashesAsync(Version, TestContext.Current.CancellationToken);

        Assert.Equal(EngineFetchStatus.Fetched, fetch.Status);
        Assert.Equal((Version, EngineHash), (read!.Version, read.Binaries[Rid].Sha256));
        Assert.Equal(hashes, Encoding.UTF8.GetString(fetch.Content!));
    }

    [Fact]
    public async Task APointerWithoutHashesIsInvalid()
    {
        WritePackage(rid: null, flatContainer: true, ("README.md", "x"u8.ToArray()));

        (EngineHashes? read, EngineFetch fetch) = await new EnginePackageFeed(Feed, NoVariables).FetchHashesAsync(Version, TestContext.Current.CancellationToken);

        Assert.Null(read);
        Assert.Equal(EngineFetchStatus.Invalid, fetch.Status);
    }

    [Fact]
    public async Task AnHttpFeedIsRefused() =>
        Assert.Equal(EngineFetchStatus.Invalid, (await new EnginePackageFeed("http://example.invalid/", NoVariables).FetchEngineAsync(Version, Rid, EngineHash, TestContext.Current.CancellationToken)).Status);

    [Fact]
    public async Task AServiceIndexLeadsToItsFlatContainerAndCredentialsGoInABasicHeader()
    {
        byte[] package = Zip(($"tools/any/{Rid}/lockflight", Engine));
        RecordingHandler handler = new(request => request.RequestUri!.AbsoluteUri switch
        {
            "https://feed.example/v3/index.json" => Answer(HttpStatusCode.OK, Encoding.UTF8.GetBytes(
                """{ "resources": [ { "@id": "https://feed.example/search", "@type": "SearchQueryService" }, { "@id": "https://feed.example/flat", "@type": "PackageBaseAddress/3.0.0" } ] }""")),
            $"https://feed.example/flat/lockflight.cli.{Rid}/{Version}/lockflight.cli.{Rid}.{Version}.nupkg" => Answer(HttpStatusCode.OK, package),
            _ => Answer(HttpStatusCode.NotFound, []),
        });
        EnvironmentReader environment = EnvironmentReader.Of(new Dictionary<string, string> { [EnginePackageFeed.UserVariable] = "alice", [EnginePackageFeed.TokenVariable] = "s3cret" });

        EngineFetch fetch = await new EnginePackageFeed("https://feed.example/v3/index.json", environment, handler).FetchEngineAsync(Version, Rid, EngineHash, TestContext.Current.CancellationToken);

        Assert.Equal(EngineFetchStatus.Fetched, fetch.Status);
        Assert.All(handler.Authorizations, header => Assert.Equal("Basic " + Convert.ToBase64String("alice:s3cret"u8.ToArray()), header));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "Unauthorized")]
    [InlineData(HttpStatusCode.InternalServerError, "Unreachable")]
    public async Task AFeedThatRefusesTheRequestSaysWhy(HttpStatusCode code, string expected)
    {
        RecordingHandler handler = new(_ => Answer(code, []));

        EngineFetch fetch = await new EnginePackageFeed("https://feed.example/flat/", NoVariables, handler).FetchEngineAsync(Version, Rid, EngineHash, TestContext.Current.CancellationToken);

        Assert.Equal(Enum.Parse<EngineFetchStatus>(expected), fetch.Status);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static HttpResponseMessage Answer(HttpStatusCode code, byte[] body) => new(code) { Content = new ByteArrayContent(body) };

    private void WritePackage(string? rid, bool flatContainer, params (string Entry, byte[] Content)[] entries)
    {
        string id = rid is null ? "lockflight.cli" : $"lockflight.cli.{rid}";
        string path = flatContainer
            ? Path.Combine(Feed, id, Version, $"{id}.{Version}.nupkg")
            : Path.Combine(Feed, rid is null ? $"Lockflight.Cli.{Version}.nupkg" : $"Lockflight.Cli.{rid}.{Version}.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Zip(entries));
    }

    private static byte[] Zip(params (string Entry, byte[] Content)[] entries)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string entry, byte[] content) in entries)
            {
                using Stream written = archive.CreateEntry(entry).Open();
                written.Write(content);
            }
        }

        return stream.ToArray();
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(answer(request));
        }
    }
}
