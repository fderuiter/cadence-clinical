using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Cadence.Kernel.Evidence;
using Cadence.Modules.Intelligence.Grounding;
using Cadence.Modules.Intelligence.Tools.Kernel;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>
/// Source-generated JSON metadata for tool arguments and results, the agent's structured output and the plan document
/// (ADR-2633). camelCase, enums as strings, nulls written so a reader sees every field.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true, AllowTrailingCommas = false)]
[JsonSerializable(typeof(NodeArguments))]
[JsonSerializable(typeof(SearchArguments))]
[JsonSerializable(typeof(TraceArguments))]
[JsonSerializable(typeof(CanIArguments))]
[JsonSerializable(typeof(SimulateArguments))]
[JsonSerializable(typeof(NodeView))]
[JsonSerializable(typeof(ChildrenView))]
[JsonSerializable(typeof(SearchView))]
[JsonSerializable(typeof(LineageView))]
[JsonSerializable(typeof(ExplanationView))]
[JsonSerializable(typeof(CanIView))]
[JsonSerializable(typeof(SimulationView))]
[JsonSerializable(typeof(ObservationValue))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(AgentRunOutput))]
[JsonSerializable(typeof(PlanDocument))]
public sealed partial class IntelligenceToolsJsonContext : JsonSerializerContext;
