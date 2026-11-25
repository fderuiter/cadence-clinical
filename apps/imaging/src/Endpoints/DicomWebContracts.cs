using Cadence.Apps.Imaging.Domain;

namespace Cadence.Apps.Imaging.Endpoints;

public record CertificateResponse(
    DeidentificationCertificate Certificate,
    bool IsSignatureValid);
