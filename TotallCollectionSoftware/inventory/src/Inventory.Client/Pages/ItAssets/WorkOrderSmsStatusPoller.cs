using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages;

/// <summary>
/// Refreshes the displayed outbox status only. It never sends or retries an SMS.
/// The caller scopes each poll to the currently open detail and its owner.
/// </summary>
internal sealed class WorkOrderSmsStatusPoller : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly int _maxPolls;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _active;
    private bool _disposed;

    public WorkOrderSmsStatusPoller() : this(TimeSpan.FromSeconds(5), 24, Task.Delay) { }

    internal WorkOrderSmsStatusPoller(TimeSpan interval, int maxPolls, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _interval = interval;
        _maxPolls = maxPolls;
        _delay = delay;
    }

    public static bool NeedsRefresh(IEnumerable<WorkOrderSmsEventDto> events) =>
        events.Any(e => e.Status is WorkOrderSmsStatus.Pending or WorkOrderSmsStatus.Sending);

    // Starting a newer watch cancels the previous one. Polling is bounded; a manual
    // refresh remains available if the queue is still pending or a status read fails.
    public Task StartAsync(Func<CancellationToken, Task<bool>> poll)
    {
        Stop();
        if (_disposed) return Task.CompletedTask;
        var source = new CancellationTokenSource();
        _active = source;
        return RunAsync(source, poll);
    }

    private async Task RunAsync(CancellationTokenSource source, Func<CancellationToken, Task<bool>> poll)
    {
        var token = source.Token;
        try
        {
            for (var i = 0; i < _maxPolls; i++)
            {
                await _delay(_interval, token);
                token.ThrowIfCancellationRequested();
                if (!await poll(token)) break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            // Stop a failed background read; the user can explicitly refresh the status.
        }
        finally
        {
            if (ReferenceEquals(_active, source)) _active = null;
            source.Dispose();
        }
    }

    public void Stop()
    {
        var source = _active;
        _active = null;
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
