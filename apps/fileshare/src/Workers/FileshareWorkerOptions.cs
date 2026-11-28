using System;
using System.ComponentModel.DataAnnotations;

namespace Cadence.Apps.Fileshare.Workers;

public sealed class FileshareWorkerOptions
{
    public const string SectionName = "FileshareWorkers";

    public TimeSpan TranscodePollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ConversionPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    [Range(1, int.MaxValue)]
    public int BatchSize { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int MaxConcurrentJobs { get; set; } = 2;

    [Range(1, int.MaxValue)]
    public int MaxRetryCount { get; set; } = 3;

    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);
}
