using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;

using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

public static class PortCheckerService
{
    /// <summary>The gateway and every service (from the topology manifest), web frontends, PostgreSQL and Keycloak.</summary>
    public static IReadOnlyList<int> DefaultPorts { get; } =
        [ServiceTopology.GatewayPort, .. ServiceTopology.Services.Select(s => s.Port), 3000, 5174, 5432, 8080];

    /// <summary>Formats a set of ports into sorted, human-readable contiguous ranges (for example <c>3000, 5174, 5432, 8000-8016, 8018 and 8080</c>).</summary>
    public static string FormatPortRanges(IEnumerable<int> ports)
    {
        int[] sorted = ports.Distinct().Order().ToArray();
        if (sorted.Length == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        int start = sorted[0];
        int prev = sorted[0];
        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i] == prev + 1)
            {
                prev = sorted[i];
                continue;
            }

            parts.Add(FormatRange(start, prev));
            start = prev = sorted[i];
        }

        parts.Add(FormatRange(start, prev));
        return parts.Count switch
        {
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            _ => $"{string.Join(", ", parts[..^1])} and {parts[^1]}",
        };
    }

    public static int CheckPorts(int[]? ports = null)
    {
        int[] targets = ports ?? [.. DefaultPorts];
        Console.WriteLine("Scanning local network ports for active Cadence microservice bindings...");

        foreach (int port in targets)
        {
            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect("127.0.0.1", port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(200));
                if (success)
                {
                    client.EndConnect(result);
                    Console.WriteLine($"  [BOUND] Port {port} is ACTIVE / IN USE");
                }
                else
                {
                    Console.WriteLine($"  [FREE] Port {port} is OPEN / UNBOUND");
                }
            }
            catch
            {
                Console.WriteLine($"  [FREE] Port {port} is OPEN / UNBOUND");
            }
        }

        Console.WriteLine("✔ Network port inspection complete.");
        return 0;
    }

    private static string FormatRange(int start, int end) =>
        start == end
            ? start.ToString(CultureInfo.InvariantCulture)
            : $"{start.ToString(CultureInfo.InvariantCulture)}-{end.ToString(CultureInfo.InvariantCulture)}";
}
