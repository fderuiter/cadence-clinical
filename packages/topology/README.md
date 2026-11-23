# Cadence.Topology

The service topology manifest (#5315): the one list of the services behind the gateway, with each one's name, port,
address variable, gateway route prefixes, OpenAPI/diff schema prefix, `Program` entry-point type name, hosted in-process
modules, and whether it needs the database and runs in the AppHost's `core` profile, together with repository-root
discovery (`RepositoryRoot`) and shared MSBuild service references (`ServiceReferences.props`).

## Project

- Project: `packages/topology/src/Cadence.Topology.csproj` (assembly `Cadence.Topology`, namespace `Cadence.Topology`).
- `ServiceTopology.Services` lists every service as a `CadenceService` (including `SchemaPrefix`, `ProgramTypeName`, and
  `HostedModules` of `HostedModuleDescriptor`); `ServiceTopology.Get(name)` finds one, and `ServiceTopology.GatewayPort`
  is the gateway's port (8000).
- `RepositoryRoot.Find(startDirectory)` walks upward for `Cadence.slnx` (falling back to `Cadence.sln` or `global.json`
  for isolated test directories), and `RepositoryRoot.ResolveSolutionFileName(repoRoot)` returns the solution file name
  present at a root.
- `packages/topology/ServiceReferences.props` lists the `<ProjectReference>` items for the gateway and every service in
  `ServiceTopology.Services`, shared by `Cadence.OpenApiExporter`, `Cadence.SchemaDocExporter`, and `Cadence.UnitTests`.
- No NuGet or project dependencies. Every public member has an XML comment (`CadenceRequireXmlDocs`).

## Referenced by

- `apps/gateway`: builds its YARP routes (`<service>-<prefix>-route`) and clusters (`<service>-cluster`) from the
  manifest, and in Development falls back to each service's local port.
- `apps/apphost`: runs each service on its port, gives the database to those that need it, and tells the gateway
  where each service is (a service the `core` profile leaves out points at its usual port).
- `packages/tooling_core`, `packages/cli`, `packages/compliance`, `packages/testing`, and `tests/`: use
  `RepositoryRoot.Find`, `ServiceTopology.Services`, and `ServiceReferences.props` for OpenAPI export, EF Core schema
  export, pre-commit diff scoping, port checking, and gateway/security host tests.

## Changing the topology

Add or change a service here and in `packages/topology/ServiceReferences.props`, then update what can't read C#:
`docker/docker-compose.yml`, the service tables in
[ARCHITECTURE.md §3](../../ARCHITECTURE.md#3-running-services-today), [docs/LOCAL_DEV_ENVIRONMENT.md](../../docs/LOCAL_DEV_ENVIRONMENT.md),
[docs/ENVIRONMENT_VARIABLES.md](../../docs/ENVIRONMENT_VARIABLES.md) and the [AppHost README](../../apps/apphost/README.md).
A new service also needs its `AddProject` line in the AppHost.

## Tests

`tests/Cadence.UnitTests/Topology/ServiceTopologyTests.cs` checks the manifest, `Cadence.slnx`, `RepositoryRoot`, and
`ServiceReferences.props` for consistency and fails when docker compose or one of those docs disagrees with it.
`AppHostModelTests` and `GatewayRouteCoverageTests` check the AppHost and the gateway built from it.
