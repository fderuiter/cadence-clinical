# Schedule of Activities test fixtures

Expected output of the Schedule of Activities compiler and amendment diff
(`tests/Cadence.UnitTests/Kernel/Protocol/ScheduleOfActivitiesTests.cs`,
[ADR-2475](../../../docs/adr/2026-09-30-schedule-of-activities-as-a-rendering-with-typed-amendment-directives.md)).
The input designs are in `tests/Cadence.TestSupport/Kernel/Protocol/SoaSamples.cs`.

| File | What it is |
| --- | --- |
| `cdisc_usdm4_full_1.soa.json` | The matrix of the CDISC USDM 4 example study, from its imported design in [`../usdm`](../usdm/README.md) |
| `cardio_2.soa.json` | The matrix of CARDIO-2: a telehealth screening call, windowed visits, an event-driven visit, an operational need and an unscheduled specification |
| `cdisc_usdm4_full_1.amendment.diff.json` | The directives for an amendment of the example study: a visit moved and relocated, a window widened, a visit added, an activity dropped from one visit, a criterion reworded and one added |

Each file was checked by hand against its design when it was added. When the compiler or the diff changes on purpose,
regenerate them with
`CADENCE_UPDATE_GOLDEN=1 dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~ScheduleOfActivitiesTests"`
and review the diff.
