# Cadence.Topology

The service topology manifest (#5315): the one list of the services behind the gateway, with each one's name, port,
address variable, gateway route prefixes, and whether it needs the database and runs in the AppHost's `core` profile.

## Project

- Project: `packages/topology/src/Cadence.Topology.csproj` (assembly `Cadence.Topology`, namespace `Cadence.Topology`).
- `ServiceTopology.Services` lists every service as a `CadenceService`; `ServiceTopology.Get(name)` finds one, and
  `ServiceTopology.GatewayPort` is the gateway's port (8000).
- No dependencies. Every public member has an XML comment (`CadenceRequireXmlDocs`).

## Referenced by

- `apps/gateway`: builds its YARP routes (`<service>-<prefix>-route`) and clusters (`<service>-cluster`) from the
  manifest, and in Development falls back to each service's local port.
- `apps/apphost`: runs each service on its port, gives the database to those that need it, and tells the gateway
  where each service is (a service the `core` profile leaves out points at its usual port).
- `packages/tooling_core`: `PortCheckerService` checks the gateway's and services' ports.

## Changing the topology

Add or change a service here, then update what can't read C#: `docker/docker-compose.yml`, the service tables in
[ARCHITECTURE.md §3](../../ARCHITECTURE.md#3-running-services-today), [docs/LOCAL_DEV_ENVIRONMENT.md](../../docs/LOCAL_DEV_ENVIRONMENT.md),
[docs/ENVIRONMENT_VARIABLES.md](../../docs/ENVIRONMENT_VARIABLES.md) and the [AppHost README](../../apps/apphost/README.md).
A new service also needs its `AddProject` line in the AppHost and its entry in `GatewayRouteCoverageTests`.

## Tests

`tests/Cadence.UnitTests/Topology/ServiceTopologyTests.cs` checks the manifest is consistent and fails when docker
compose or one of those docs disagrees with it. `AppHostModelTests` and `GatewayRouteCoverageTests` check the AppHost
and the gateway built from it.
