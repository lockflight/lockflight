namespace Lockflight.Cli.Hooks.Adapters;

/// <summary>What an adapter made of a hook payload.</summary>
internal enum HookAdapterStatus
{
    /// <summary>The payload was translated into a request.</summary>
    Parsed = 1,

    /// <summary>The payload is not valid for the adapter (not JSON, not an object, or not the neutral shape for this event).</summary>
    InvalidPayload = 2,

    /// <summary><c>LOCKFLIGHT_HOOK_ADAPTER</c> names an adapter this engine does not know.</summary>
    UnknownAdapter = 3,
}
