using System;

namespace Cadence.Compliance;

public class TamperDetectedException : Exception
{
    public bool IsValid { get; }
    public string Status { get; }

    public TamperDetectedException(string message, bool isValid = false, string status = "TAMPERED_INVALID_HASH")
        : base(message)
    {
        IsValid = isValid;
        Status = status;
    }
}
