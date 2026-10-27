using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// One-time codes for the human decisions a person gives inside a Claude Code session, one per slot (an approval phase,
/// <c>baseline-build</c>, <c>reset</c>, …), in <c>.lockflight/local/session-approvals/&lt;slot&gt;.json</c> (git-ignored, protected).
/// A code binds what the person was shown with it: the subject (a feature, a unit), named hashes of the state, and the entries a
/// baseline would accept. Only a slow salted hash of the code is stored: the agent can read the file, and a fast hash of a short
/// code could be reversed within its lifetime. A code survives <see cref="MaxAttempts"/> wrong codes at most.
/// </summary>
internal sealed class SessionCodes(string root, TimeProvider time, RandomSource random)
{
    public const string RelativeDirectory = ".lockflight/local/session-approvals";

    /// <summary>Lower-case letters and digits without the look-alikes 0/o, 1/l/i.</summary>
    public const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public const int CodeLength = 6;

    /// <summary>Wrong codes typed for a slot after which its waiting code is discarded.</summary>
    public const int MaxAttempts = 5;

    private const string Schema = "lockflight.session-code/v1";
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const int Iterations = 100_000;

    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(10);

    /// <summary>How far the clock may step back (a time sync) before a waiting code counts as issued in the future.</summary>
    private static TimeSpan ClockStepAllowance { get; } = TimeSpan.FromMinutes(1);

    public string PathOf(string slot) => Path.Combine(root, RelativeDirectory, $"{slot}.json");

    /// <summary>
    /// Why the slot's waiting code cannot be kept safely: a part of <see cref="RelativeDirectory"/> or the code's own file is a
    /// symbolic link, a file where a directory belongs, or a directory where the file belongs. Null when it can.
    /// </summary>
    public string? StateProblem(string slot)
    {
        if (ProjectDirectories.Problem(root, RelativeDirectory) is string problem)
        {
            return problem;
        }

        string path = PathOf(slot);
        string shown = $"{RelativeDirectory}/{Path.GetFileName(path)}";
        return new FileInfo(path).LinkTarget is not null ? $"{shown} is a symbolic link"
            : Directory.Exists(path) ? $"{shown} is a directory, not a file"
            : null;
    }

    /// <summary>Issues a new code for the slot, replacing any code waiting for it; returns the code to show the person.</summary>
    public string Issue(string slot, string subject, IReadOnlyList<(string Name, string Hash)> bound) => Issue(slot, subject, bound, []);

    /// <param name="payload">Entries the decision would record, kept with the code so the second step records exactly them.</param>
    public string Issue(string slot, string subject, IReadOnlyList<(string Name, string Hash)> bound, IReadOnlyList<string> payload)
    {
        string code = random.Code(Alphabet, CodeLength);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        JsonObject hashes = [];
        foreach ((string name, string hash) in bound)
        {
            hashes[name] = hash;
        }

        // Added one by one through the non-generic Add(JsonNode): the generic overload a collection expression picks is not AOT-safe.
        JsonArray entries = [];
        foreach (string entry in payload)
        {
            entries.Add((JsonNode)entry);
        }

        Write(PathOf(slot), new JsonObject
        {
            ["schema"] = Schema,
            ["command"] = slot,
            ["subject"] = subject,
            ["bound"] = hashes,
            ["payload"] = entries,
            ["salt"] = Convert.ToHexStringLower(salt),
            ["codeHash"] = Convert.ToHexStringLower(HashOf(code, salt)),
            ["expiresAt"] = (time.GetUtcNow() + Lifetime).ToString("O", CultureInfo.InvariantCulture),
        });
        return code;
    }

