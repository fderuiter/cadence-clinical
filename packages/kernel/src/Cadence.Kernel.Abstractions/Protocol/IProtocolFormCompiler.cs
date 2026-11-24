using Cadence.Kernel.Protocol.Design;

namespace Cadence.Kernel.Protocol;

/// <summary>
/// Immutable compiled form artifacts derived from an approved <see cref="ProtocolDesign"/> (ADR-2809).
/// </summary>
/// <param name="FormId">Deterministic form snapshot identifier within the study and protocol version.</param>
/// <param name="FormHash">Lowercase hexadecimal SHA-256 digest of <paramref name="OpenRosaXml"/>.</param>
/// <param name="OpenRosaXml">Compiled OpenRosa XForms XML schema.</param>
/// <param name="DdfIrJson">Serialized DDF intermediate representation JSON.</param>
/// <param name="OdmXml">Compiled CDISC ODM v1.3 XML metadata document.</param>
public sealed record CompiledProtocolForm(
    string FormId,
    string FormHash,
    string OpenRosaXml,
    string DdfIrJson,
    string OdmXml);

/// <summary>
/// Kernel port that compiles <see cref="ProtocolDesign.CollectionSpecs"/> into OpenRosa XForms, DDF IR JSON,
/// and ODM XML snapshots upon protocol version approval without coupling the kernel to DDF or templating assemblies.
/// </summary>
public interface IProtocolFormCompiler
{
    /// <summary>
    /// Compiles <paramref name="design"/>'s collection specifications into an immutable form snapshot,
    /// or returns <c>null</c> when <paramref name="design"/> defines no collection specifications.
    /// </summary>
    /// <param name="design">Approved protocol design document.</param>
    /// <param name="studyIdentifier">Study identifier used as the compiled form and ODM study key.</param>
    /// <param name="versionTag">Protocol version number or tag.</param>
    CompiledProtocolForm? Compile(ProtocolDesign design, string studyIdentifier, string versionTag);
}
