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
    private const ulong MaximumSupportedLinkVersion = 2;

    // These are TrustTunnel wire IDs; their numeric values must remain stable.
    private static class LinkTag
    {
        public const ulong Version = 0;
        public const ulong Hostname = 1;
        public const ulong Address = 2;
        public const ulong CustomSni = 3;
        public const ulong HasIpv6 = 4;
        public const ulong Username = 5;
        public const ulong Password = 6;
        public const ulong SkipVerification = 7;
        public const ulong Certificate = 8;
        public const ulong UpstreamProtocol = 9;
        public const ulong AntiDpi = 10;
        public const ulong ClientRandom = 11;
        public const ulong Name = 12;
        public const ulong DnsUpstreams = 13;
        public const ulong Subscription = 14;
        public const ulong HighestKnown = Subscription;
    }

    private static class ProtocolCode
    {
        public const ulong Http2 = 1;
        public const ulong Http3 = 2;
    }

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
            // Repeated addresses accumulate; for every other known tag, the last value wins.
            if (tag == LinkTag.Address) addresses.Add(StrictUtf8.GetString(value));
            else if (tag <= LinkTag.HighestKnown) fields[tag] = value.ToArray();
            // The specification requires that unknown tags are ignored.
        }

        // The original link format omitted the version tag and is interpreted as version 0.
        var version = Integer(fields, LinkTag.Version, 0);
        if (version > MaximumSupportedLinkVersion) throw new ProfileImportException(Messages.Get("Link.VersionUnsupported"));
        if (fields.ContainsKey(LinkTag.Subscription))
            throw new ProfileImportException(Messages.Get("Link.SubscriptionUnsupported"));
        if (Boolean(fields, LinkTag.SkipVerification, false))
            throw new ProfileImportException(Messages.Get("Import.CertificateVerificationDisabled"));
        var hostname = String(fields, LinkTag.Hostname, required: true);
        var profile = new ServerProfile
        {
            Name = String(fields, LinkTag.Name, hostname),
            Hostname = hostname,
            Addresses = addresses,
            CustomSni = String(fields, LinkTag.CustomSni),
            HasIpv6 = Boolean(fields, LinkTag.HasIpv6, true),
            Username = String(fields, LinkTag.Username, required: true),
            Password = String(fields, LinkTag.Password, required: true),
            CertificatePem = fields.TryGetValue(LinkTag.Certificate, out var certificate) ? DecodeCertificates(certificate) : "",
            UpstreamProtocol = Integer(fields, LinkTag.UpstreamProtocol, ProtocolCode.Http2) switch
            {
                ProtocolCode.Http2 => "http2",
                ProtocolCode.Http3 => "http3",
                _ => throw new ProfileImportException(Messages.Get("Link.ProtocolInvalid"))
            },
            AntiDpi = Boolean(fields, LinkTag.AntiDpi, false),
            ClientRandom = String(fields, LinkTag.ClientRandom),
            DnsUpstreams = fields.TryGetValue(LinkTag.DnsUpstreams, out var dns) ? DecodeStrings(dns) : []
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
            // The top two bits encode the byte length: 1, 2, 4, or 8. The remaining bits
            // and following bytes form the value in big-endian order.
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
