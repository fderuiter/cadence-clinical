# Mapping Specifications

How to write, pin and use a mapping specification: the map from the Concepts a study collects to the variables of a
standard's datasets. The model is in spec §6.3 and
[ADR-2463](../adr/2026-09-30-mapping-specifications-as-governed-concept-to-variable-maps.md) (K028). The format is
read by `MappingSpecificationJson` in `Cadence.Kernel.Abstractions` (namespace `Cadence.Kernel.Standards.Mapping`), and
the .NET engine is `MappingEngine` in [`Cadence.Standards`](../../packages/kernel/src/Cadence.Standards/README.md).

## Lifecycle

1. Import the CT package the specification uses into the tenant's Concept Registry, and register and approve the
   standard version it maps into (ADR-2460).
2. Draft it with `DraftLibraryItem`: kind `MappingSpecification`, a key, the content below, and **exactly one** standard
   version in `StandardVersions`. Drafting refuses content that does not resolve (see [Checks](#checks)) and links the
   standard version, the CT versions and the Concept versions as `DefinedBy` lineage inputs of the drafted version.
3. Approve it with `ApproveLibraryItem` and an `Approved` attestation over its content hash.
4. Pin it in the study's `DeclareStudyStandards`, alongside the standard version it maps into.
5. Submit the protocol version. K-STD-1 checks that every Concept its CollectionSpec items record has a mapping (see
   [K-STD-1](#k-std-1-mapping-before-first-data-capture)). When the study pins a rule pack, run
   [design-time conformance](DESIGN_CONFORMANCE.md) on the draft first; it also checks that each mapping can write a
   conformant unit, type, codelist and value for the items it covers (K-STD-2).

A change is a new version of the same key. Approved content never changes.

## Format

```json
{
  "title": "SDTMIG 3.4 vital signs and labs",
  "controlledTerminology": [ { "codeSystem": "sdtmct", "version": "2024-09-27" } ],
  "units": [
    { "ucum": "mm[Hg]", "submissionValue": "mmHg" },
    { "ucum": "kPa", "submissionValue": "kPa" },
    { "ucum": "mg/dL", "submissionValue": "mg/dL" },
    { "ucum": "mmol/L", "submissionValue": "mmol/L" }
  ],
  "mappings": [
    {
      "id": "vs-sysbp",
      "concept": { "id": "0192a6b1-0000-7000-8000-000000000001", "version": 1 },
      "dataset": "VS",
      "assign": { "VSTESTCD": "SYSBP", "VSTEST": "Systolic Blood Pressure" },
      "result": { "original": "VSORRES", "originalUnit": "VSORRESU",
                  "standardCharacter": "VSSTRESC", "standardNumeric": "VSSTRESN", "standardUnit": "VSSTRESU" },
      "standardUnit": "mm[Hg]",
      "conversions": [ { "from": "kPa", "to": "mm[Hg]", "factor": 7.50062 } ],
      "decimals": 0
    },
    {
      "id": "lb-gluc",
      "concept": { "id": "0192a6b1-0000-7000-8000-000000000002", "version": 2 },
      "dataset": "LB",
      "assign": { "LBTESTCD": "GLUC", "LBTEST": "Glucose" },
      "result": { "original": "LBORRES", "originalUnit": "LBORRESU",
                  "standardCharacter": "LBSTRESC", "standardNumeric": "LBSTRESN", "standardUnit": "LBSTRESU" },
      "standardUnit": "mmol/L",
      "conversions": [ { "from": "mg/dL", "to": "mmol/L", "factor": 0.0555 } ],
      "decimals": 2
    },
    {
      "id": "vs-position",
      "concept": { "id": "0192a6b1-0000-7000-8000-000000000003", "version": 1 },
      "dataset": "VS",
      "result": { "original": "VSPOS" },
      "codelist": "C78431"
    }
  ]
}
```

| Property | Meaning |
| --- | --- |
| `title` | What the specification maps, for people. |
| `overrides` | Optional. The key of the base specification this one overrides (see [Study overrides](#study-overrides)). |
| `controlledTerminology` | The CT versions the decodes and unit terms come from: a CDISC CT package type (`sdtmct`) or a code-system URI, with its version. One version per code system. |
| `units` | Each UCUM unit the specification writes, with the standard's term for it (the CT UNIT submission value). |
| `mappings` | One mapping per Concept, 1 to 5000. |
| `mappings[].id` | The mapping's id within the specification. |
| `mappings[].concept` | The Concept (`id`, a UUID) at the version the mapping was authored against. The mapping applies to every version of that Concept. |
| `mappings[].dataset` | The dataset (SDTM domain, …) the record belongs to. |
| `mappings[].assign` | Constant variables that identify the record, such as the test code. This is where value-level metadata goes. |
| `mappings[].result` | Where the result goes: `original` (required) and optionally `originalUnit`, `standardCharacter`, `standardNumeric`, `standardUnit`. |
| `mappings[].standardUnit` | The UCUM unit standardised results are expressed in. Required with `standardNumeric`, `standardUnit` or conversions. |
| `mappings[].conversions` | Linear conversions into the standard unit: `standard = value × factor + offset` (`offset` defaults to 0). |
| `mappings[].decimals` | Decimal places of the standardised result (0 to 10). Omit it to write the converted value exactly, without trailing zeros. |
| `mappings[].codelist` | For a coded result: the NCI C-code of the codelist the code is decoded against. A coded result is not a quantity. |

Dataset and variable names are upper-case letters, digits and `_`, starting with a letter. Conversion factors are the
sponsor's stated data: the engine never derives one from UCUM.

## How the engine maps a value

`IMappingEngine.Map(specification, input, decoder)` takes the Concept, the value as invariant text and its UCUM unit,
and returns the dataset and the variables of the record fragment:

- The `assign` variables get their constants, in variable-name order.
- `original` gets the value as collected; `originalUnit` gets the unit's term from `units`.
- A number with a standard unit is converted into it, rounded to `decimals` (half away from zero), and written to
  `standardCharacter` and `standardNumeric`; `standardUnit` gets the standard unit's term.
- A bound such as `<1.0` or `>=200` goes to `standardCharacter` as collected, with no numeric value or unit. Other
  text collected with a unit, under a mapping that standardises, is a problem.
- A coded result is decoded to its CT submission value, written to `original` and `standardCharacter`.
- A value that was not collected writes the constants and empty results.

The engine never guesses. A missing mapping, unit, conversion, unit term or decode, a quantity that is neither a number nor a bound, or a
conversion out of range is a problem, and a result with a
problem writes no fragment. For example, systolic blood pressure `120 mm[Hg]` maps to `VSTESTCD=SYSBP`, `VSORRES=120`,
`VSORRESU=mmHg`, `VSSTRESC=120`, `VSSTRESN=120`, `VSSTRESU=mmHg`; glucose `90 mg/dL` maps to `LBSTRESN=5.00`,
`LBSTRESU=mmol/L`.

## Checks

The reader refuses:

- unknown properties;
- a Concept mapped twice;
- a variable written twice;
- a conversion that does not lead into the standard unit;
- a unit the mapping writes without a unit term.

Drafting also refuses a specification when:

- it names anything but exactly one standard version;
- a dataset or variable does not exist in that standard version;
- a CT version is not imported into the tenant;
- a codelist is in none of its CT versions;
- a Concept version does not exist;
- `overrides` names a key that is not a mapping specification.

The reader also refuses a coded result (`codelist`) that writes a unit or a numeric result.

## Study overrides

A study-level override is its own versioned specification that names its base in `overrides`. The study pins both.
The override's mappings replace the base's mappings for the same Concepts and add the rest. `DeclareStudyStandards`
refuses the pins when:

- a specification maps into a standard version the study does not declare;
- two versions of the same specification are pinned;
- an override's base is not pinned, is itself an override, or maps into another standard version;
- two specifications map the same Concept in the same standard version, other than an override replacing its own
  base's mapping.

## K-STD-1: mapping before first data capture

Submitting a draft protocol version for review checks, for a study that has declared standards, that every Concept its
CollectionSpec items record has a mapping in the pinned specifications. The submission is refused with
`K.Standards.MappingMissing` otherwise.

A gap may be accepted instead. Record it as a Design-stage finding on `MappingGap.Target(concept)`: a `DesignElement`
target with dataset `CollectionSpec` and the Concept's NodeRef without a version. Design-time conformance records
exactly this finding (rule DC-001, [DESIGN_CONFORMANCE.md](DESIGN_CONFORMANCE.md)). Then accept it with `AcceptFinding`
and a `Responsibility` attestation. The acceptance covers the Concept in every protocol version of the study.

When the check passes, the protocol version records each mapping specification version it relies on as a `MappedBy`
lineage input.
