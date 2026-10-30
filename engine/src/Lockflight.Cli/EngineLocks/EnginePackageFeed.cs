using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// Fetches the engine from a NuGet feed without the .NET SDK, the same way <c>install.sh</c> does: the RID package from the feed's
/// flat container (nuget.org's by default; a flat-container base, a v3 <c>index.json</c>, or a local folder of packages), the
/// executable extracted from it and checked against the expected hash before it is returned. Feed credentials come from
/// <c>LOCKFLIGHT_NUGET_USER</c> and <c>LOCKFLIGHT_NUGET_TOKEN</c>. Only <c>https://</c> feeds, <c>file://</c> and local folders.
/// </summary>
internal sealed class EnginePackageFeed(string source, EnvironmentReader environment, HttpMessageHandler? handler = null)
{
    public const string NuGetFlatContainer = "https://api.nuget.org/v3-flatcontainer/";
    public const string FeedVariable = "LOCKFLIGHT_NUGET_FEED";
    public const string UserVariable = "LOCKFLIGHT_NUGET_USER";
    public const string TokenVariable = "LOCKFLIGHT_NUGET_TOKEN";

    private const string PackageBaseAddress = "PackageBaseAddress/3.0.0";
    private const string PointerId = "lockflight.cli";
    private static readonly TimeSpan RequestTimeLimit = TimeSpan.FromMinutes(5);

    /// <summary>The feed the environment names (<c>LOCKFLIGHT_NUGET_FEED</c>), else nuget.org.</summary>
    public static EnginePackageFeed FromEnvironment(EnvironmentReader environment) =>
        new(environment.Get(FeedVariable) is { Length: > 0 } feed ? feed : NuGetFlatContainer, environment);

    /// <summary>The executable of <paramref name="rid"/> at <paramref name="version"/>, only when its SHA-256 is <paramref name="expectedSha256"/>.</summary>
    public async Task<EngineFetch> FetchEngineAsync(string version, string rid, string expectedSha256, CancellationToken cancellationToken)
    {
        EngineFetch package = await FetchPackageAsync($"{PointerId}.{rid}", version, cancellationToken);
        if (package.Content is not byte[] bytes)
        {
            return package;
        }

        EngineFetch executable = Extract(bytes, rid);
        if (executable.Content is not byte[] engine)
        {
            return executable;
        }

        string actual = Convert.ToHexStringLower(SHA256.HashData(engine));
        return actual == expectedSha256
            ? executable
            : EngineFetch.Failed(EngineFetchStatus.HashMismatch, $"the {rid} engine of {version} is {actual}, the lock says {expectedSha256}");
    }

