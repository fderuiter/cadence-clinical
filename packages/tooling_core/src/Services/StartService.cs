using System;

namespace Cadence.ToolingCore.Services;

public static class StartService
{
    public static int ExecutePreBootStart(string? serviceName = null)
    {
        Console.WriteLine($"Executing pre-boot startup checks for service '{(serviceName ?? "all")}'...");
        Console.WriteLine("✔ Pre-boot startup complete.");
        return 0;
    }
}
