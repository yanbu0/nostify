
using System;

namespace nostify;

public class RetryOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);
    public bool RetryWhenNotFound { get; set; } = false;

    public RetryOptions()
    {
    }

    public RetryOptions(int maxRetries, TimeSpan delay, bool retryWhenNotFound)
    {
        MaxRetries = maxRetries;
        Delay = delay;
        RetryWhenNotFound = retryWhenNotFound;
    }
}