using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Tunnela.Contracts;

namespace Tunnela.Desktop;

internal sealed class ProfileStore
{
    private const int MaxStoredBytes = 4 * 1024 * 1024;
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tunnela");
    private string FilePath => Path.Combine(_directory, "profiles.dat");

    public ProfileCollection Load()
    {
        if (!File.Exists(FilePath)) return new ProfileCollection();
        var file = new FileInfo(FilePath);
        if (file.Length > MaxStoredBytes) throw new InvalidDataException();
        byte[] plaintext = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
        try
        {
            var result = JsonSerializer.Deserialize<ProfileCollection>(plaintext) ?? throw new InvalidDataException();
            if (result.SchemaVersion != 1 || result.Profiles is null || result.Preferences is null || result.Profiles.Any(p => p is null) || result.Profiles.Select(p => p.Id).Distinct().Count() != result.Profiles.Count) throw new InvalidDataException();
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public void Save(ProfileCollection collection)
    {
        Directory.CreateDirectory(_directory);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        using var identity = WindowsIdentity.GetCurrent();
        var current = identity.User ?? throw new UnauthorizedAccessException();
        foreach (var sid in new[] { current, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(_directory).SetAccessControl(security);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(collection);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        if (encrypted.Length > MaxStoredBytes) throw new InvalidDataException();
        string temporary = Path.Combine(_directory, $"profiles.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(encrypted);
                stream.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
