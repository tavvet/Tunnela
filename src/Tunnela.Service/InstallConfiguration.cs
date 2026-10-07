using Tunnela.Contracts.Localization;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace Tunnela.Service;

internal sealed record InstallConfiguration
{
    public string ControllerSid { get; init; } = "";
    public string EngineSha256 { get; init; } = "";
    public string WintunSha256 { get; init; } = "";
    public string EngineVersion { get; init; } = "1.1.7";

    public static string InstallDirectory => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
    public static string EnginePath => Path.Combine(InstallDirectory, "engine", "trusttunnel_client.exe");
    public static string WintunPath => Path.Combine(InstallDirectory, "engine", "wintun.dll");
    public static string RuntimeDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tunnela", "Service");
    public static string RuntimeConfigPath => Path.Combine(RuntimeDirectory, "active.toml");
    public static string ProcessRecordPath => Path.Combine(RuntimeDirectory, "engine-session.json");

    public static InstallConfiguration LoadAndValidate()
    {
        if (!WindowsIdentity.GetCurrent().IsSystem)
        {
            throw new InvalidOperationException(Messages.GetEnglish("Service.SystemIdentityRequired"));
        }

        var programFiles = Path.TrimEndingDirectorySeparator(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var expectedInstallation = Path.Combine(programFiles, "Tunnela", "service");
        if (!string.Equals(InstallDirectory, expectedInstallation, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Messages.GetEnglish("Service.InstallLocationRequired"));
        }

        ProtectedFiles.CheckNoReparsePoints(InstallDirectory);
        for (var directory = new DirectoryInfo(InstallDirectory); directory is not null; directory = directory.Parent)
        {
            ProtectedFiles.RequireAdministratorWriteOnly(directory);
            if (string.Equals(directory.FullName, programFiles, StringComparison.OrdinalIgnoreCase)) break;
        }
        var configFile = new FileInfo(Path.Combine(InstallDirectory, "service-install.json"));
        ProtectedFiles.RequireAdministratorWriteOnly(configFile);
        if (configFile.Length > 8192)
        {
            throw new InvalidDataException(Messages.GetEnglish("Service.InstallConfigTooLarge"));
        }

        var config = JsonSerializer.Deserialize<InstallConfiguration>(File.ReadAllText(configFile.FullName))
            ?? throw new InvalidDataException(Messages.GetEnglish("Service.InstallConfigMissing"));
        var controller = new SecurityIdentifier(config.ControllerSid);
        // A specific local/domain user SID, never Everyone, Authenticated Users or a group alias.
        if (!controller.IsAccountSid() || controller.Value.EndsWith("-513", StringComparison.Ordinal))
        {
            throw new InvalidDataException(Messages.GetEnglish("Service.ControllerSidRequired"));
        }
        if (config.EngineVersion != "1.1.7" || !IsSha256(config.EngineSha256) || !IsSha256(config.WintunSha256))
        {
            throw new InvalidDataException(Messages.GetEnglish("Service.EngineManifestUnsupported"));
        }

        ProtectedFiles.EnsureRuntimeDirectory();
        return config;
    }

    public bool VerifyEngine()
    {
        try
        {
            ProtectedFiles.CheckNoReparsePoints(Path.GetDirectoryName(EnginePath)!);
            ProtectedFiles.RequireAdministratorWriteOnly(new DirectoryInfo(Path.GetDirectoryName(EnginePath)!));
            return VerifyFile(EnginePath, EngineSha256) && VerifyFile(WintunPath, WintunSha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SystemException)
        {
            return false;
        }
    }

    private static bool VerifyFile(string path, string expected)
    {
        var file = new FileInfo(path);
        ProtectedFiles.RequireAdministratorWriteOnly(file);
        using var stream = file.OpenRead();
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(expected));
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

internal static class ProtectedFiles
{
    internal static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    internal static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const FileSystemRights WriteRights = FileSystemRights.Write
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles;

    internal static void CheckNoReparsePoints(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(Messages.GetEnglish("Service.ReparsePointForbidden"));
            }
            current = current.Parent;
        }
    }

    internal static void RequireAdministratorWriteOnly(FileSystemInfo info)
    {
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(Messages.GetEnglish("Service.ProtectedFileMissing"));
        }
        FileSystemSecurity security = info is DirectoryInfo directory
            ? directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
            : ((FileInfo)info).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0)
            {
                continue;
            }
            if ((rule.FileSystemRights & WriteRights) != 0 && !IsTrusted(rule.IdentityReference.Value))
            {
                throw new UnauthorizedAccessException(Messages.GetEnglish("Service.ProtectedFileWritable"));
            }
        }
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (owner is null || !IsTrusted(owner.Value))
        {
            throw new UnauthorizedAccessException(Messages.GetEnglish("Service.ProtectedOwnerRequired"));
        }
    }

    internal static void EnsureRuntimeDirectory()
    {
        var root = Path.GetDirectoryName(InstallConfiguration.RuntimeDirectory)!;
        foreach (var path in new[] { root, InstallConfiguration.RuntimeDirectory })
        {
            Directory.CreateDirectory(path);
            CheckNoReparsePoints(path);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(AdministratorsSid);
            foreach (var sid in new[] { SystemSid, AdministratorsSid })
            {
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            }
            new DirectoryInfo(path).SetAccessControl(security);
        }
    }

    internal static void WriteSecret(string path, string content)
    {
        CheckNoReparsePoints(InstallConfiguration.RuntimeDirectory);
        RequireAdministratorWriteOnly(new DirectoryInfo(InstallConfiguration.RuntimeDirectory));
        var temporary = Path.Combine(InstallConfiguration.RuntimeDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(AdministratorsSid);
            security.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(AdministratorsSid, FileSystemRights.FullControl, AccessControlType.Allow));
            using (var stream = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.Write,
                FileShare.None, 4096, FileOptions.WriteThrough, security))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(content);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static void DeleteRuntimeFiles()
    {
        // These two files are owned by this service; never recursively delete user data.
        File.Delete(InstallConfiguration.RuntimeConfigPath);
        File.Delete(InstallConfiguration.ProcessRecordPath);
    }

    private static bool IsTrusted(string sid) => sid == SystemSid.Value || sid == AdministratorsSid.Value || sid == TrustedInstallerSid;
}
