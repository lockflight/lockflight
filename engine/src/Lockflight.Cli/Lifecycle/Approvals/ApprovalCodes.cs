using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// One-time codes for approving inside a Claude Code session, one per phase, in
/// <c>.lockflight/local/session-approvals/&lt;phase&gt;.json</c> (git-ignored, protected). A code binds the feature and the artifact
/// hashes the person was shown with it. Only a slow salted hash of the code is stored: the agent can read the file, and a fast hash
/// of a short code could be reversed within its lifetime.
/// </summary>
internal sealed class ApprovalCodes(string root, TimeProvider time, RandomSource random)
{
    public const string RelativeDirectory = ".lockflight/local/session-approvals";

    /// <summary>Lower-case letters and digits without the look-alikes 0/o, 1/l/i.</summary>
    public const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public const int CodeLength = 6;

    private const string Schema = "lockflight.session-approval/v1";
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const int Iterations = 100_000;

    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(10);

    /// <summary>How far the clock may step back (a time sync) before a waiting code counts as issued in the future.</summary>
    private static TimeSpan ClockStepAllowance { get; } = TimeSpan.FromMinutes(1);

    public string PathOf(ApprovalPhase phase) => Path.Combine(root, RelativeDirectory, $"{ApprovalPhaseNames.ToName(phase)}.json");

    /// <summary>
    /// Why the phase's waiting code cannot be kept safely: a part of <see cref="RelativeDirectory"/> or the code's own file is a
    /// symbolic link, a file where a directory belongs, or a directory where the file belongs. Null when it can.
    /// </summary>
    public string? StateProblem(ApprovalPhase phase)
    {
        if (ProjectDirectories.Problem(root, RelativeDirectory) is string problem)
        {
            return problem;
        }

        string path = PathOf(phase);
        string shown = $"{RelativeDirectory}/{Path.GetFileName(path)}";
        return new FileInfo(path).LinkTarget is not null ? $"{shown} is a symbolic link"
            : Directory.Exists(path) ? $"{shown} is a directory, not a file"
            : null;
    }

    /// <summary>Issues a new code for the phase, replacing any code waiting for it; returns the code to show the person.</summary>
    public string Issue(string featureId, ApprovalPhase phase, IReadOnlyList<(string Name, string Hash)> artifacts)
    {
        string code = random.Code(Alphabet, CodeLength);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        JsonObject hashes = [];
        foreach ((string name, string hash) in artifacts)
        {
            hashes[name] = hash;
        }

        Write(PathOf(phase), new JsonObject
        {
            ["schema"] = Schema,
            ["feature"] = featureId,
            ["phase"] = ApprovalPhaseNames.ToName(phase),
            ["artifacts"] = hashes,
            ["salt"] = Convert.ToHexStringLower(salt),
            ["codeHash"] = Convert.ToHexStringLower(HashOf(code, salt)),
            ["expiresAt"] = (time.GetUtcNow() + Lifetime).ToString("O", CultureInfo.InvariantCulture),
        });
        return code;
    }

    /// <summary>
    /// Checks a typed code (ignoring case and surrounding space). An unreadable entry, or one written for another phase, is
    /// discarded as if none were waiting; an expired one, or one that expires further ahead than a code lives (the clock went
    /// back by more than a time sync would), is discarded as expired.
    /// </summary>
    public ApprovalCodeCheck Check(ApprovalPhase phase, string typedCode)
    {
        string path = PathOf(phase);
        if (!File.Exists(path))
        {
            return ApprovalCodeCheck.Of(ApprovalCodeState.NoneWaiting);
        }

        if (Read(path, phase) is not WaitingCode waiting)
        {
            Discard(phase);
            return ApprovalCodeCheck.Of(ApprovalCodeState.NoneWaiting);
        }

        DateTimeOffset now = time.GetUtcNow();
        if (now >= waiting.ExpiresAt || waiting.ExpiresAt - now > Lifetime + ClockStepAllowance)
        {
            Discard(phase);
            return ApprovalCodeCheck.Of(ApprovalCodeState.Expired);
        }

        byte[] typedHash = HashOf(typedCode.Trim().ToLowerInvariant(), waiting.Salt);
        return CryptographicOperations.FixedTimeEquals(typedHash, waiting.CodeHash)
            ? new ApprovalCodeCheck(ApprovalCodeState.Matched, waiting.FeatureId, waiting.Artifacts)
            : ApprovalCodeCheck.Of(ApprovalCodeState.WrongCode);
    }

    /// <summary>Removes the phase's waiting code: once used, or when what it was shown for has changed.</summary>
    public void Discard(ApprovalPhase phase) => File.Delete(PathOf(phase));

    private static byte[] HashOf(string code, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, Iterations, HashAlgorithmName.SHA256, HashLength);

    /// <summary>Writes a new entry; whatever was at the path is removed first, so the write never follows a link left there.</summary>
    private static void Write(string path, JsonObject entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Delete(path);
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write);
        file.Write(Encoding.UTF8.GetBytes(JsonFiles.Format(entry)));
    }

    private static WaitingCode? Read(string path, ApprovalPhase phase)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject entry
                || Text(entry["schema"]) != Schema
                || Text(entry["phase"]) != ApprovalPhaseNames.ToName(phase)
                || Text(entry["feature"]) is not string feature
                || !FeatureLocator.IsPlainId(feature)
                || entry["artifacts"] is not JsonObject artifacts
                || Text(entry["salt"]) is not string salt
                || Text(entry["codeHash"]) is not string codeHash
                || !DateTimeOffset.TryParse(Text(entry["expiresAt"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expiresAt))
            {
                return null;
            }

            (string Name, string Hash)[] hashes = [.. artifacts.Select(artifact => (artifact.Key, Text(artifact.Value) ?? string.Empty))];
            return new WaitingCode(feature, hashes, Convert.FromHexString(salt), Convert.FromHexString(codeHash), expiresAt);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private sealed record WaitingCode(string FeatureId, IReadOnlyList<(string Name, string Hash)> Artifacts, byte[] Salt, byte[] CodeHash, DateTimeOffset ExpiresAt);
}
