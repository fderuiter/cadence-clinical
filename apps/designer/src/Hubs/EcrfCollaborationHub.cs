using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Apps.Designer.Models;
using Cadence.Apps.Designer.Persistence;
using Cadence.Apps.Designer.Services;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Designer.Hubs;

public class EcrfCollaborationHub : Hub
{
    private readonly IEcrfSessionManager _sessionManager;
    private readonly ILogger<EcrfCollaborationHub> _logger;

    public EcrfCollaborationHub(IEcrfSessionManager sessionManager, ILogger<EcrfCollaborationHub> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Joins a collaborative eCRF authoring session group, receiving initial CRDT state and active user presence.
    /// </summary>
    public async Task JoinSession(string formId, string? studyId = null)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, formId);

        _sessionManager.GetOrCreateSession(formId, studyId);
        var stateChunks = _sessionManager.GetUpdateChunks(formId);

        // Send existing CRDT update chunks to joining client
        if (stateChunks.Count > 0)
        {
            await Clients.Caller.SendAsync("ReceiveStateChunks", stateChunks);
        }

        // Send active users list to joining client
        var activeUsers = _sessionManager.GetActiveUsers(formId);
        await Clients.Caller.SendAsync("ReceiveActiveUsers", activeUsers);

        _logger.LogInformation("Client {ConnectionId} joined session {FormId}", Context.ConnectionId, formId);
    }

    /// <summary>
    /// Leaves a collaborative eCRF authoring session group.
    /// </summary>
    public async Task LeaveSession(string formId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, formId);
        _sessionManager.RemoveUser(formId, Context.ConnectionId);

        await Clients.OthersInGroup(formId).SendAsync("UserLeft", Context.ConnectionId);
        _logger.LogInformation("Client {ConnectionId} left session {FormId}", Context.ConnectionId, formId);
    }

    /// <summary>
    /// Broadcasts binary CRDT update vector to all other co-authors in the form group.
    /// </summary>
    public async Task SendUpdate(string formId, byte[] updateVector)
    {
        if (updateVector == null || updateVector.Length == 0) return;

        _sessionManager.ApplyUpdate(formId, updateVector);
        await Clients.OthersInGroup(formId).SendAsync("ReceiveUpdate", updateVector);
    }

    /// <summary>
    /// Broadcasts real-time awareness/presence state (cursor position, active section/field) to group.
    /// </summary>
    public async Task SendAwareness(string formId, AwarenessState awareness)
    {
        if (awareness == null) return;

        var updatedState = awareness with { UserId = Context.ConnectionId };
        _sessionManager.UpdateAwareness(formId, Context.ConnectionId, updatedState);

        await Clients.OthersInGroup(formId).SendAsync("ReceiveAwareness", Context.ConnectionId, updatedState);
    }

    /// <summary>
    /// Requests a state sync for reconnecting clients.
    /// </summary>
    public async Task RequestSync(string formId)
    {
        var stateChunks = _sessionManager.GetUpdateChunks(formId);
        await Clients.Caller.SendAsync("ReceiveStateChunks", stateChunks);
    }

    /// <summary>
    /// Saves a snapshot of the current CRDT document state and triggers async USDM compilation.
    /// </summary>
    public async Task SaveSnapshot(string formId, string usdmJson)
    {
        var dbContext = Context.GetHttpContext()?.RequestServices.GetService<DesignerDbContext>();
        var result = await _sessionManager.SaveSnapshotAsync(formId, usdmJson, dbContext);

        await Clients.Group(formId).SendAsync("SnapshotSaved", result);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
