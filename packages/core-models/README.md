# Cadence.CoreModels

Protocol RAG models and a citation and faithfulness evaluator used by the compliance package.

## Project

- Project: `packages/core-models/src/Cadence.CoreModels.csproj` (assembly `Cadence.CoreModels`, namespace `Cadence.CoreModels`).
- `ProtocolRagModels.cs`: `ProtocolRagContextChunk`, `ParsedCitation`.
- `ProtocolRagEvaluator.cs`: citation regex and marker formatting, confidence threshold, and the grounded-answer system prompt.

## Key dependencies

None beyond the .NET base library.

## Referenced by

`packages/compliance` and `tests/Cadence.UnitTests/Cadence.UnitTests.csproj`.

## Tests

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests"
```
