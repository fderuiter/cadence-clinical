using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
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

    private UsdmStagingResult Result(ProtocolDesign? design) => new(_problems.Count == 0 ? design : null, [.. _problems], [.. _notes], [.. _unmappedConcepts]);

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
        if (code is { } c && UcumUnits.TryGetValue(c.Code, out var ucum))
        {
            return new Dose(amount, ucum);
        }

        Note(administration, $"dose unit '{code?.Decode}' has no UCUM equivalent here; its CDISC name is kept.");
        return new Dose(amount, code?.Decode ?? string.Empty);
    }

    /// <summary>CDISC Unit codelist (C71620) codes whose UCUM form is certain.</summary>
    private static readonly FrozenDictionary<string, string> UcumUnits = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["C28253"] = "mg",
        ["C48155"] = "g",
        ["C28252"] = "kg",
        ["C48152"] = "ug",
        ["C48531"] = "[lb_av]",
        ["C28254"] = "mL",
        ["C48505"] = "L",
        ["C64783"] = "dL",
        ["C25613"] = "%",
        ["C49670"] = "mm[Hg]",
        ["C49671"] = "{beats}/min",
        ["C49673"] = "{breaths}/min",
        ["C42559"] = "Cel",
        ["C44277"] = "[degF]",
        ["C49668"] = "cm",
        ["C41139"] = "m",
        ["C28251"] = "mm",
        ["C48500"] = "[in_i]",
        ["C29848"] = "a",
        ["C29846"] = "mo",
        ["C29844"] = "wk",
        ["C25301"] = "d",
        ["C25529"] = "h",
        ["C48154"] = "min",
        ["C42535"] = "s",
        ["C67015"] = "mmol/L",
        ["C64572"] = "umol/L",
        ["C67014"] = "mg/dL",
        ["C67306"] = "g/dL",
        ["C67388"] = "U/L",
        ["C48570"] = "IU/L",
        ["C49672"] = "kg/m2",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> UcumAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["kilogram"] = "kg",
        ["kilograms"] = "kg",
        ["kg"] = "kg",
        ["gram"] = "g",
        ["grams"] = "g",
        ["g"] = "g",
        ["milligram"] = "mg",
        ["milligrams"] = "mg",
        ["mg"] = "mg",
        ["microgram"] = "ug",
        ["micrograms"] = "ug",
        ["ug"] = "ug",
        ["mcg"] = "ug",
        ["pound"] = "[lb_av]",
        ["pounds"] = "[lb_av]",
        ["millimeter of mercury"] = "mm[Hg]",
        ["mmhg"] = "mm[Hg]",
        ["mm[hg]"] = "mm[Hg]",
        ["degree celsius"] = "Cel",
        ["celsius"] = "Cel",
        ["cel"] = "Cel",
        ["degree fahrenheit"] = "[degF]",
        ["fahrenheit"] = "[degF]",
        ["centimeter"] = "cm",
        ["centimeters"] = "cm",
        ["cm"] = "cm",
        ["millimeter"] = "mm",
        ["millimeters"] = "mm",
        ["mm"] = "mm",
        ["meter"] = "m",
        ["meters"] = "m",
        ["m"] = "m",
        ["milliliter"] = "mL",
        ["milliliters"] = "mL",
        ["ml"] = "mL",
        ["liter"] = "L",
        ["liters"] = "L",
        ["l"] = "L",
        ["deciliter"] = "dL",
        ["deciliters"] = "dL",
        ["dl"] = "dL",
        ["percent"] = "%",
        ["percentage"] = "%",
        ["%"] = "%",
        ["beats per minute"] = "{beats}/min",
        ["bpm"] = "{beats}/min",
        ["breaths per minute"] = "{breaths}/min",
        ["year"] = "a",
        ["years"] = "a",
        ["month"] = "mo",
        ["months"] = "mo",
        ["week"] = "wk",
        ["weeks"] = "wk",
        ["day"] = "d",
        ["days"] = "d",
        ["hour"] = "h",
        ["hours"] = "h",
        ["minute"] = "min",
        ["minutes"] = "min",
        ["second"] = "s",
        ["seconds"] = "s",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

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
                if (await ItemAsync(id, bcId, resolve, resolveTerminology, resolved, resolvedTerminology, cancellationToken).ConfigureAwait(false) is { } item)
                {
                    items.Add(item);
                }
            }

            specs.Add(new CollectionSpec(id, Usdm.Str(activity, "name") ?? string.Empty, items));
        }

        return specs;
    }

    private async Task<CollectionItem?> ItemAsync(
        string activityId,
        string bcId,
        BiomedicalConceptResolver resolve,
        ProtocolTerminologyResolver? resolveTerminology,
        Dictionary<string, ConceptRef?> resolved,
        Dictionary<string, ProtocolTerminologyResolution> resolvedTerminology,
        CancellationToken cancellationToken)
    {
        if (!_biomedicalConcepts.TryGetValue(bcId, out var bc))
        {
            _problems.Add($"{activityId}: Biomedical Concept '{bcId}' is not in the study version.");
            return null;
        }

        var code = Usdm.Code(bc.TryGetProperty("code", out var alias) ? alias : default)?.Code;
        if (code is null)
        {
            _problems.Add($"{bcId}: the Biomedical Concept has no code.");
            return null;
        }

        if (!resolved.TryGetValue(code, out var concept))
        {
            concept = await resolve(code, cancellationToken).ConfigureAwait(false);
            resolved[code] = concept;
        }

        if (concept is not { } found)
        {
            _unmappedConcepts.Add(new UsdmUnmappedConceptToken(code, Usdm.Str(bc, "name") ?? code, $"{activityId}.{bcId}"));
            return null;
        }

        var itemId = $"{activityId}.{bcId}";
        var enabledProps = Usdm.Arr(bc, "properties").Where(p => !IsExplicitlyDisabled(p)).ToList();
        if (HasItemRulesOrConditions(bc) || enabledProps.Any(HasItemRulesOrConditions))
        {
            _notes.Add(new UsdmImportNote(itemId, "item rules and conditions on Biomedical Concept properties are not carried as executable rules."));
        }

        JsonElement? primaryProp = SelectPrimaryProperty(enabledProps, code);
        bool required = (primaryProp is { } reqProp ? ReadBool(reqProp, "isRequired") : null)
            ?? ReadBool(bc, "isRequired")
            ?? false;

        string? unit = null;
        string? carriedUnitPropId = null;
        if (primaryProp is { } explicitPrimary && TryReadExplicitUnit(explicitPrimary, itemId, out var primaryUnit))
        {
            unit = primaryUnit;
        }
        else if (TryReadExplicitUnit(bc, itemId, out var bcUnit))
        {
            unit = bcUnit;
        }
        else if (primaryProp is { } chosenPrimary)
        {
            var unitProps = enabledProps
                .Where(p => !IsSameProperty(p, chosenPrimary) && IsUnitProperty(p))
                .ToList();
            if (unitProps.Count > 0)
            {
                var unitProp = unitProps[0];
                if (TryReadExplicitUnit(unitProp, itemId, out var propExplicitUnit))
                {
                    unit = propExplicitUnit;
                    carriedUnitPropId = Usdm.Id(unitProp);
                }
                else
                {
                    var unitCodes = ReadEnabledResponseCodes(unitProp);
                    if (unitCodes.Count == 1)
                    {
                        var single = unitCodes[0];
                        if (TryResolveUcumUnit(single.Code, single.Decode, allowArbitraryUcumExpression: false, out var mappedUcum))
                        {
                            unit = mappedUcum;
                            carriedUnitPropId = Usdm.Id(unitProp);
                        }
                        else
                        {
                            _notes.Add(new UsdmImportNote(
                                itemId,
                                $"unit '{single.Decode}' ({single.Code}) on '{PropertyLabel(unitProp)}' has no canonical UCUM equivalent; imported item without fixed unit constraint."));
                        }
                    }
                }
            }
        }

        TerminologyBinding? terminology = null;
        var responseSource = primaryProp ?? bc;
        var responseCodes = ReadEnabledResponseCodes(responseSource);
        if (responseCodes.Count > 0)
        {
            var codelistCode = FirstNonBlank(
                PropertyStandardCode(responseSource),
                VariableStem(Usdm.Text(responseSource, "name")),
                Usdm.Id(responseSource),
                code)!;
            var codelistName = FirstNonBlank(
                Usdm.Text(responseSource, "label"),
                Usdm.Text(responseSource, "name"),
                Usdm.Text(bc, "label"),
                Usdm.Text(bc, "name"),
                codelistCode)!;

            if (resolveTerminology is not null)
            {
                var groupCode = OdmDesignImporter.BoundTerminologyToken(codelistCode);
                var entries = new List<CodeSystemEntryData>(responseCodes.Count);
                var seenCodes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var rc in responseCodes)
                {
                    var boundedCode = OdmDesignImporter.BoundTerminologyToken(rc.Code)!;
                    if (!seenCodes.Add(boundedCode))
                    {
                        continue;
                    }

                    entries.Add(new CodeSystemEntryData(
                        Code: boundedCode,
                        Display: FirstNonBlank(rc.Decode, rc.Label, rc.Code)!,
                        GroupCode: groupCode,
                        SubmissionValue: !string.Equals(boundedCode, rc.Code, StringComparison.Ordinal) ? rc.Code : null));
                }

                var primarySystem = responseCodes.Select(r => r.CodeSystem).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                var primaryVersion = responseCodes.Select(r => r.CodeSystemVersion).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                var cacheKey = $"{codelistCode}|{primarySystem}|{primaryVersion}|{string.Join(",", entries.Select(e => e.Code))}";

                if (!resolvedTerminology.TryGetValue(cacheKey, out var termResolution))
                {
                    termResolution = await resolveTerminology(
                        new ProtocolTerminologyCandidate(
                            SourceKey: $"usdm:{bcId}:{Usdm.Id(responseSource)}",
                            CodelistCode: codelistCode,
                            CodelistName: codelistName,
                            CodeSystemUri: primarySystem,
                            CodeSystemName: primarySystem,
                            CodeSystemVersion: primaryVersion,
                            Entries: entries),
                        cancellationToken).ConfigureAwait(false);
                    resolvedTerminology[cacheKey] = termResolution;
                }

                if (!string.IsNullOrWhiteSpace(termResolution.ErrorMessage))
                {
                    _problems.Add($"{itemId}: {termResolution.ErrorMessage}");
                    return null;
                }

                terminology = termResolution.Binding;
            }
            else
            {
                _notes.Add(new UsdmImportNote(
                    itemId,
                    $"responseCodes ({responseCodes.Count}) on '{codelistName}' were not bound to a code-system version because no terminology resolver was supplied."));
            }
        }

        if (terminology is not null)
        {
            unit = null;
        }

        var rawDatatype = (primaryProp is { } dtProp ? Usdm.Text(dtProp, "datatype") : null) ?? Usdm.Text(bc, "datatype");
        var valueKind = terminology is not null
            ? ObservationValueKind.Coded
            : MapUsdmDatatype(rawDatatype, unit, itemId);

        if (primaryProp is { } selectedPrimary)
        {
            var uncarriedQualifiers = enabledProps
                .Where(p => !IsSameProperty(p, selectedPrimary) && (carriedUnitPropId is null || !string.Equals(Usdm.Id(p), carriedUnitPropId, StringComparison.Ordinal)))
                .Select(PropertyLabel)
                .ToList();
            if (uncarriedQualifiers.Count > 0)
            {
                _notes.Add(new UsdmImportNote(
                    itemId,
                    $"qualifier properties ({string.Join(", ", uncarriedQualifiers)}) are not carried; ADR-2473 keeps one item per Biomedical Concept."));
            }
        }

        return new CollectionItem(
            itemId,
            found,
            Unit: unit,
            Terminology: terminology,
            Required: required,
            Purpose: null,
            ValueKind: valueKind);
    }

    private static JsonElement? SelectPrimaryProperty(IReadOnlyList<JsonElement> enabledProps, string bcCode)
    {
        if (enabledProps.Count == 0)
        {
            return null;
        }

        foreach (var prop in enabledProps)
        {
            var stdCode = PropertyStandardCode(prop);
            var stem = VariableStem(Usdm.Text(prop, "name"));
            if (string.Equals(stdCode, "C70856", StringComparison.Ordinal)
                || (stem is not null
                    && (stem.EndsWith("ORRES", StringComparison.OrdinalIgnoreCase)
                        || stem.EndsWith("AVAL", StringComparison.OrdinalIgnoreCase))))
            {
                return prop;
            }
        }

        foreach (var prop in enabledProps)
        {
            if (!IsUnitProperty(prop)
                && !IsTimingOrTopicIdentifierProperty(prop, bcCode)
                && string.Equals(PropertyStandardCode(prop), bcCode, StringComparison.Ordinal))
            {
                return prop;
            }
        }

        foreach (var prop in enabledProps)
        {
            if (!IsUnitProperty(prop)
                && !IsTimingOrTopicIdentifierProperty(prop, bcCode)
                && (!string.IsNullOrWhiteSpace(Usdm.Text(prop, "datatype")) || ReadEnabledResponseCodes(prop).Count > 0))
            {
                return prop;
            }
        }

        foreach (var prop in enabledProps)
        {
            if (!IsUnitProperty(prop) && !IsTimingOrTopicIdentifierProperty(prop, bcCode))
            {
                return prop;
            }
        }

        return enabledProps[0];
    }

    private ObservationValueKind? MapUsdmDatatype(string? rawDatatype, string? unit, string itemId)
    {
        if (string.IsNullOrWhiteSpace(rawDatatype))
        {
            return unit is not null ? ObservationValueKind.Quantity : null;
        }

        switch (rawDatatype.Trim().ToLowerInvariant())
        {
            case "decimal":
            case "float":
            case "double":
            case "number":
            case "quantity":
            case "integer":
            case "int":
                return ObservationValueKind.Quantity;
            case "boolean":
            case "bool":
                return ObservationValueKind.Boolean;
            case "date":
            case "datetime":
            case "time":
            case "partialdate":
            case "partialtime":
            case "partialdatetime":
            case "durationdatetime":
                return ObservationValueKind.DateTime;
            case "string":
            case "text":
            case "uri":
                return unit is not null ? ObservationValueKind.Quantity : ObservationValueKind.Text;
            default:
                _notes.Add(new UsdmImportNote(itemId, $"datatype '{rawDatatype}' is not recognized and was approximated as Text."));
                return unit is not null ? ObservationValueKind.Quantity : ObservationValueKind.Text;
        }
    }

    private bool TryReadExplicitUnit(JsonElement container, string itemId, out string? unit)
    {
        unit = null;
        if (container.ValueKind != JsonValueKind.Object || !container.TryGetProperty("unit", out var unitProp))
        {
            return false;
        }

        if (unitProp.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (unitProp.ValueKind == JsonValueKind.String)
        {
            var raw = unitProp.GetString()?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            if (TryResolveUcumUnit(null, raw, allowArbitraryUcumExpression: true, out var resolved))
            {
                unit = resolved;
                return true;
            }

            _notes.Add(new UsdmImportNote(itemId, $"unit '{raw}' is not a valid UCUM expression; imported item without fixed unit constraint."));
            return true;
        }

        if (unitProp.ValueKind == JsonValueKind.Object && Usdm.Code(unitProp) is { } unitCode)
        {
            if (TryResolveUcumUnit(unitCode.Code, unitCode.Decode, allowArbitraryUcumExpression: true, out var resolved))
            {
                unit = resolved;
                return true;
            }

            _notes.Add(new UsdmImportNote(itemId, $"unit '{unitCode.Decode}' ({unitCode.Code}) has no canonical UCUM equivalent; imported item without fixed unit constraint."));
            return true;
        }

        return false;
    }

    private static bool TryResolveUcumUnit(
        string? cdiscCode,
        string? decodeOrSymbol,
        bool allowArbitraryUcumExpression,
        out string? ucum)
    {
        ucum = null;
        if (!string.IsNullOrWhiteSpace(cdiscCode) && UcumUnits.TryGetValue(cdiscCode.Trim(), out var byCode))
        {
            ucum = byCode;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(decodeOrSymbol))
        {
            var trimmed = decodeOrSymbol.Trim();
            if (UcumAliases.TryGetValue(trimmed, out var byAlias))
            {
                ucum = byAlias;
                return true;
            }

            if (allowArbitraryUcumExpression && Ucum.Problem(trimmed) is null)
            {
                ucum = trimmed;
                return true;
            }
        }

        return false;
    }

    private static List<UsdmResponseCodeItem> ReadEnabledResponseCodes(JsonElement element)
    {
        var list = new List<UsdmResponseCodeItem>();
        foreach (var rc in Usdm.Arr(element, "responseCodes"))
        {
            if (IsExplicitlyDisabled(rc))
            {
                continue;
            }

            var codeContainer = Usdm.Obj(rc, "code") ?? rc;
            if (codeContainer.ValueKind == JsonValueKind.Object && Usdm.Obj(codeContainer, "standardCode") is { } std)
            {
                codeContainer = std;
            }

            var code = Usdm.Text(codeContainer, "code");
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var decode = FirstNonBlank(Usdm.Text(codeContainer, "decode"), Usdm.Text(rc, "label"), Usdm.Text(rc, "name"), code)!;
            var label = FirstNonBlank(Usdm.Text(rc, "label"), Usdm.Text(rc, "name"));
            var system = Usdm.Text(codeContainer, "codeSystem");
            var version = Usdm.Text(codeContainer, "codeSystemVersion");
            list.Add(new UsdmResponseCodeItem(code, decode, label, system, version));
        }

        return list;
    }

    private static bool IsUnitProperty(JsonElement prop)
    {
        var stdCode = PropertyStandardCode(prop);
        if (stdCode is "C48208" or "C49669" or "C50400" or "C42578" or "C25709")
        {
            return true;
        }

        var stem = VariableStem(Usdm.Text(prop, "name"));
        if (stem is not null
            && (stem.EndsWith("ORRESU", StringComparison.OrdinalIgnoreCase)
                || stem.EndsWith("STRESU", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var label = Usdm.Text(prop, "label") ?? Usdm.Text(prop, "name");
        return label is not null
            && (string.Equals(label, "Unit", StringComparison.OrdinalIgnoreCase)
                || label.EndsWith(" Unit", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTimingOrTopicIdentifierProperty(JsonElement prop, string bcCode)
    {
        var stdCode = PropertyStandardCode(prop);
        if (string.Equals(stdCode, "C82515", StringComparison.Ordinal))
        {
            return true;
        }

        var stem = VariableStem(Usdm.Text(prop, "name"));
        if (stem is null)
        {
            return false;
        }

        if (stem.EndsWith("DTC", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith("TESTCD", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(stdCode, bcCode, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(Usdm.Text(prop, "datatype"))
            && ReadEnabledResponseCodes(prop).Count == 0
            && stem.EndsWith("TEST", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasItemRulesOrConditions(JsonElement element) =>
        Usdm.Arr(element, "rules").Count > 0
        || Usdm.Arr(element, "conditions").Count > 0
        || Usdm.Strs(element, "conditionIds").Count > 0
        || HasNonEmptyField(element, "rule")
        || HasNonEmptyField(element, "condition");

    private static bool HasNonEmptyField(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var val)
        && val.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(val.GetString()),
            JsonValueKind.Object => true,
            JsonValueKind.Array => val.GetArrayLength() > 0,
            _ => false,
        };

    private static bool IsExplicitlyDisabled(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("isEnabled", out var flag)
        && flag.ValueKind == JsonValueKind.False;

    private static bool? ReadBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var prop)
            ? prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private static bool IsSameProperty(JsonElement left, JsonElement right) =>
        string.Equals(Usdm.Id(left), Usdm.Id(right), StringComparison.Ordinal)
        && string.Equals(Usdm.Str(left, "name"), Usdm.Str(right, "name"), StringComparison.Ordinal);

    private static string? PropertyStandardCode(JsonElement prop) =>
        Usdm.Obj(prop, "code") is { } codeObj ? Usdm.Code(codeObj)?.Code : null;

    private static string PropertyLabel(JsonElement prop) =>
        FirstNonBlank(VariableStem(Usdm.Text(prop, "name")), Usdm.Text(prop, "label"), Usdm.Text(prop, "name"), Usdm.Id(prop)) ?? "property";

    private static string? VariableStem(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var trimmed = rawName.Trim();
        var parenIdx = trimmed.IndexOf(" (", StringComparison.Ordinal);
        return parenIdx > 0 ? trimmed[..parenIdx].Trim() : trimmed;
    }

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    private sealed record UsdmResponseCodeItem(
        string Code,
        string Decode,
        string? Label,
        string? CodeSystem,
        string? CodeSystemVersion);

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

    /// <summary>
    /// The element's <c>text</c> as written. USDM tags in it (<c>&lt;usdm:tag&gt;</c>, <c>&lt;usdm:ref&gt;</c>) are
    /// kept and noted: the design does not substitute a study's dictionary values.
    /// </summary>
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
