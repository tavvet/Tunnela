using System.Text;
using Tunnela.Contracts;
using Tunnela.Desktop.Localization;

namespace Tunnela.Desktop;

internal sealed record DiagnosticReportStatus(string Service, string Connection, string Process, string Engine);

/// <summary>Formats structured service diagnostics without reading storage or writing files.</summary>
internal sealed class DiagnosticReportBuilder
{
    private readonly Lazy<string[]> _privateValues;

    public DiagnosticReportBuilder(IEnumerable<ServerProfile> profiles)
    {
        // Include saved profiles and unsaved drafts so edited credentials are covered too.
        // Preserve value order: redaction uses the same literal replacements in the UI and report.
        // Wait until the first message needs redaction: an empty journal must not convert drafts.
        _privateValues = new(() => profiles.SelectMany(profile =>
                new[] { profile.Password, profile.Username, profile.Hostname, profile.Name, profile.CertificatePem }
                    .Concat(profile.Addresses))
            .Where(value => !string.IsNullOrEmpty(value))
            .ToArray());
    }

    public string Redact(string message)
    {
        foreach (var value in _privateValues.Value)
            message = message.Replace(value, Text.Get("Redacted"), StringComparison.OrdinalIgnoreCase);
        return message;
    }

    public string Build(DiagnosticReportStatus status, IEnumerable<DiagnosticEntry> logs, DateTimeOffset createdAt)
    {
        var report = new StringBuilder();
        report.AppendLine(Text.Get("ReportTitle"));
        report.AppendLine(Text.Get("ReportDate", createdAt.ToString("O")));
        report.AppendLine(Text.Get("ReportService", status.Service));
        report.AppendLine(Text.Get("ReportState", status.Connection));
        report.AppendLine(Text.Get("ReportProcess", status.Process));
        report.AppendLine(Text.Get("ReportEngine", status.Engine));
        report.AppendLine(Text.Get("ReportPrivacy"));
        report.AppendLine();

        // Only structured service diagnostics are accepted; raw engine output is never requested.
        foreach (var entry in logs)
            report.AppendLine($"{entry.Timestamp:O} [{entry.Level}] {Redact(entry.Message)}");

        return report.ToString();
    }
}
