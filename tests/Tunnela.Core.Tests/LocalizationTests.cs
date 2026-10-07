using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.Json;
using Tunnela.Contracts;
using Tunnela.Contracts.Localization;
using Tunnela.Service;

namespace Tunnela.Core.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void OldPreferencesDefaultToEnglishWithoutChangingUserValues()
    {
        using var culture = new UiCultureScope("ru-RU");
        var profiles = JsonSerializer.Deserialize<ProfileCollection>("""
            {"SchemaVersion":1,"Profiles":[{"Name":"Мой сервер","Username":"пользователь"}],"Preferences":{"MinimizeToTray":false}}
            """)!;
        Assert.Equal("en", profiles.Preferences.Language);
        Assert.False(profiles.Preferences.MinimizeToTray);
        Assert.Equal("Мой сервер", Assert.Single(profiles.Profiles).Name);
        Assert.Equal("пользователь", profiles.Profiles[0].Username);
        Assert.Equal("en", new AppPreferences().Language);
        Assert.Equal("New connection", new ServerProfile().Name);
        Assert.Equal("ru", JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(new AppPreferences { Language = "ru" }))!.Language);
    }

    [Fact]
    public void OldWireMessagesRemainReadableWithoutInventingCodes()
    {
        using var culture = new UiCultureScope("en-US");
        var response = JsonSerializer.Deserialize<ServiceResponse>("""
            {"Success":false,"Error":"Профиль не передан.","Snapshot":{"State":2,"Message":"TrustTunnel подтвердил VPN-соединение.","ProfileName":"Мой сервер"},"Logs":[{"Timestamp":"2026-10-07T00:00:00Z","Level":"info","Message":"Служба управления готова. Автоматическое подключение выключено."}]}
            """)!;
        Assert.Null(response.ErrorCode);
        Assert.Null(response.Snapshot.MessageCode);
        Assert.Null(Assert.Single(response.Logs).MessageCode);
        Assert.Equal("No profile was provided.", Messages.Translate(response.Error!, response.ErrorCode));
        Assert.Equal("TrustTunnel confirmed the VPN connection.", Messages.Translate(response.Snapshot.Message, response.Snapshot.MessageCode));
        Assert.Equal("The control service is ready. Automatic connection is disabled.", Messages.Translate(response.Logs[0].Message, response.Logs[0].MessageCode));
        Assert.Equal("Мой сервер", response.Snapshot.ProfileName);
    }

    [Theory]
    [InlineData("en-US", "Enter a connection name.", "The configuration is empty or exceeds the 1 MiB limit.")]
    [InlineData("ru-RU", "Укажите название подключения.", "Конфигурация пуста или превышает допустимый размер 1 МиБ.")]
    [InlineData("fr-FR", "Enter a connection name.", "The configuration is empty or exceeds the 1 MiB limit.")]
    public void ValidationAndImportFollowUiCultureWithEnglishFallback(string language, string validation, string import)
    {
        using var culture = new UiCultureScope(language);
        Assert.Equal(validation, Assert.Single(ProfileValidator.Validate(ProfileValidatorTests.ValidProfile() with { Name = "" })));
        Assert.Equal(import, Assert.Throws<ProfileImportException>(() => TomlProfileCodec.Import("")).Message);
        var invalidRule = Assert.Single(ProfileValidator.Validate(ProfileValidatorTests.ValidProfile() with { Rules = ["private-secret://bad"] }));
        Assert.Contains("1", invalidRule);
        Assert.DoesNotContain("private-secret", invalidRule);
        Assert.DoesNotContain("{0}", invalidRule);
    }

    [Fact]
    public void CoreLocalizationDoesNotTranslateImportedUserValues()
    {
        var original = ProfileValidatorTests.ValidProfile() with { Name = "Мой сервер", Username = "пользователь", Password = "пароль" };
        string englishExport;
        using (new UiCultureScope("en-US")) englishExport = TomlProfileCodec.Export(original);
        using (new UiCultureScope("ru-RU"))
        {
            Assert.Equal(englishExport, TomlProfileCodec.Export(original));
            var imported = TomlProfileCodec.Import(englishExport, original.Name);
            Assert.Equal(original.Name, imported.Name);
            Assert.Equal(original.Username, imported.Username);
            Assert.Equal(original.Password, imported.Password);
        }
    }

    [Fact]
    public void ServiceFallbackStaysEnglishWhileClientTranslatesItsCode()
    {
        using var culture = new UiCultureScope("ru-RU");
        var operation = new ServiceOperationError("Service.ProfileMissing");
        Assert.Equal("No profile was provided.", operation.Message);
        Assert.Equal("Профиль не передан.", Messages.Translate(operation.Message, operation.Code));
        Assert.Equal("ru-RU", CultureInfo.CurrentUICulture.Name);
        var response = new ServiceResponse
        {
            Error = operation.Message, ErrorCode = operation.Code,
            Snapshot = new TunnelSnapshot { Message = Messages.GetEnglish("Service.Connected"), MessageCode = "Service.Connected" },
            Logs = [new(DateTimeOffset.UtcNow, "info", Messages.GetEnglish("Service.Ready"), "Service.Ready")]
        };
        var decoded = JsonSerializer.Deserialize<ServiceResponse>(JsonSerializer.Serialize(response))!;
        Assert.Equal("Service.ProfileMissing", decoded.ErrorCode);
        Assert.Equal("Service.Connected", decoded.Snapshot.MessageCode);
        Assert.Equal("Service.Ready", Assert.Single(decoded.Logs).MessageCode);
    }

    [Fact]
    public void TranslationUsesExactLegacyMatchesAndPreservesUnknownText()
    {
        using var culture = new UiCultureScope("en-US");
        Assert.Equal("Waiting for TrustTunnel to exit normally.", Messages.Translate("Ожидание штатного завершения Tunnela.", null));
        const string unknown = "My server: VPN отключён";
        Assert.Equal(unknown, Messages.Translate(unknown, null));
        Assert.Equal(unknown, Messages.Translate(unknown, "Future.UnknownCode"));
        Assert.Equal("VPN is disconnected.", Messages.Translate("older fallback", "Service.Disconnected"));
    }

    [Fact]
    public void EnglishAndRussianCatalogsHaveMatchingKeysAndFormatArguments()
    {
        var manager = new ResourceManager("Tunnela.Contracts.Localization.Messages", typeof(Messages).Assembly);
        var neutral = ReadCatalog(manager, CultureInfo.InvariantCulture);
        var russian = ReadCatalog(manager, CultureInfo.GetCultureInfo("ru"));
        Assert.NotEmpty(neutral);
        Assert.Equal(neutral.Keys.Order(), russian.Keys.Order());
        foreach (var (key, english) in neutral)
        {
            Assert.False(string.IsNullOrWhiteSpace(english), key);
            Assert.False(string.IsNullOrWhiteSpace(russian[key]), key);
            var englishFormat = CompositeFormat.Parse(english);
            var russianFormat = CompositeFormat.Parse(russian[key]);
            Assert.Equal(englishFormat.MinimumArgumentCount, russianFormat.MinimumArgumentCount);
            var arguments = Enumerable.Repeat<object>("value", englishFormat.MinimumArgumentCount).ToArray();
            _ = string.Format(CultureInfo.InvariantCulture, englishFormat, arguments);
            _ = string.Format(CultureInfo.GetCultureInfo("ru"), russianFormat, arguments);
        }
    }

    private static Dictionary<string, string> ReadCatalog(ResourceManager manager, CultureInfo culture)
    {
        var resources = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(resources);
        return resources.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
    }

    private sealed class UiCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
        public UiCultureScope(string culture) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
