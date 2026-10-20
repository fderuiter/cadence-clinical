using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Evaluation;

namespace Cadence.Modules.Intelligence.Application;

/// <summary>
/// Suspends a configuration version through <see cref="SuspendTaskConfiguration"/>, as the person or service the
/// command pipeline resolves for the current request, so the suspension is authorized and audited like any command.
/// </summary>
internal sealed class TaskConfigurationSuspension(IKernelCommandSender sender) : ITaskConfigurationSuspension
{
    public Task<CommandResult<TaskQualificationState>> SuspendAsync(
        TenantId tenant, ConfigurationRef configuration, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return sender.SendAsync(
            new SuspendTaskConfiguration(
                tenant,
                configuration.Key,
                configuration.Version,
                $"intelligence:suspend:{configuration.Key}:v{configuration.Version}:{configuration.ContentHash}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reason)))[..16]}",
                reason),
            cancellationToken: cancellationToken);
    }
}
