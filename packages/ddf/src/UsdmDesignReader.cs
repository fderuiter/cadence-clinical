using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;

namespace Cadence.Ddf;

/// <summary>
/// One USDM 4 import (<see cref="UsdmDesignImporter"/>). It reads the chosen study version and design and builds each
/// element list of the protocol design, collecting problems and notes as it goes.
/// </summary>
internal sealed class UsdmDesignReader(UsdmImportOptions options)
{
    private readonly List<string> _problems = [];
    private readonly List<UsdmImportNote> _notes = [];
    private readonly List<UsdmUnmappedConceptToken> _unmappedConcepts = [];
    private readonly Dictionary<string, JsonElement> _biomedicalConcepts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _criterionItems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _encounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _armInterventions = new(StringComparer.Ordinal);

    public async Task<UsdmStagingResult> ReadAsync(
        JsonElement root,
        BiomedicalConceptResolver resolve,
        CancellationToken cancellationToken,
        ProtocolTerminologyResolver? resolveTerminology = null)
    {
        if (!Select(root, out var study, out var version, out var design))
        {
            return Result(null);
        }

        Index(version, design);
        var endpoints = Endpoints(design);
        var interventions = Interventions(version, design);
        var arms = Arms(design);
        var imported = new ProtocolDesign
        {
            Title = Title(study, version),
            Objectives = Objectives(design),
            Endpoints = endpoints,
            Estimands = Estimands(design, endpoints),
            Populations = Populations(design),
            EligibilityCriteria = EligibilityCriteria(design),
            Arms = arms,
            Epochs = Epochs(design),
            Cohorts = Cohorts(design),
            Interventions = interventions,
            PlannedActivities = PlannedActivities(design),
            CollectionSpecs = await CollectionSpecsAsync(design, resolve, resolveTerminology, cancellationToken).ConfigureAwait(false),
        };
        NoteUncarried(version, design);

        if (_problems.Count > 0)
        {
            return Result(null);
        }

        var check = ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(imported));
        _problems.AddRange(check.Problems);
        return Result(check.Design);
    }

    private UsdmStagingResult Result(ProtocolDesign? design) =>
        new(_problems.Count == 0 ? design : null, [.. _problems], [.. _notes], [.. _unmappedConcepts]);

    private bool Select(JsonElement root, out JsonElement study, out JsonElement version, out JsonElement design)
    {
        study = version = design = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            _problems.Add("The document is not a USDM Wrapper object.");
            return false;
        }

        var usdmVersion = Usdm.Str(root, "usdmVersion");
        if (usdmVersion is null || usdmVersion.Split('.')[0] != UsdmDesignImporter.SupportedMajorVersion)
        {
            _problems.Add($"usdmVersion '{usdmVersion}' is not supported; the importer reads USDM {UsdmDesignImporter.SupportedMajorVersion}.x.");
            return false;
        }

        if (Usdm.Obj(root, "study") is not { } found)
        {
            _problems.Add("The document has no study.");
            return false;
        }

        study = found;
        return One(Usdm.Arr(study, "versions"), options.StudyVersionId, "study version", out version)
            && One(Usdm.Arr(version, "studyDesigns"), options.StudyDesignId, "study design", out design);
    }

    private bool One(IReadOnlyList<JsonElement> candidates, string? id, string what, out JsonElement chosen)
    {
        chosen = default;
        var matches = id is null ? candidates : [.. candidates.Where(c => Usdm.Str(c, "id") == id)];
        if (matches.Count == 1)
        {
            chosen = matches[0];
            return true;
        }

        var ids = string.Join(", ", candidates.Select(c => Usdm.Str(c, "id")));
        _problems.Add(id is null
            ? $"The document has {candidates.Count} {what}s ({ids}); name the one to import."
            : $"There is no {what} '{id}' (found: {ids}).");
        return false;
    }

    private void Index(JsonElement version, JsonElement design)
    {
        foreach (var bc in Usdm.Arr(version, "biomedicalConcepts"))
        {
            _biomedicalConcepts[Usdm.Id(bc)] = bc;
        }

        foreach (var item in Usdm.Arr(version, "eligibilityCriterionItems"))
        {
            _criterionItems[Usdm.Id(item)] = item;
        }

        foreach (var encounter in Usdm.Arr(design, "encounters"))
        {
            _encounters[Usdm.Id(encounter)] = encounter;
        }

        var elements = Usdm.Arr(design, "elements").ToDictionary(Usdm.Id, e => Usdm.Strs(e, "studyInterventionIds"), StringComparer.Ordinal);
        foreach (var cell in Usdm.Arr(design, "studyCells"))
        {
            var arm = Usdm.Str(cell, "armId") ?? string.Empty;
            if (!_armInterventions.TryGetValue(arm, out var list))
            {
                list = [];
                _armInterventions[arm] = list;
            }

            foreach (var intervention in Usdm.Strs(cell, "elementIds").SelectMany(e => elements.GetValueOrDefault(e) ?? []))
            {
                if (!list.Contains(intervention, StringComparer.Ordinal))
                {
                    list.Add(intervention);
                }
            }
        }
    }

    private string Title(JsonElement study, JsonElement version)
    {
        var titles = Usdm.Arr(version, "titles");
        var official = titles.FirstOrDefault(t => Usdm.Decode(t, "type") is { } type && type.Contains("official", StringComparison.OrdinalIgnoreCase));
        var title = Usdm.Str(official.ValueKind == JsonValueKind.Object ? official : titles.FirstOrDefault(), "text") ?? Usdm.Str(study, "name");
        if (title is null)
        {
            _problems.Add("The study has no title and no name.");
        }

        return title ?? string.Empty;
    }

    private List<Objective> Objectives(JsonElement design) =>
        [.. Usdm.Arr(design, "objectives").Select(o => new Objective(Usdm.Id(o), Level(o), TextOf(o)))];

    private List<Endpoint> Endpoints(JsonElement design) =>
        [.. Usdm.Arr(design, "objectives").SelectMany(o => Usdm.Arr(o, "endpoints").Select(e =>
            new Endpoint(Usdm.Id(e), Usdm.Id(o), Level(e), TextOf(e))))];

    private ObjectiveLevel Level(JsonElement element)
    {
        var level = Usdm.Decode(element, "level") ?? string.Empty;
        if (level.Contains("primary", StringComparison.OrdinalIgnoreCase))
        {
            return ObjectiveLevel.Primary;
        }

        if (level.Contains("secondary", StringComparison.OrdinalIgnoreCase))
        {
            return ObjectiveLevel.Secondary;
        }

        if (level.Contains("exploratory", StringComparison.OrdinalIgnoreCase))
        {
            return ObjectiveLevel.Exploratory;
        }

        _problems.Add($"{Usdm.Id(element)}: level '{level}' is not primary, secondary or exploratory.");
        return ObjectiveLevel.Exploratory;
    }

    private List<Estimand> Estimands(JsonElement design, IReadOnlyList<Endpoint> endpoints) =>
        [.. Usdm.Arr(design, "estimands").Select(e =>
        {
            var id = Usdm.Id(e);
            var endpointId = Usdm.Str(e, "variableOfInterestId") ?? string.Empty;
            var interventions = Usdm.Strs(e, "interventionIds");
            var arms = _armInterventions.Where(a => a.Value.Exists(interventions.Contains)).Select(a => a.Key).Order(StringComparer.Ordinal).ToList();
            var variable = endpoints.FirstOrDefault(p => p.Id == endpointId)?.Text ?? string.Empty;
            return new Estimand(
                id,
                endpointId,
                Usdm.Str(e, "analysisPopulationId") ?? string.Empty,
                arms,
                variable,
                [.. Usdm.Arr(e, "intercurrentEvents").Select(IntercurrentEventOf)],
                Usdm.Str(e, "populationSummary") ?? string.Empty,
                interventions);
        })];

    private static IntercurrentEvent IntercurrentEventOf(JsonElement ice)
    {
        var description = Usdm.Str(ice, "text") ?? Usdm.Str(ice, "name") ?? string.Empty;
        var stated = Usdm.Str(ice, "strategy") ?? string.Empty;
        var key = new string([.. stated.Where(char.IsLetter)]).ToUpperInvariant();
        IntercurrentEventStrategy? strategy = key switch
        {
            _ when key.StartsWith("TREATMENTPOLICY", StringComparison.Ordinal) => IntercurrentEventStrategy.TreatmentPolicy,
            _ when key.StartsWith("HYPOTHETICAL", StringComparison.Ordinal) => IntercurrentEventStrategy.Hypothetical,
            _ when key.StartsWith("COMPOSITE", StringComparison.Ordinal) => IntercurrentEventStrategy.Composite,
            _ when key.StartsWith("WHILEONTREATMENT", StringComparison.Ordinal) => IntercurrentEventStrategy.WhileOnTreatment,
            _ when key.StartsWith("PRINCIPALSTRAT", StringComparison.Ordinal) => IntercurrentEventStrategy.PrincipalStratum,
            _ => null,
        };
        return strategy is { } known
            ? new IntercurrentEvent(description, known)
            : new IntercurrentEvent(description, IntercurrentEventStrategy.Other, stated);
    }

    private List<Population> Populations(JsonElement design)
    {
        var populations = new List<Population>();
        if (Usdm.Obj(design, "population") is { } enrolled)
        {
            populations.Add(new Population(Usdm.Id(enrolled), Usdm.Str(enrolled, "name") ?? string.Empty, Usdm.Strs(enrolled, "criterionIds"), Usdm.Text(enrolled, "description")));
        }

        foreach (var analysis in Usdm.Arr(design, "analysisPopulations"))
        {
            if (Usdm.Strs(analysis, "subsetOfIds").Count > 0)
            {
                Note(analysis, "subsetOfIds is not carried; the design has no population hierarchy.");
            }

            populations.Add(new Population(Usdm.Id(analysis), Usdm.Str(analysis, "name") ?? string.Empty, [], Usdm.Text(analysis, "text")));
        }

        return populations;
    }

    private List<EligibilityCriterion> EligibilityCriteria(JsonElement design) =>
        [.. Usdm.Arr(design, "eligibilityCriteria").Select(c =>
        {
            var id = Usdm.Id(c);
            var category = Usdm.Decode(c, "category") ?? string.Empty;
            var itemId = Usdm.Str(c, "criterionItemId") ?? string.Empty;
            string? text = _criterionItems.TryGetValue(itemId, out var item) ? TextOf(item) : null;
            if (text is null)
            {
                _problems.Add($"{id}: criterion item '{itemId}' is not in the study version.");
            }

            var inclusion = category.Contains("inclusion", StringComparison.OrdinalIgnoreCase);
            if (!inclusion && !category.Contains("exclusion", StringComparison.OrdinalIgnoreCase))
            {
                _problems.Add($"{id}: category '{category}' is neither inclusion nor exclusion.");
            }

            return new EligibilityCriterion(id, inclusion ? CriterionCategory.Inclusion : CriterionCategory.Exclusion, text ?? string.Empty, []);
        })];

    private List<Arm> Arms(JsonElement design) =>
        [.. Usdm.Arr(design, "arms").Select(a =>
        {
            var type = Usdm.Decode(a, "type") ?? string.Empty;
            var armType = ArmTypeOf(type);
            if (armType == ArmType.Other)
            {
                Note(a, $"arm type '{type}' is read as Other.");
            }

            return new Arm(Usdm.Id(a), Usdm.Str(a, "name") ?? string.Empty, armType, _armInterventions.GetValueOrDefault(Usdm.Id(a)) ?? []);
        })];

    private static ArmType ArmTypeOf(string type) => type.ToUpperInvariant() switch
    {
        var t when t.Contains("EXPERIMENTAL", StringComparison.Ordinal) => ArmType.Experimental,
        var t when t.Contains("ACTIVE COMPARATOR", StringComparison.Ordinal) => ArmType.ActiveComparator,
        var t when t.Contains("PLACEBO", StringComparison.Ordinal) => ArmType.PlaceboComparator,
        var t when t.Contains("NO INTERVENTION", StringComparison.Ordinal) => ArmType.NoIntervention,
        _ => ArmType.Other,
    };

    private List<Epoch> Epochs(JsonElement design)
    {
        var epochs = Usdm.Arr(design, "epochs");
        var byId = epochs.ToDictionary(Usdm.Id, StringComparer.Ordinal);
        var ordered = new List<JsonElement>();
        var current = epochs.FirstOrDefault(e => Usdm.Str(e, "previousId") is not { } previous || !byId.ContainsKey(previous));
        while (current.ValueKind == JsonValueKind.Object && ordered.Count < epochs.Count && !ordered.Exists(e => Usdm.Id(e) == Usdm.Id(current)))
        {
            ordered.Add(current);
            current = Usdm.Str(current, "nextId") is { } next && byId.TryGetValue(next, out var found) ? found : default;
        }

        if (ordered.Count != epochs.Count)
        {
            _notes.Add(new UsdmImportNote("epochs", "previousId/nextId do not chain every epoch; epochs are ordered as listed."));
            ordered = [.. epochs];
        }

        return [.. ordered.Select((e, i) => new Epoch(Usdm.Id(e), Usdm.Str(e, "name") ?? string.Empty, i + 1))];
    }

    private List<Cohort> Cohorts(JsonElement design)
    {
        if (Usdm.Obj(design, "population") is not { } population)
        {
            return [];
        }

        var arms = Usdm.Arr(design, "arms");
        return [.. Usdm.Arr(population, "cohorts").Select(c =>
        {
            var id = Usdm.Id(c);
            if (Usdm.Strs(c, "criterionIds").Count > 0)
            {
                Note(c, "cohort eligibility criteria are not carried; the design's criteria belong to populations.");
            }

            var cohortArms = arms.Where(a => Usdm.Strs(a, "populationIds").Contains(id, StringComparer.Ordinal)).Select(Usdm.Id).ToList();
            return new Cohort(id, Usdm.Str(c, "name") ?? string.Empty, cohortArms, Usdm.Text(c, "description"));
        })];
    }

    private List<Intervention> Interventions(JsonElement version, JsonElement design)
    {
        var all = Usdm.Arr(version, "studyInterventions");
        var used = Usdm.Strs(design, "studyInterventionIds");
        var chosen = used.Count == 0 ? all : [.. all.Where(i => used.Contains(Usdm.Id(i), StringComparer.Ordinal))];
        foreach (var missing in used.Where(id => !all.Any(i => Usdm.Id(i) == id)))
        {
            _problems.Add($"studyInterventionIds names '{missing}', which is not in the study version.");
        }

        return [.. chosen.Select(InterventionOf)];
    }

    private Intervention InterventionOf(JsonElement intervention)
    {
        var id = Usdm.Id(intervention);
        var administrations = Usdm.Arr(intervention, "administrations");
        if (administrations.Count > 1)
        {
            Note(intervention, $"{administrations.Count} administrations; the design keeps the first.");
        }

        if (Usdm.Arr(intervention, "codes").Count > 0)
        {
            Note(intervention, "product codes are not carried; the design names a product by concept.");
        }

        var first = administrations.FirstOrDefault();
        var administered = first.ValueKind == JsonValueKind.Object;
        return new Intervention(
            id,
            Usdm.Str(intervention, "name") ?? string.Empty,
            Dose: administered ? DoseOf(first) : null,
            Route: administered ? Usdm.Decode(first, "route") : null,
            Regimen: administered ? Usdm.Decode(first, "frequency") : null);
    }

    private Dose? DoseOf(JsonElement administration)
    {
        if (Usdm.Obj(administration, "dose") is not { } dose || !dose.TryGetProperty("value", out var value) || !value.TryGetDecimal(out var amount))
        {
            return null;
        }

        if (amount <= 0)
        {
            Note(administration, $"dose {amount} is not positive and is not carried.");
            return null;
        }

        var code = Usdm.Obj(dose, "unit") is { } unit ? Usdm.Code(unit) : null;
        if (code is { } c && UsdmUcumUnitResolver.UcumUnits.TryGetValue(c.Code, out var ucum))
        {
            return new Dose(amount, ucum);
        }

        Note(administration, $"dose unit '{code?.Decode}' has no UCUM equivalent here; its CDISC name is kept.");
        return new Dose(amount, code?.Decode ?? string.Empty);
    }

    private List<PlannedActivity> PlannedActivities(JsonElement design)
    {
        var timelines = Usdm.Arr(design, "scheduleTimelines");
        var main = timelines.FirstOrDefault(t => t.TryGetProperty("mainTimeline", out var flag) && flag.ValueKind == JsonValueKind.True);
        if (main.ValueKind != JsonValueKind.Object)
        {
            main = timelines.FirstOrDefault();
        }

        foreach (var other in timelines.Where(t => Usdm.Id(t) != (main.ValueKind == JsonValueKind.Object ? Usdm.Id(main) : null)))
        {
            Note(other, "only the main timeline is imported; this timeline's instances are not carried.");
        }

        if (main.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var schedule = new UsdmSchedule(main, _notes);
        var activities = new List<PlannedActivity>();
        foreach (var instance in Usdm.Arr(main, "instances"))
        {
            if (Usdm.Str(instance, "instanceType") != "ScheduledActivityInstance")
            {
                Note(instance, "decisions and other non-activity instances are not carried.");
                continue;
            }

            if (Usdm.Str(instance, "epochId") is not { } epoch)
            {
                Note(instance, "the instance names no epoch and is not carried.");
                continue;
            }

            var encounter = Usdm.Str(instance, "encounterId") is { } encounterId ? _encounters.GetValueOrDefault(encounterId) : default;
            var visit = encounter.ValueKind == JsonValueKind.Object;
            activities.Add(new PlannedActivity(
                Usdm.Id(instance),
                (visit ? Usdm.Str(encounter, "name") : null) ?? Usdm.Str(instance, "name") ?? string.Empty,
                epoch,
                visit ? Locations(encounter) : [],
                Usdm.Strs(instance, "activityIds"),
                schedule.TimingOf(Usdm.Id(instance))));
        }

        return activities;
    }

    private List<ActivityLocation> Locations(JsonElement encounter)
    {
        var locations = new List<ActivityLocation>();
        foreach (var setting in Usdm.Arr(encounter, "environmentalSettings").Concat(Usdm.Arr(encounter, "contactModes")))
        {
            var decode = Usdm.Code(setting)?.Decode ?? string.Empty;
            if (LocationOf(decode) is { } location)
            {
                if (!locations.Contains(location))
                {
                    locations.Add(location);
                }
            }
            else if (!decode.Equals("In Person", StringComparison.OrdinalIgnoreCase))
            {
                Note(encounter, $"setting or contact mode '{decode}' has no activity location.");
            }
        }

        return locations;
    }

    private static ActivityLocation? LocationOf(string decode) => decode.ToUpperInvariant() switch
    {
        "CLINIC" or "HOSPITAL" or "SITE" or "INVESTIGATIONAL SITE" => ActivityLocation.Site,
        "HOME" => ActivityLocation.Home,
        "TELEPHONE CALL" or "TELEPHONE" or "VIDEO CALL" or "TELEMEDICINE" or "TELEHEALTH" => ActivityLocation.Telehealth,
        "LOCAL FACILITY" or "PHARMACY" or "LABORATORY" => ActivityLocation.LocalFacility,
        "REMOTE" => ActivityLocation.Remote,
        _ => null,
    };

    private async Task<List<CollectionSpec>> CollectionSpecsAsync(
        JsonElement design,
        BiomedicalConceptResolver resolve,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, ConceptRef?>(StringComparer.Ordinal);
        var resolvedTerminology = new Dictionary<string, ProtocolTerminologyResolution>(StringComparer.Ordinal);
        var specs = new List<CollectionSpec>();
        foreach (var activity in Usdm.Arr(design, "activities"))
        {
            var id = Usdm.Id(activity);
            if (Usdm.Strs(activity, "bcCategoryIds").Count + Usdm.Strs(activity, "bcSurrogateIds").Count > 0)
            {
                Note(activity, "Biomedical Concept categories and surrogates are not carried; only Biomedical Concepts become items.");
            }

            if (Usdm.Arr(activity, "rules").Count > 0 || Usdm.Strs(activity, "conditionIds").Count > 0)
            {
                Note(activity, "activity rules and conditions are not carried as executable item rules.");
            }

            var items = new List<CollectionItem>();
            foreach (var bcId in Usdm.Strs(activity, "biomedicalConceptIds"))
            {
                var item = await UsdmBiomedicalConceptItemBuilder.BuildItemAsync(
                    id,
                    bcId,
                    _biomedicalConcepts,
                    resolve,
                    resolveTerminology,
                    resolved,
                    resolvedTerminology,
                    _problems,
                    _notes,
                    _unmappedConcepts,
                    cancellationToken).ConfigureAwait(false);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            specs.Add(new CollectionSpec(id, Usdm.Str(activity, "name") ?? string.Empty, items));
        }

        return specs;
    }

    private void NoteUncarried(JsonElement version, JsonElement design)
    {
        _notes.Add(new UsdmImportNote("activities", "USDM does not say why data is collected; items have no purpose until an author adds one (K-PROT-3)."));
        foreach (var property in (string[])["indications", "biospecimenRetentions", "notes"])
        {
            if (Usdm.Arr(design, property).Count > 0)
            {
                _notes.Add(new UsdmImportNote(property, $"{property} are not carried by the protocol design."));
            }
        }

        if (Usdm.Arr(version, "conditions").Count + Usdm.Arr(design, "conditions").Count > 0)
        {
            _notes.Add(new UsdmImportNote("conditions", "conditions and conditional collection rules are not carried by the protocol design."));
        }
    }

    private string TextOf(JsonElement element)
    {
        var text = Usdm.Str(element, "text") ?? string.Empty;
        if (text.Contains("<usdm:", StringComparison.Ordinal))
        {
            Note(element, "the text holds USDM tags, kept as written; their dictionary values are not substituted.");
        }

        return text;
    }

    private void Note(JsonElement element, string message) => _notes.Add(new UsdmImportNote(Usdm.Id(element), message));
}
