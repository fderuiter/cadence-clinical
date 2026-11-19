using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;

using FluentAssertions;

using Xunit;

namespace Cadence.Kernel.ConsumerTests;

/// <summary>A synthetic command a module might define. It uses only the public SDK types.</summary>
public sealed record RecordSyntheticNoteCommand(
    TenantId TenantId,
    string IdempotencyKey,
    string ReasonForChange,
    string Text) : ICommand<string>;

/// <summary>Handles the synthetic command without touching the unit of work, so it needs no kernel internals.</summary>
public sealed class RecordSyntheticNoteHandler : ICommandHandler<RecordSyntheticNoteCommand, string>
{
    public Task<CommandResult<string>> HandleAsync(
        RecordSyntheticNoteCommand command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return Task.FromResult<CommandResult<string>>(new KernelError("note.empty", 400, "Note text is required."));
        }

        return Task.FromResult(CommandResult<string>.Success(command.Text.Trim()));
    }
}

/// <summary>
/// C4 acceptance: a module compiles and runs against <c>Cadence.Kernel.Abstractions</c> and <c>Cadence.Kernel.Modules</c>
/// only. The handler never reads <see cref="ICommandContext"/>, so the test passes <c>null</c> for it.
/// </summary>
public sealed class ConsumerContractTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("00000000-0000-0000-0000-000000000001")); // deid-ignore: synthetic zero-prefixed GUID, not PHI

    [Fact]
    public async Task Handler_returns_success_for_a_valid_synthetic_command()
    {
        var handler = new RecordSyntheticNoteHandler();
        var command = new RecordSyntheticNoteCommand(Tenant, "key-1", "synthetic test", "  hello  ");

        var result = await handler.HandleAsync(command, context: null!, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
        result.Replayed.Should().BeFalse();
    }

    [Fact]
    public async Task Handler_returns_a_typed_failure_instead_of_throwing()
    {
        var handler = new RecordSyntheticNoteHandler();
        var command = new RecordSyntheticNoteCommand(Tenant, "key-2", "synthetic test", "   ");

        var result = await handler.HandleAsync(command, context: null!, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("note.empty");
    }
}
