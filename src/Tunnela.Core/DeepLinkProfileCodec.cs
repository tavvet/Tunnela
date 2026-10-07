using Tunnela.Contracts.Localization;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Tunnela.Contracts;

namespace Tunnela.Core;

/// <summary>Decodes static tt://? links, versions 0–2. Subscription retrieval is intentionally unsupported.</summary>
public static class DeepLinkProfileCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static ServerProfile Import(string text)
    {
        try
        {
            return Decode(text);
        }
        catch (ProfileImportException) { throw; }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException or
                                           CryptographicException or AsnContentException)
        {
            throw new ProfileImportException(Messages.Get("Link.Invalid"));
        }
    }

    private static ServerProfile Decode(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1_048_576)
            throw new ProfileImportException(Messages.Get("Link.SizeInvalid"));
        text = text.Trim();
        if (!text.StartsWith("tt://?", StringComparison.OrdinalIgnoreCase))
            throw new ProfileImportException(Messages.Get("Link.SchemeInvalid"));
        var payload = text[6..];
        if (payload.Length == 0 || payload.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ProfileImportException(Messages.Get("Link.EncodingInvalid"));
        var bytes = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/') + new string('=', (4 - payload.Length % 4) % 4));
        var reader = new TlvReader(bytes);
        var fields = new Dictionary<ulong, byte[]>();
        var addresses = new List<string>();
        while (!reader.IsEmpty)
        {
            var tag = reader.ReadInteger();
            var length = reader.ReadInteger();
            if (length > int.MaxValue) throw Malformed();
            var value = reader.ReadBytes((int)length);
            if (tag == 2) addresses.Add(StrictUtf8.GetString(value));
            else if (tag <= 14) fields[tag] = value.ToArray();
            // The specification requires that unknown tags are ignored.
        }

        var version = Integer(fields, 0, 0);
        if (version > 2) throw new ProfileImportException(Messages.Get("Link.VersionUnsupported"));
        if (fields.ContainsKey(14))
            throw new ProfileImportException(Messages.Get("Link.SubscriptionUnsupported"));
        if (Boolean(fields, 7, false))
            throw new ProfileImportException(Messages.Get("Import.CertificateVerificationDisabled"));
        var hostname = String(fields, 1, required: true);
        var profile = new ServerProfile
        {
            Name = String(fields, 12, hostname),
            Hostname = hostname,
            Addresses = addresses,
            CustomSni = String(fields, 3),
            HasIpv6 = Boolean(fields, 4, true),
            Username = String(fields, 5, required: true),
            Password = String(fields, 6, required: true),
            CertificatePem = fields.TryGetValue(8, out var certificate) ? DecodeCertificates(certificate) : "",
            UpstreamProtocol = Integer(fields, 9, 1) switch
            {
                1 => "http2",
                2 => "http3",
                _ => throw new ProfileImportException(Messages.Get("Link.ProtocolInvalid"))
            },
            AntiDpi = Boolean(fields, 10, false),
            ClientRandom = String(fields, 11),
            DnsUpstreams = fields.TryGetValue(13, out var dns) ? DecodeStrings(dns) : []
        };
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0) throw new ProfileImportException(string.Join(Environment.NewLine, errors));
        return profile;
    }

    private static string String(Dictionary<ulong, byte[]> fields, ulong tag, string fallback = "", bool required = false)
    {
        if (fields.TryGetValue(tag, out var bytes)) return StrictUtf8.GetString(bytes);
        if (required) throw new ProfileImportException(Messages.Get("Link.ParametersMissing"));
        return fallback;
    }

    private static ulong Integer(Dictionary<ulong, byte[]> fields, ulong tag, ulong fallback)
    {
        if (!fields.TryGetValue(tag, out var bytes)) return fallback;
        var reader = new TlvReader(bytes);
        var value = reader.ReadInteger();
        if (!reader.IsEmpty) throw Malformed();
        return value;
    }

    private static bool Boolean(Dictionary<ulong, byte[]> fields, ulong tag, bool fallback)
    {
        if (!fields.TryGetValue(tag, out var bytes)) return fallback;
        if (bytes.Length != 1 || bytes[0] > 1) throw Malformed();
        return bytes[0] == 1;
    }

    private static List<string> DecodeStrings(byte[] bytes)
    {
        var reader = new TlvReader(bytes);
        var strings = new List<string>();
        while (!reader.IsEmpty)
        {
            var length = reader.ReadInteger();
            if (length > int.MaxValue) throw Malformed();
            strings.Add(StrictUtf8.GetString(reader.ReadBytes((int)length)));
        }
        return strings;
    }

    private static string DecodeCertificates(byte[] bytes)
    {
        var reader = new AsnReader(bytes, AsnEncodingRules.DER);
        var pem = new StringBuilder();
        while (reader.HasData)
        {
            var encoded = reader.ReadEncodedValue();
            using var certificate = X509CertificateLoader.LoadCertificate(encoded.Span);
            pem.AppendLine(PemEncoding.WriteString("CERTIFICATE", encoded.Span));
        }
        return pem.ToString();
    }

    private static ProfileImportException Malformed() => new(Messages.Get("Link.BinaryInvalid"));

    private ref struct TlvReader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;
        public readonly bool IsEmpty => _remaining.IsEmpty;

        public ulong ReadInteger()
        {
            if (_remaining.IsEmpty) throw Malformed();
            var length = 1 << (_remaining[0] >> 6);
            var encoded = ReadBytes(length);
            ulong value = (ulong)(encoded[0] & 0x3f);
            for (var index = 1; index < encoded.Length; index++) value = (value << 8) | encoded[index];
            return value;
        }

        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            if (length < 0 || length > _remaining.Length) throw Malformed();
            var result = _remaining[..length];
            _remaining = _remaining[length..];
            return result;
        }
    }
}
