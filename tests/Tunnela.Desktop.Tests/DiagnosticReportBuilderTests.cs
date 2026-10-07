using System.Globalization;
using Tunnela.Contracts;
using Tunnela.Desktop.Localization;

namespace Tunnela.Desktop.Tests;

// Pure formatting tests with synthetic profiles: no window, tray, profile store, or service is used.
public sealed class DiagnosticReportBuilderTests : IDisposable
{
    private readonly string _previousLanguage = Text.Language;
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _previousDefaultCulture = CultureInfo.DefaultThreadCurrentUICulture;

    [Theory]
    [InlineData("en", "[redacted]")]
    [InlineData("ru", "[скрыто]")]
    public void RedactionCoversSavedAndDraftValuesIgnoringCase(string language, string replacement)
    {
        Text.SetLanguage(language);
        var saved = new ServerProfile
        {
            Name = "Saved profile",
            Hostname = "gateway.example.invalid",
            Username = "test-account",
            Password = "initial-password",
            CertificatePem = "synthetic-certificate-pem",
            Addresses = ["192.0.2.10:443", "[2001:db8::10]:443"]
        };
        var draft = saved with { Name = "Edited profile", Password = "draft-password", Addresses = ["198.51.100.20:443"] };
        var builder = new DiagnosticReportBuilder([saved, draft]);

        string message = "SAVED PROFILE|GATEWAY.EXAMPLE.INVALID|TEST-ACCOUNT|INITIAL-PASSWORD|SYNTHETIC-CERTIFICATE-PEM|"
            + "192.0.2.10:443|[2001:DB8::10]:443|EDITED PROFILE|DRAFT-PASSWORD|198.51.100.20:443";

        Assert.Equal(string.Join("|", Enumerable.Repeat(replacement, 10)), builder.Redact(message));
        Assert.Equal("initial-password", saved.Password);
        Assert.Equal("draft-password", draft.Password);
    }

    [Fact]
    public void EmptyPrivateValuesLeaveOrdinaryDiagnosticsUnchanged()
    {
        var builder = new DiagnosticReportBuilder([new ServerProfile { Name = "", Addresses = [""] }]);
        Assert.Equal("VPN is disconnected.", builder.Redact("VPN is disconnected."));
        Assert.Equal("", builder.Redact(""));
    }

    [Theory]
    [InlineData("en", "Tunnela — diagnostic report")]
    [InlineData("ru", "Tunnela — диагностический отчёт")]
    public void EmptyReportDoesNotConvertMalformedDraft(string language, string expectedTitle)
    {
        Text.SetLanguage(language);
        ServerProfile[] saved = [new() { Name = "Saved profile", Password = "synthetic-password" }];
        ProfileEditor[] drafts = [new(new ServerProfile { Name = null! }, dirty: true)];
        int draftConversions = 0;
        // Match MainWindow's deferred saved-plus-draft sequence. This draft cannot be
        // converted, but an empty journal has no messages that need profile redaction.
        var profiles = saved.Concat(drafts.Select(draft =>
        {
            draftConversions++;
            return draft.ToProfile();
        }));
        var builder = new DiagnosticReportBuilder(profiles);
        Assert.Equal(0, draftConversions);
        var status = new DiagnosticReportStatus("Available", "Disconnected", "Stopped", "1.1.7");
        var createdAt = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        string report = builder.Build(status, Array.Empty<DiagnosticEntry>(), createdAt);

        Assert.Equal(0, draftConversions);
        Assert.StartsWith(expectedTitle + Environment.NewLine, report);
        Assert.DoesNotContain("synthetic-password", report);
    }

    [Fact]
    public void NonemptyReportPropagatesDraftConversionFailure()
    {
        Text.SetLanguage("en");
        ServerProfile[] saved = [new() { Password = "synthetic-password" }];
        ProfileEditor[] drafts = [new(new ServerProfile { Name = null! }, dirty: true)];
        var builder = new DiagnosticReportBuilder(saved.Concat(drafts.Select(draft => draft.ToProfile())));
        var status = new DiagnosticReportStatus("Available", "Disconnected", "Stopped", "1.1.7");
        var createdAt = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        DiagnosticEntry[] logs = [new(createdAt, "warning", "Input synthetic-password was rejected.")];

        // A real message must not be exported if collecting the values to redact fails.
        Assert.Throws<NullReferenceException>(() => builder.Build(status, logs, createdAt));
    }

    [Fact]
    public void EnglishReportPreservesFormatAndRedactsLogMessages()
    {
        Text.SetLanguage("en");
        var createdAt = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var builder = new DiagnosticReportBuilder([new ServerProfile { Password = "synthetic-password" }]);
        var status = new DiagnosticReportStatus("Available", "VPN disconnected", "Stopped", "1.1.7");
        DiagnosticEntry[] logs =
        [
            new(createdAt.AddMinutes(-1), "info", "VPN is disconnected."),
            new(createdAt, "warning", "Input synthetic-password was rejected.")
        ];

        string report = builder.Build(status, logs, createdAt);

        string expected = string.Join(Environment.NewLine,
        [
            "Tunnela — diagnostic report",
            "Date: 2026-10-07T12:00:00.0000000+00:00",
            "Service: Available",
            "State: VPN disconnected",
            "Process: Stopped",
            "Engine: 1.1.7",
            "Profiles, addresses, passwords, and configurations are excluded from this report.",
            "",
            "2026-10-07T11:59:00.0000000+00:00 [info] VPN is disconnected.",
            "2026-10-07T12:00:00.0000000+00:00 [warning] Input [redacted] was rejected.",
            ""
        ]);
        Assert.Equal(expected, report);
        Assert.Equal("Input synthetic-password was rejected.", logs[1].Message);
    }

    [Fact]
    public void ReportUsesCurrentLanguageAfterLanguageSwitch()
    {
        Text.SetLanguage("en");
        var builder = new DiagnosticReportBuilder([new ServerProfile { Hostname = "gateway.example.invalid" }]);
        Text.SetLanguage("ru");
        var createdAt = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var status = new DiagnosticReportStatus("Доступна", "VPN отключён", "Остановлен", "1.1.7");

        string report = builder.Build(status, [new(createdAt, "info", "gateway.example.invalid")], createdAt);

        Assert.StartsWith("Tunnela — диагностический отчёт" + Environment.NewLine, report);
        Assert.Contains("Служба: Доступна" + Environment.NewLine, report);
        Assert.Contains("Состояние: VPN отключён" + Environment.NewLine, report);
        Assert.EndsWith("[info] [скрыто]" + Environment.NewLine, report);
        Assert.DoesNotContain("gateway.example.invalid", report);
    }

    public void Dispose()
    {
        Text.SetLanguage(_previousLanguage);
        CultureInfo.CurrentUICulture = _previousCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _previousDefaultCulture;
    }
}
