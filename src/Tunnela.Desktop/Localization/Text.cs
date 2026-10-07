using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;
using Tunnela.Contracts.Localization;

namespace Tunnela.Desktop.Localization;

/// <summary>Application language is an explicit preference, independent of the Windows language.</summary>
public sealed class Text : INotifyPropertyChanged
{
    private static readonly ResourceManager Strings = new("Tunnela.Desktop.Localization.Strings", typeof(Text).Assembly);
    private static readonly ResourceManager Views = new("Tunnela.Desktop.Localization.Views", typeof(Text).Assembly);
    public static Text Current { get; } = new();
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
    public static string Language => Culture.Name;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key] => Get(key);

    public static string NormalizeLanguage(string? language) => language == "ru" ? "ru" : "en";

    public static void SetLanguage(string? language)
    {
        Culture = CultureInfo.GetCultureInfo(NormalizeLanguage(language));
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs("Item[]"));
    }

    public static string Get(string key, params object[] arguments)
    {
        var format = Strings.GetString(key, Culture) ?? Views.GetString(key, Culture)
            ?? throw new ArgumentException($"Missing UI resource: {key}", nameof(key));
        return arguments.Length == 0 ? format : string.Format(Culture, format, arguments);
    }

    // Async callbacks can retain the culture from when the operation started. Resolve all
    // shared messages using the current selection, without changing the service's culture.
    internal static T InCurrentLanguage<T>(Func<T> action)
    {
        var previous = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = Culture; return action(); }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    internal static string FromService(string fallback, string? code) => InCurrentLanguage(() => Messages.Translate(fallback, code));
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Text.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
