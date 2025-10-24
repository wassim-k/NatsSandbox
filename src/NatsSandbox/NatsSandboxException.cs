namespace NatsSandbox;

/// <summary>
/// Exception thrown when an error occurs in NatsSandbox operations.
/// </summary>
public sealed class NatsSandboxException : Exception
{
    public NatsSandboxException(string message) : base(message)
    {
    }

    public NatsSandboxException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