    /// <summary>
    /// Checks a typed code (ignoring case and surrounding space). An unreadable entry, one written for another slot, or one whose
    /// subject <paramref name="isValidSubject"/> rejects, is discarded as if none were waiting; an expired one, or one that expires further ahead than a code lives (the clock went
    /// back by more than a time sync would), is discarded as expired. A wrong code is counted; the last allowed one discards the
    /// waiting code.
    /// </summary>
    public SessionCodeCheck Check(string slot, string typedCode, Predicate<string> isValidSubject)
    {
        string path = PathOf(slot);
        if (!File.Exists(path))
        {
            return SessionCodeCheck.Of(SessionCodeState.NoneWaiting);
        }

        if (Read(path, slot) is not WaitingCode waiting || !isValidSubject(waiting.Subject))
        {
            Discard(slot);
            return SessionCodeCheck.Of(SessionCodeState.NoneWaiting);
        }

        DateTimeOffset now = time.GetUtcNow();
        if (now >= waiting.ExpiresAt || waiting.ExpiresAt - now > Lifetime + ClockStepAllowance)
        {
            Discard(slot);
            return SessionCodeCheck.Of(SessionCodeState.Expired);
        }

        byte[] typedHash = HashOf(typedCode.Trim().ToLowerInvariant(), waiting.Salt);
        if (CryptographicOperations.FixedTimeEquals(typedHash, waiting.CodeHash))
        {
            return new SessionCodeCheck(SessionCodeState.Matched, waiting.Subject, waiting.Bound, waiting.Payload);
        }

        if (waiting.Attempts + 1 >= MaxAttempts)
        {
            Discard(slot);
            return SessionCodeCheck.Of(SessionCodeState.TooManyAttempts);
        }

        waiting.Entry["attempts"] = waiting.Attempts + 1;
        Write(path, waiting.Entry);
        return SessionCodeCheck.Of(SessionCodeState.WrongCode);
    }

    /// <summary>
    /// The slot's waiting code, if one is valid now, without checking or counting a typed code: so an incomplete second step can
    /// be told which line to type. Null when none is waiting, it cannot be read, or it expired.
    /// </summary>
    public SessionCodeWaiting? Waiting(string slot, Predicate<string> isValidSubject)
    {
        string path = PathOf(slot);
        if (StateProblem(slot) is not null || !File.Exists(path) || Read(path, slot) is not WaitingCode waiting || !isValidSubject(waiting.Subject))
        {
            return null;
        }

        DateTimeOffset now = time.GetUtcNow();
        return now >= waiting.ExpiresAt || waiting.ExpiresAt - now > Lifetime + ClockStepAllowance
            ? null
            : new SessionCodeWaiting(waiting.Subject, waiting.Payload, waiting.ExpiresAt);
    }

    /// <summary>Removes the slot's waiting code: once used, or when what it was shown for has changed.</summary>
    public void Discard(string slot) => File.Delete(PathOf(slot));

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

    private static WaitingCode? Read(string path, string slot)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject entry
                || Text(entry["schema"]) != Schema
                || Text(entry["command"]) != slot
                || Text(entry["subject"]) is not string subject
                || entry["bound"] is not JsonObject bound
                || entry["payload"] is not JsonArray payload
                || payload.Any(item => Text(item) is null)
                || Text(entry["salt"]) is not string salt
                || Text(entry["codeHash"]) is not string codeHash
                || !DateTimeOffset.TryParse(Text(entry["expiresAt"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expiresAt))
            {
                return null;
            }

            (string Name, string Hash)[] hashes = [.. bound.Select(item => (item.Key, Text(item.Value) ?? string.Empty))];
            string[] entries = [.. payload.Select(item => Text(item)!)];
            int attempts = entry["attempts"] is JsonValue counted && counted.TryGetValue(out int count) ? count : 0;
            return new WaitingCode(entry, subject, hashes, entries, Convert.FromHexString(salt), Convert.FromHexString(codeHash), expiresAt, attempts);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private sealed record WaitingCode(
        JsonObject Entry,
        string Subject,
        IReadOnlyList<(string Name, string Hash)> Bound,
        IReadOnlyList<string> Payload,
        byte[] Salt,
        byte[] CodeHash,
        DateTimeOffset ExpiresAt,
        int Attempts);
}
