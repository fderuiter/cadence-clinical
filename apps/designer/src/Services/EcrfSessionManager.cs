using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Designer.Models;
using Cadence.Apps.Designer.Persistence;
using Cadence.Ddf;
using Cadence.Kernel.Time;

using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Designer.Services;

public class EcrfCollaborationSession
{
    public string FormId { get; }
    public string StudyId { get; set; }
    public ConcurrentQueue<byte[]> UpdateVectors { get; } = new();
    public ConcurrentDictionary<string, AwarenessState> ActiveUsers { get; } = new();
    public string? LatestUsdmJson { get; set; }
    public DDFCompilationResult? LastCompilationResult { get; set; }
    public string? LastCompilationError { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }

    public EcrfCollaborationSession(string formId, string? studyId = null)
    {
        FormId = formId;
        StudyId = studyId ?? "STUDY-DRAFT";
    }

    public List<byte[]> GetUpdateChunks()
    {
        return UpdateVectors.ToList();
    }

    public byte[] GetCombinedUpdateBytes()
    {
        var chunks = GetUpdateChunks();
        if (chunks.Count == 0) return Array.Empty<byte>();

        int totalLen = chunks.Sum(c => c.Length);
        byte[] result = new byte[totalLen];
        int offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }
}

public interface IEcrfSessionManager
{
    EcrfCollaborationSession GetOrCreateSession(string formId, string? studyId = null);
    void ApplyUpdate(string formId, byte[] updateVector);
    List<byte[]> GetUpdateChunks(string formId);
    byte[] GetCombinedStateVector(string formId);
    void UpdateAwareness(string formId, string connectionId, AwarenessState awareness);
    void RemoveUser(string formId, string connectionId);
    List<AwarenessState> GetActiveUsers(string formId);
    Task<DDFCompilationResult?> CompileUsdmAsync(string formId, string usdmJson);
    Task<SnapshotSaveResult> SaveSnapshotAsync(string formId, string usdmJson, DesignerDbContext? dbContext, IClock? clock = null);
}

public class EcrfSessionManager : IEcrfSessionManager
{
    private readonly ConcurrentDictionary<string, EcrfCollaborationSession> _sessions = new();
    private readonly ILogger<EcrfSessionManager> _logger;
    private readonly IClock _clock;

    public EcrfSessionManager(ILogger<EcrfSessionManager> logger, IClock? clock = null)
    {
        _logger = logger;
        _clock = clock ?? SystemClock.Instance;
    }

    public EcrfCollaborationSession GetOrCreateSession(string formId, string? studyId = null)
    {
        return _sessions.GetOrAdd(formId, id => new EcrfCollaborationSession(id, studyId));
    }

    public void ApplyUpdate(string formId, byte[] updateVector)
    {
        if (updateVector == null || updateVector.Length == 0) return;
        var session = GetOrCreateSession(formId);
        session.UpdateVectors.Enqueue(updateVector);
        session.LastUpdatedAt = _clock.UtcNow;
    }

    public List<byte[]> GetUpdateChunks(string formId)
    {
        if (_sessions.TryGetValue(formId, out var session))
        {
            return session.GetUpdateChunks();
        }
        return new List<byte[]>();
    }

    public byte[] GetCombinedStateVector(string formId)
    {
        if (_sessions.TryGetValue(formId, out var session))
        {
            return session.GetCombinedUpdateBytes();
        }
        return Array.Empty<byte>();
    }

    public void UpdateAwareness(string formId, string connectionId, AwarenessState awareness)
    {
        var session = GetOrCreateSession(formId);
        session.ActiveUsers[connectionId] = awareness;
    }

    public void RemoveUser(string formId, string connectionId)
    {
        if (_sessions.TryGetValue(formId, out var session))
        {
            session.ActiveUsers.TryRemove(connectionId, out _);
        }
    }

    public List<AwarenessState> GetActiveUsers(string formId)
    {
        if (_sessions.TryGetValue(formId, out var session))
        {
            return session.ActiveUsers.Values.ToList();
        }
        return new List<AwarenessState>();
    }

    /// <summary>
    /// Executes USDM compilation check asynchronously in background without blocking CRDT stream.
    /// </summary>
    public async Task<DDFCompilationResult?> CompileUsdmAsync(string formId, string usdmJson)
    {
        var session = GetOrCreateSession(formId);
        session.LatestUsdmJson = usdmJson;

        return await Task.Run(() =>
        {
            try
            {
                var result = UsdmCompiler.CompileUsdm(usdmJson, session.StudyId);
                session.LastCompilationResult = result;
                session.LastCompilationError = null;
                _logger.LogInformation("Asynchronous USDM compilation succeeded for form {FormId}", formId);
                return result;
            }
            catch (Exception ex)
            {
                session.LastCompilationResult = null;
                session.LastCompilationError = ex.Message;
                _logger.LogWarning(ex, "Asynchronous USDM compilation failed for form {FormId}", formId);
                return null;
            }
        });
    }

    /// <summary>
    /// Serializes CRDT binary state vector alongside JSON schema document to preserve REST API compatibility
    /// and saves to database context if present.
    /// </summary>
    public async Task<SnapshotSaveResult> SaveSnapshotAsync(string formId, string usdmJson, DesignerDbContext? dbContext, IClock? clock = null)
    {
        var session = GetOrCreateSession(formId);
        var effectiveClock = clock ?? _clock;
        var now = effectiveClock.UtcNow;
        var crdtState = GetCombinedStateVector(formId);

        // Run compilation asynchronously
        DDFCompilationResult? compResult = null;
        string? compError = null;
        string compStatus = "Valid";

        try
        {
            compResult = await CompileUsdmAsync(formId, usdmJson);
            if (compResult == null)
            {
                compStatus = "Invalid";
                compError = session.LastCompilationError ?? "Schema compilation failed";
            }
        }
        catch (Exception ex)
        {
            compStatus = "Invalid";
            compError = ex.Message;
        }

        if (dbContext != null)
        {
            try
            {
                var snapshot = await dbContext.FormSnapshots.FindAsync(formId);
                if (snapshot == null)
                {
                    snapshot = new EcrfFormSnapshot
                    {
                        FormId = formId,
                        StudyId = session.StudyId,
                        UsdmJson = usdmJson,
                        CrdtStateVector = crdtState,
                        UpdatedAt = now,
                        CompilationStatus = compStatus,
                        CompilationResultJson = compResult != null ? JsonSerializer.Serialize(compResult) : null
                    };
                    dbContext.FormSnapshots.Add(snapshot);
                }
                else
                {
                    snapshot.StudyId = session.StudyId;
                    snapshot.UsdmJson = usdmJson;
                    snapshot.CrdtStateVector = crdtState;
                    snapshot.UpdatedAt = now;
                    snapshot.CompilationStatus = compStatus;
                    snapshot.CompilationResultJson = compResult != null ? JsonSerializer.Serialize(compResult) : null;
                }
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist snapshot for form {FormId}", formId);
            }
        }

        return new SnapshotSaveResult(
            FormId: formId,
            SavedAt: now,
            Success: compStatus == "Valid",
            CompilationResult: compResult,
            CompilationError: compError);
    }
}
