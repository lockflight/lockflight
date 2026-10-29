using Lockflight.Cli.Hooks.Requests;

namespace Lockflight.Cli.Hooks.Adapters;

/// <summary>The outcome of translating a payload: the <see cref="Request"/> when <see cref="HookAdapterStatus.Parsed"/>, else why not.</summary>
internal sealed record HookAdapterResult(HookAdapterStatus Status, HookRequest? Request, string? Message)
{
    public static HookAdapterResult Of(HookRequest? request) =>
        request is null ? new(HookAdapterStatus.InvalidPayload, null, null) : new(HookAdapterStatus.Parsed, request, null);
}
