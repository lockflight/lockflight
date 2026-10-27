using System.Text.Json;

namespace Lockflight.Cli.Review;

/// <summary>A run file read far enough to order it: its id, when it was recorded, its number within that second, its schema and its JSON.</summary>
internal sealed record StoredRun(string Id, DateTimeOffset At, int Sequence, string Schema, JsonElement Root);