    /// <summary>The release's <c>engine-hashes.json</c> from the pointer package of <paramref name="version"/>, read and checked.</summary>
    public async Task<(EngineHashes? Hashes, EngineFetch Fetch)> FetchHashesAsync(string version, CancellationToken cancellationToken)
    {
        EngineFetch package = await FetchPackageAsync(PointerId, version, cancellationToken);
        if (package.Content is not byte[] bytes)
        {
            return (null, package);
        }

        using ZipArchive archive = new(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (archive.GetEntry(EngineHashes.FileName) is not ZipArchiveEntry entry)
        {
            return (null, EngineFetch.Failed(EngineFetchStatus.Invalid, $"Lockflight.Cli {version} has no {EngineHashes.FileName} (released before the engine lock?)"));
        }

        byte[] content = ReadEntry(entry);
        if (EngineHashesReader.Read(Encoding.UTF8.GetString(content), out string? problem) is not EngineHashes hashes)
        {
            return (null, EngineFetch.Failed(EngineFetchStatus.Invalid, $"{EngineHashes.FileName} of {version}: {problem}"));
        }

        return hashes.Version.Equals(version, StringComparison.OrdinalIgnoreCase)
            ? (hashes, new EngineFetch(EngineFetchStatus.Fetched, content, $"{EngineHashes.FileName} of {version}"))
            : (null, EngineFetch.Failed(EngineFetchStatus.Invalid, $"{EngineHashes.FileName} of {version} describes {hashes.Version}"));
    }

    /// <summary>The one <c>tools/&lt;tfm&gt;/&lt;rid&gt;/lockflight</c> entry of a RID package; never one that leaves its directory.</summary>
    public static EngineFetch Extract(byte[] package, string rid)
    {
        try
        {
            using ZipArchive archive = new(new MemoryStream(package), ZipArchiveMode.Read);
            Regex executable = new($"^tools/[^/]+/{Regex.Escape(rid)}/{Regex.Escape(EngineRids.ExecutableName(rid))}$", RegexOptions.CultureInvariant);
            ZipArchiveEntry[] matches = [.. archive.Entries.Where(entry => executable.IsMatch(entry.FullName))];
            if (matches.Length != 1)
            {
                return EngineFetch.Failed(EngineFetchStatus.Invalid, $"the {rid} package holds {matches.Length} tools/<tfm>/{rid}/{EngineRids.ExecutableName(rid)} entries, expected one");
            }

            string name = matches[0].FullName;
            return name.Split('/').Contains("..", StringComparer.Ordinal) || name.StartsWith('/')
                ? EngineFetch.Failed(EngineFetchStatus.Invalid, $"the {rid} package's entry {name} leaves its directory")
                : new EngineFetch(EngineFetchStatus.Fetched, ReadEntry(matches[0]), name);
        }
        catch (InvalidDataException exception)
        {
            return EngineFetch.Failed(EngineFetchStatus.Invalid, $"the {rid} package is not a valid package: {exception.Message}");
        }
    }

    private async Task<EngineFetch> FetchPackageAsync(string id, string version, CancellationToken cancellationToken)
    {
        string lowerVersion = version.ToLowerInvariant();
        if (LocalFolder() is string folder)
        {
            return ReadLocalPackage(folder, id, lowerVersion);
        }

        if (!source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return EngineFetch.Failed(EngineFetchStatus.Invalid, $"refusing the feed {source}: only https:// feeds, file:// and local folders");
        }

        EngineFetch baseAddress = await FlatContainerAsync(cancellationToken);
        return baseAddress.Status != EngineFetchStatus.Fetched
            ? baseAddress
            : await DownloadAsync($"{baseAddress.Message}{id}/{lowerVersion}/{id}.{lowerVersion}.nupkg", cancellationToken);
    }

    /// <summary>The flat-container base address; for a service index, its <c>PackageBaseAddress/3.0.0</c>. The address is the result's message.</summary>
    private async Task<EngineFetch> FlatContainerAsync(CancellationToken cancellationToken)
    {
        if (!source.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
        {
            return new EngineFetch(EngineFetchStatus.Fetched, null, source.EndsWith('/') ? source : source + "/");
        }

        EngineFetch index = await DownloadAsync(source, cancellationToken);
        if (index.Content is not byte[] content)
        {
            return index;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            foreach (JsonElement resource in document.RootElement.GetProperty("resources").EnumerateArray())
            {
                if (resource.TryGetProperty("@type", out JsonElement type) && type.GetString() == PackageBaseAddress
                    && resource.TryGetProperty("@id", out JsonElement id) && id.GetString() is string address
                    && address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return new EngineFetch(EngineFetchStatus.Fetched, null, address.EndsWith('/') ? address : address + "/");
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return EngineFetch.Failed(EngineFetchStatus.Invalid, $"the service index {source} is not readable: {exception.Message}");
        }

        return EngineFetch.Failed(EngineFetchStatus.Invalid, $"the service index {source} has no https {PackageBaseAddress}");
    }

    private async Task<EngineFetch> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using HttpClient client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = RequestTimeLimit;
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (environment.Get(UserVariable) is { Length: > 0 } user && environment.Get(TokenVariable) is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{token}")));
        }

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => new EngineFetch(EngineFetchStatus.Fetched, await response.Content.ReadAsByteArrayAsync(cancellationToken), url),
                HttpStatusCode.NotFound => EngineFetch.Failed(EngineFetchStatus.NotFound, $"{url}: not found"),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => EngineFetch.Failed(EngineFetchStatus.Unauthorized, $"{url}: HTTP {(int)response.StatusCode}"),
                _ => EngineFetch.Failed(EngineFetchStatus.Unreachable, $"{url}: HTTP {(int)response.StatusCode}"),
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return EngineFetch.Failed(EngineFetchStatus.Unreachable, $"{url}: {exception.Message}");
        }
    }

    /// <summary>The folder of a <c>file://</c> or local-path source; null for a remote feed.</summary>
    private string? LocalFolder()
    {
        if (source.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(source).LocalPath;
        }

        return Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" ? null : source;
    }

    /// <summary>A package in a local folder: a flat container (<c>id/version/id.version.nupkg</c>) or a folder of packages.</summary>
    private static EngineFetch ReadLocalPackage(string folder, string id, string lowerVersion)
    {
        string[] candidates =
        [
            Path.Combine(folder, id, lowerVersion, $"{id}.{lowerVersion}.nupkg"),
            .. Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.nupkg").Where(path => Path.GetFileName(path).Equals($"{id}.{lowerVersion}.nupkg", StringComparison.OrdinalIgnoreCase))
                : [],
        ];
        return candidates.FirstOrDefault(File.Exists) is string path
            ? new EngineFetch(EngineFetchStatus.Fetched, File.ReadAllBytes(path), path)
            : EngineFetch.Failed(EngineFetchStatus.NotFound, $"{folder}: no {id} {lowerVersion}");
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
