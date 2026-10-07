using System.Collections;
using System.Globalization;
using System.Resources;

[assembly: NeutralResourcesLanguage("en")]

namespace Tunnela.Contracts.Localization;

/// <summary>Shared client messages. The service sends stable codes and neutral English text.</summary>
public static class Messages
{
    private static readonly ResourceManager Resources = new("Tunnela.Contracts.Localization.Messages", typeof(Messages).Assembly);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly Lazy<IReadOnlyDictionary<string, string>> LegacyServiceMessages = new(CreateLegacyServiceMessages);

    public static string Get(string key, params object[] args) => Format(key, CultureInfo.CurrentUICulture, args);

    /// <summary>Produces a stable fallback without changing the process or thread culture.</summary>
    public static string GetEnglish(string key, params object[] args) => Format(key, English, args);

    /// <summary>Translates a known wire code or an exact legacy service message; unknown text remains unchanged.</summary>
    public static string Translate(string fallback, string? code)
    {
        if (code is not null && Resources.GetString(code, CultureInfo.CurrentUICulture) is { } message)
            return message;
        return LegacyServiceMessages.Value.TryGetValue(fallback, out var legacyCode) ? Get(legacyCode) : fallback;
    }

    private static string Format(string key, CultureInfo culture, object[] args)
    {
        var template = Resources.GetString(key, culture) ?? key;
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }

    private static IReadOnlyDictionary<string, string> CreateLegacyServiceMessages()
    {
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("ru") })
        {
            var resources = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: true);
            if (resources is null) continue;
            foreach (DictionaryEntry entry in resources)
                if (entry.Key is string key && key.StartsWith("Service.", StringComparison.Ordinal) && entry.Value is string value)
                    messages.TryAdd(value, key);
        }
        // Earlier releases referred to the wrapper instead of the engine in these three messages.
        messages["Процесс запущен. Ожидание подтверждения VPN-соединения от Tunnela."] = "Service.ProcessStarted";
        messages["Не удалось запустить Tunnela. Проверьте установку и права службы."] = "Service.EngineStartFailed";
        messages["Ожидание штатного завершения Tunnela."] = "Service.Disconnecting";
        return messages;
    }
}
