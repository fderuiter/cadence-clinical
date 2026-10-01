using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Protocol.Design;

namespace Cadence.Ddf;

public static class PatientMigrationEngine
{
    public static PatientMigrationResult ExecutePatientMigration(
        PatientMigrationMatrix matrix,
        IReadOnlyDictionary<string, object?> patientData,
        bool isRetrospective = false)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(patientData);

        var data = new Dictionary<string, object?>(patientData, StringComparer.Ordinal);
        var queriesRaised = new List<string>();

        var directives = matrix.Directives ?? [];

        foreach (var directive in directives)
        {
            string varId = directive.VariableId;
            if (string.IsNullOrWhiteSpace(varId))
            {
                continue;
            }

            string action = directive.Action?.ToUpperInvariant() ?? "ADD";

            if (action == "ADD")
            {
                if (directive.MigrationMode == MigrationMode.Retrospective || isRetrospective)
                {
                    if (!data.TryGetValue(varId, out var val) || val is null)
                    {
                        string baseMsg = directive.BackfillQueryMessage ?? $"Retrospective amendment required field {varId} missing";
                        string msg = baseMsg.Contains(varId, StringComparison.Ordinal) ? baseMsg : $"[{varId}] {baseMsg}";
                        queriesRaised.Add(msg);
                    }
                }
                else if (!data.ContainsKey(varId))
                {
                    data[varId] = null;
                }
            }
            else if (action == "REMOVE" && data.TryGetValue(varId, out var existingVal))
            {
                data.Remove(varId);
                data[$"_archived_{varId}"] = existingVal;
            }
            else if (action is "MODIFY_CONSTRAINT" or "MODIFY_RULE" && data.TryGetValue(varId, out var val) && val is null && directive.MigrationMode == MigrationMode.Retrospective)
            {
                string baseMsg = directive.BackfillQueryMessage ?? $"Rule modification requires updated value for {varId}";
                string msg = baseMsg.Contains(varId, StringComparison.Ordinal) ? baseMsg : $"[{varId}] {baseMsg}";
                queriesRaised.Add(msg);
            }
        }

        return new PatientMigrationResult(
            ProtocolId: matrix.ProtocolId,
            FromVersion: matrix.FromVersionTag,
            ToVersion: matrix.ToVersionTag,
            PatientData: data,
            QueriesRaised: queriesRaised,
            DirectivesAppliedCount: directives.Count,
            IsCompliant: queriesRaised.Count == 0
        );
    }

    public static PatientMigrationMatrix BuildMigrationMatrix(
        string protocolId,
        string fromVersionTag,
        string toVersionTag,
        SoaAmendmentDiff? diff,
        DateTimeOffset effectiveDate = default)
    {
        var directives = new List<MigrationDirective>();

        if (diff != null)
        {
            foreach (var d in diff.Directives)
            {
                string elementId = d.ElementId;
                MigrationMode mode = diff.ChangesSchedule ? MigrationMode.Retrospective : MigrationMode.Prospective;

                switch (d.Kind)
                {
                    case SoaDirectiveKind.VisitAdded:
                    case SoaDirectiveKind.ActivityAdded:
                        directives.Add(new MigrationDirective(
                            VariableId: elementId,
                            Action: "ADD",
                            MigrationMode: mode,
                            BackfillQueryMessage: $"Field {elementId} introduced in protocol amendment {toVersionTag} requires data backfill."
                        ));
                        break;

                    case SoaDirectiveKind.VisitRemoved:
                    case SoaDirectiveKind.ActivityRemoved:
                        directives.Add(new MigrationDirective(
                            VariableId: elementId,
                            Action: "REMOVE",
                            MigrationMode: MigrationMode.Prospective,
                            BackfillQueryMessage: null
                        ));
                        break;

                    case SoaDirectiveKind.VisitMoved:
                    case SoaDirectiveKind.VisitWindowChanged:
                    case SoaDirectiveKind.VisitLocationsChanged:
                    case SoaDirectiveKind.ActivityConceptsChanged:
                        directives.Add(new MigrationDirective(
                            VariableId: elementId,
                            Action: "MODIFY_CONSTRAINT",
                            MigrationMode: mode,
                            BackfillQueryMessage: $"Constraint modified for {elementId} in protocol amendment {toVersionTag}."
                        ));
                        break;

                    case SoaDirectiveKind.ActivityFrequencyChanged:
                    case SoaDirectiveKind.CriterionAdded:
                    case SoaDirectiveKind.CriterionRemoved:
                    case SoaDirectiveKind.CriterionChanged:
                        directives.Add(new MigrationDirective(
                            VariableId: elementId,
                            Action: "MODIFY_RULE",
                            MigrationMode: mode,
                            BackfillQueryMessage: $"Rule modified for {elementId} in protocol amendment {toVersionTag}."
                        ));
                        break;
                }
            }
        }

        return new PatientMigrationMatrix(
            ProtocolId: protocolId,
            FromVersionTag: fromVersionTag,
            ToVersionTag: toVersionTag,
            Directives: directives,
            EffectiveDate: effectiveDate
        );
    }
}
