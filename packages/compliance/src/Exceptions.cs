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

public class UnapprovedAIRecordException : Exception
{
    public string ReviewStatus { get; }

    public UnapprovedAIRecordException(
        string message = "AI-generated record is in draft/pending state and cannot be treated as active clinical trial execution data.",
        string reviewStatus = "DRAFT_AI")
        : base(message)
    {
        ReviewStatus = reviewStatus;
    }
}
