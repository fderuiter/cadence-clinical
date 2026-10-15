using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.AiGateway.Services;

/// <summary>
/// In-memory and ephemeral Redis vector index and cache service.
/// @req:PRD-SYS-001
/// </summary>
public sealed class VectorCacheService : IVectorCacheService
{
    private readonly ConcurrentDictionary<Guid, VectorCandidateEntry> _store = new();
    private bool _isHealthy = true;

    public VectorCacheService()
    {
        // Seed standard dictionary terms if empty
        SeedDefaultDictionaryTerms();
    }

    public Task IndexCandidatesAsync(IEnumerable<VectorCandidateEntry> candidates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (var entry in candidates)
        {
            _store[entry.Id] = entry;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorCandidateEntry>> GetCandidatesAsync(
        string? dictionaryType = null,
        string? studyId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isHealthy)
        {
            throw new InvalidOperationException("Vector cache service is currently offline.");
        }

        var query = _store.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(dictionaryType))
        {
            query = query.Where(x => x.DictionaryType.Equals(dictionaryType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(x => x.Scope == "ENTERPRISE" || (x.Scope == "STUDY" && x.StudyId == studyId));
        }

        IReadOnlyList<VectorCandidateEntry> list = query.ToList();
        return Task.FromResult(list);
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_isHealthy);
    }

    public void SetHealthy(bool healthy)
    {
        _isHealthy = healthy;
    }

    private void SeedDefaultDictionaryTerms()
    {
        var embedder = new VectorEmbeddingService();

        var defaultMedDraTerms = new[]
        {
            ("Headache", "10019211", "HEADACHE", "Nervous system disorders"),
            ("Headache acute", "10019212", "HEADACHE ACUTE", "General disorders"),
            ("Headache tension", "10019213", "TENSION HEADACHE", "Nervous system disorders"),
            ("Migraine", "10027599", "MIGRAINE", "Nervous system disorders"),
            ("Nausea", "10028813", "NAUSEA", "Gastrointestinal disorders"),
            ("Vomiting", "10047700", "VOMITING", "Gastrointestinal disorders"),
            ("Fatigue", "10016256", "FATIGUE", "General disorders"),
            ("Fever", "10037660", "PYREXIA", "General disorders"),
            ("Pyrexia", "10037660", "PYREXIA", "General disorders"),
            ("Dizziness", "10013573", "DIZZINESS", "Nervous system disorders"),
            ("Hypertension", "10020772", "HYPERTENSION", "Vascular disorders"),
            ("Chest pain", "10008479", "CHEST PAIN", "General disorders"),
            ("Shortness of breath", "10013968", "DYSPNOEA", "Respiratory disorders"),
            ("Dyspnea", "10013968", "DYSPNOEA", "Respiratory disorders"),
            ("Rash", "10037844", "RASH", "Skin and subcutaneous tissue disorders"),
            ("Diarrhea", "10012735", "DIARRHOEA", "Gastrointestinal disorders"),
            ("Diarrhoea", "10012735", "DIARRHOEA", "Gastrointestinal disorders"),
        };

        foreach (var (verbatim, code, pt, soc) in defaultMedDraTerms)
        {
            Guid id = Guid.NewGuid();
            var entry = new VectorCandidateEntry(
                Id: id,
                Scope: "ENTERPRISE",
                StudyId: null,
                VerbatimText: verbatim,
                DictionaryType: "MEDDRA",
                DictionaryVersion: "26.1",
                TargetCode: code,
                TargetTerm: pt,
                TargetLevel: soc,
                Embedding: embedder.GenerateEmbedding(verbatim));

            _store[id] = entry;
        }

        var defaultWhoDrugTerms = new[]
        {
            ("Aspirin", "00000801", "ASPIRIN", "Analgesic"),
            ("Paracetamol", "00001502", "PARACETAMOL", "Analgesic"),
            ("Acetaminophen", "00001502", "PARACETAMOL", "Analgesic"),
            ("Ibuprofen", "00002103", "IBUPROFEN", "Anti-inflammatory"),
            ("Amoxicillin", "00003404", "AMOXICILLIN", "Antibacterial"),
            ("Metformin", "00005505", "METFORMIN", "Antidiabetic"),
            ("Atorvastatin", "00008906", "ATORVASTATIN", "Lipid modifying agent"),
        };

        foreach (var (verbatim, code, pt, level) in defaultWhoDrugTerms)
        {
            Guid id = Guid.NewGuid();
            var entry = new VectorCandidateEntry(
                Id: id,
                Scope: "ENTERPRISE",
                StudyId: null,
                VerbatimText: verbatim,
                DictionaryType: "WHODRUG",
                DictionaryVersion: "2024Q1",
                TargetCode: code,
                TargetTerm: pt,
                TargetLevel: level,
                Embedding: embedder.GenerateEmbedding(verbatim));

            _store[id] = entry;
        }
    }
}
