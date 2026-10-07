using Tunnela.Contracts.Localization;

namespace Tunnela.Service;

/// <summary>A fixed catalog code, never an OS exception or text containing profile data.</summary>
internal sealed record ServiceOperationError(string Code)
{
    public string Message => Messages.GetEnglish(Code);
}
