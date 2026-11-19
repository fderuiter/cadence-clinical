namespace Cadence.Testing.Persistence;

/// <summary>
/// The one PostgreSQL major every database test, CI and the docker compose stack run (#5438, ADR-2533). Fixtures that
/// start a container use <see cref="Image"/>; fixtures that connect to a server refuse one of another major.
/// </summary>
public static class PostgresTestImage
{
    /// <summary>The PostgreSQL major version the tests qualify.</summary>
    public const int Major = 18;

    /// <summary>The digest-pinned image, the same one <c>docker/docker-compose.yml</c> runs.</summary>
    public const string Image = "postgres:18-alpine@sha256:10a5c69a83035ab5239f5620d708520e8b28f5aa1f2b20143e6024532edadfb5";

    /// <summary>
    /// Why a server reporting <paramref name="serverVersionNumber"/> (<c>SHOW server_version_num</c>, for example
    /// <c>180001</c>) cannot be used, or <c>null</c> when it runs <see cref="Major"/>.
    /// </summary>
    public static string? Mismatch(int serverVersionNumber) =>
        serverVersionNumber / 10000 == Major
            ? null
            : $"The tests qualify PostgreSQL {Major}, but the server runs {serverVersionNumber / 10000} (server_version_num {serverVersionNumber}).";
}
