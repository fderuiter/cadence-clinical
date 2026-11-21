using System;
using System.Threading;
using System.Threading.Tasks;

using MassTransit;
using MassTransit.Testing;

namespace Cadence.UnitTests.Fixtures;

internal static class MassTransitHarnessExtensions
{
    public static async Task PublishAndWaitForConsumeAsync<T>(
        this ITestHarness harness,
        T message,
        CancellationToken cancellationToken)
        where T : class
    {
        var messageId = Guid.NewGuid();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = harness.Bus.ConnectConsumeObserver(new MessageIdConsumeObserver<T>(messageId, tcs));
        try
        {
            await harness.Bus.Publish(message, ctx => ctx.MessageId = messageId, cancellationToken).ConfigureAwait(false);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            handle.Disconnect();
        }
    }

    private sealed class MessageIdConsumeObserver<T>(Guid expectedMessageId, TaskCompletionSource completion) : IConsumeObserver
        where T : class
    {
        public Task PreConsume<TMessage>(ConsumeContext<TMessage> context)
            where TMessage : class => Task.CompletedTask;

        public Task PostConsume<TMessage>(ConsumeContext<TMessage> context)
            where TMessage : class
        {
            if (typeof(TMessage) == typeof(T) && context.MessageId == expectedMessageId)
            {
                completion.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public Task ConsumeFault<TMessage>(ConsumeContext<TMessage> context, Exception exception)
            where TMessage : class
        {
            if (typeof(TMessage) == typeof(T) && context.MessageId == expectedMessageId)
            {
                completion.TrySetException(exception);
            }

            return Task.CompletedTask;
        }
    }
}
