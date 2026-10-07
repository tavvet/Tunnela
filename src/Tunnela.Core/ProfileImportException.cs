namespace Tunnela.Core;

/// <summary>Only fixed messages and field names may be included; never attach parser exceptions or input.</summary>
public sealed class ProfileImportException(string message) : FormatException(message)
{
}
