using System;
using System.Linq;
using System.Net.Sockets;

using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

public static class PortCheckerService
{
    /// <summary>The gateway and every service (from the topology manifest), PostgreSQL and Keycloak.</summary>
    private static readonly int[] DefaultPorts =
        [ServiceTopology.GatewayPort, .. ServiceTopology.Services.Select(s => s.Port), 5432, 8080];

    public static int CheckPorts(int[]? ports = null)
    {
        ports ??= DefaultPorts;
        Console.WriteLine("Scanning local network ports for active Cadence microservice bindings...");

        foreach (int port in ports)
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
}
