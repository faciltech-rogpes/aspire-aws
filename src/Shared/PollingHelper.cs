
namespace Shared;

public static class PollingHelper
{
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        string? failureMessage = null)
    {
        timeout ??= TimeSpan.FromSeconds(120);
        interval ??= TimeSpan.FromMilliseconds(500);

        var deadline = DateTimeOffset.UtcNow.Add(timeout.Value);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(interval.Value).ConfigureAwait(false);
        }

        throw new TimeoutException(
            failureMessage ?? $"Condition was not met within {timeout.Value.TotalSeconds:F0}s.");
    }

    public static async Task AssertNeverAsync(
        Func<Task<bool>> condition,
        TimeSpan? duration = null,
        TimeSpan? interval = null,
        string? failureMessage = null)
    {
        duration ??= TimeSpan.FromSeconds(5);
        interval ??= TimeSpan.FromMilliseconds(500);

        var deadline = DateTimeOffset.UtcNow.Add(duration.Value);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                throw new Exception(
                    failureMessage ?? "Condition became true when it should have remained false.");
            }

            await Task.Delay(interval.Value).ConfigureAwait(false);
        }
    }
}
