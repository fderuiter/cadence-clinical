using System;

namespace Cadence.Apps.Fileshare.Workers;

public sealed class FileshareWorkerOptions
{
    public TimeSpan TranscodePollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ConversionPollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 10;
    public int MaxConcurrentJobs { get; set; } = 2;
    public int MaxRetryCount { get; set; } = 3;
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);
}
