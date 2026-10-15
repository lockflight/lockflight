namespace Billing;

public static class ChargePolicy
{
    public const int MaxAttempts = 3;

    public static bool ShouldRetry(int failedAttempts) => failedAttempts < MaxAttempts;
}
