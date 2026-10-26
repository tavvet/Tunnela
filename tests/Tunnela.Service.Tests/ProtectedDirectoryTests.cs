using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Tunnela.Service.Tests;

/// <summary>Uses only a fresh folder under the current user's temporary directory.</summary>
public sealed class ProtectedDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Tunnela.Tests." + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PreexistingUserDirectoryIsRejectedWithoutChangingItsSecurity()
    {
        // Same situation as a standard user creating %ProgramData%\Tunnela before the service.
        var directory = Directory.CreateDirectory(Path.Combine(_root, "Service"));
        var before = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);

        Assert.Throws<UnauthorizedAccessException>(() => ProtectedFiles.EnsureProtectedDirectory(directory.FullName));

        var after = new DirectoryInfo(directory.FullName).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
        Assert.Equal(before, after);
    }

    [Fact]
    public void PreexistingDirectoryKeepsItsContents()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "Service"));
        var marker = Path.Combine(directory.FullName, "marker.txt");
        File.WriteAllText(marker, "preserved");

        Assert.ThrowsAny<Exception>(() => ProtectedFiles.EnsureProtectedDirectory(directory.FullName));
        Assert.Equal("preserved", File.ReadAllText(marker));
    }

    [Fact]
    public void ServiceDescriptorIsAdministratorOwnedAndProtected()
    {
        var security = ProtectedFiles.CreateProtectedDirectorySecurity(ProtectedFiles.AdministratorsSid);
        Assert.Equal("O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)",
            security.GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access));
    }

    [Fact]
    public void MissingDirectoryGetsOnlyTheProtectedDaclAtCreation()
    {
        // The temporary parent grants this user inheritable full control. The new folder must not
        // inherit it: the descriptor has to be applied by the create call itself, as on a fresh install.
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "Tunnela");
        FileSystemSecurity? created = null;

        ProtectedFiles.EnsureProtectedDirectory(path, CurrentUser, directory => created = ReadSecurity(directory));

        Assert.NotNull(created);
        Assert.True(created.AreAccessRulesProtected);
        ProtectedFiles.RequireNoUntrustedWriteRules(created);
        Assert.Equal(CurrentUser, created.GetOwner(typeof(SecurityIdentifier)));
        var rules = created.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        Assert.All(rules, rule => Assert.False(rule.IsInherited));
        Assert.Equal(new[] { ProtectedFiles.SystemSid.Value, ProtectedFiles.AdministratorsSid.Value }.Order(),
            rules.Select(rule => rule.IdentityReference.Value).Order());
    }

    [Fact]
    public void DirectoryCreatedEarlierIsVerifiedWithoutChangingIt()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "Tunnela");
        ProtectedFiles.EnsureProtectedDirectory(path, CurrentUser, _ => { });
        var before = Sddl(ReadSecurity(new DirectoryInfo(path)));
        var verified = 0;

        ProtectedFiles.EnsureProtectedDirectory(path, CurrentUser, _ => verified++);

        Assert.Equal(1, verified);
        Assert.Equal(before, Sddl(ReadSecurity(new DirectoryInfo(path))));
    }

    private static SecurityIdentifier CurrentUser
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User!;
        }
    }

    private static FileSystemSecurity ReadSecurity(DirectoryInfo directory) =>
        directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);

    private static string Sddl(FileSystemSecurity security) =>
        security.GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        // A folder created with the protected DACL denies this user ordinary access. As its owner,
        // the user may still grant itself access before cleanup.
        foreach (var directory in new DirectoryInfo(_root).GetDirectories())
        {
            try
            {
                var security = new DirectorySecurity();
                security.AddAccessRule(new FileSystemAccessRule(CurrentUser, FileSystemRights.FullControl, AccessControlType.Allow));
                directory.SetAccessControl(security);
            }
            catch (UnauthorizedAccessException) { }
        }
        Directory.Delete(_root, recursive: true);
    }
}
