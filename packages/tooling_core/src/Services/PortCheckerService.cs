using System;
using System.Net.Sockets;

namespace Cadence.ToolingCore.Services;

public static class PortCheckerService
{
    private static readonly int[] DefaultPorts = [8000, 8001, 8002, 8004, 8005, 8006, 8007, 8008, 8009, 8010, 8011, 8012, 8013, 8014, 8015, 8016, 8017, 8018, 8019, 5432, 7687, 8080];

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
