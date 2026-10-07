using System.Collections;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Tunnela.Contracts;
using Tunnela.Desktop.Localization;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Tunnela.Desktop.Tests;

public sealed class LocalizationTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _previousDefault = CultureInfo.DefaultThreadCurrentUICulture;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("ru-RU")]
    public void MissingOrUnsupportedPreferenceUsesEnglishRegardlessOfWindowsCulture(string? language)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        Text.SetLanguage(language);
        Assert.Equal("en", Text.Language);
        Assert.Equal("Settings", Text.Get("ViewSettings"));
    }

    [Fact]
    public void BothCatalogsHaveMatchingNonemptyKeysAndFormatArguments()
    {
        foreach (var name in new[] { "Strings", "Views" })
        {
            var manager = new ResourceManager("Tunnela.Desktop.Localization." + name, typeof(Text).Assembly);
            var english = ReadCatalog(manager, CultureInfo.InvariantCulture);
            var russian = ReadCatalog(manager, CultureInfo.GetCultureInfo("ru"));
            Assert.Equal(english.Keys.Order(), russian.Keys.Order());
            foreach (var (key, value) in english)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), key);
                Assert.False(string.IsNullOrWhiteSpace(russian[key]), key);
                Assert.DoesNotMatch("[А-Яа-яЁё]", value);
                Assert.Equal(CompositeFormat.Parse(value).MinimumArgumentCount, CompositeFormat.Parse(russian[key]).MinimumArgumentCount);
            }
        }
    }

    [Fact]
    public void EveryViewReferenceResolvesInBothLanguages()
    {
        foreach (var file in new[] { "MainWindow.xaml", "ImportWindow.xaml" })
        {
            var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Source", file));
            var keys = Regex.Matches(source, @"\{loc:Loc (\w+)\}").Select(match => match.Groups[1].Value).Distinct().ToArray();
            Assert.NotEmpty(keys);
            foreach (var language in new[] { "en", "ru" })
            {
                Text.SetLanguage(language);
                foreach (var key in keys) Assert.False(string.IsNullOrWhiteSpace(Text.Get(key)), key);
            }
        }
    }

    [Fact]
    public void ExistingWpfBindingChangesLanguageWithoutRecreatingControl()
    {
        RunOnSta(() =>
        {
            Text.SetLanguage("en");
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Source", "MainWindow.xaml"));
            var labelSource = new XElement(source.Descendants(presentation + "TextBlock").First(element => element.Attribute("Text")?.Value == "{loc:Loc ViewSettings}"));
            labelSource.SetAttributeValue(XNamespace.Xmlns + "loc", "clr-namespace:Tunnela.Desktop.Localization;assembly=Tunnela.Desktop");
            var label = (TextBlock)XamlReader.Parse(labelSource.ToString());
            Assert.Equal("Settings", label.Text);
            Text.SetLanguage("ru");
            label.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
            Assert.Equal("Настройки", label.Text);
            Text.SetLanguage("en");
            label.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
            Assert.Equal("Settings", label.Text);
            Assert.Null(PresentationSource.FromVisual(label));
        });
    }

    [Fact]
    public void LanguageRefreshPreservesProfileDraftAndConfirmedConnectionSnapshot()
    {
        Text.SetLanguage("en");
        var profile = new ServerProfile { Name = "Мой сервер", Hostname = "vpn.example.org" };
        var editor = new ProfileEditor(profile) { Name = "Несохранённое имя", Rules = "example.org" };
        var draftBefore = editor.ToProfile();
        var model = new UiModel { Selected = editor, Page = 3, MinimizeToTray = false };
        model.Profiles.Add(editor);
        var snapshot = new TunnelSnapshot { State = TunnelState.Connected, ProcessId = 123, ProfileId = profile.Id, ProfileName = profile.Name, Message = "TrustTunnel подтвердил VPN-соединение." };
        model.Apply(snapshot);
        Text.SetLanguage("ru");
        model.RefreshLanguage();
        Assert.Same(snapshot, model.Snapshot);
        Assert.True(model.ServiceAvailable);
        Assert.Equal("VPN подключён", model.StatusTitle);
        Assert.True(editor.IsDirty);
        Assert.Same(profile, editor.Source);
        Assert.Equal(draftBefore.Name, editor.Name);
        Assert.Equal(draftBefore.Rules, editor.ToProfile().Rules);
        Assert.Equal(3, model.Page);
        Assert.False(model.MinimizeToTray);
        Text.SetLanguage("en");
        model.RefreshLanguage();
        Assert.Equal("VPN connected", model.StatusTitle);
        Assert.DoesNotMatch("[А-Яа-яЁё]", model.StatusMessage);
        Assert.Contains(profile.Name, model.ActiveProfileText);
    }

    [Fact]
    public void UnavailableAndUncheckedStatesNeverBecomeConnectedDuringLanguageRefresh()
    {
        Text.SetLanguage("ru");
        var model = new UiModel();
        Text.SetLanguage("en");
        model.RefreshLanguage();
        Assert.Equal("Checking service", model.StatusTitle);
        Assert.False(model.ServiceAvailable);
        model.Unavailable("ServiceNotRunning");
        Text.SetLanguage("ru");
        model.RefreshLanguage();
        Assert.False(model.ServiceAvailable);
        Assert.Equal(TunnelState.Unknown, model.Snapshot.State);
        Assert.Null(model.Snapshot.ProcessId);
        Assert.Contains("не запущена", model.StatusMessage);
    }

    [Fact]
    public void ResponseUsesCurrentSelectionEvenWhenAsyncCallbackHasAnOlderCulture()
    {
        Text.SetLanguage("ru");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        var translated = Text.FromService("TrustTunnel confirmed the VPN connection.", "Service.Connected");
        Assert.Equal("TrustTunnel подтвердил VPN-соединение.", translated);
        Assert.Equal("en", CultureInfo.CurrentUICulture.Name);
    }

    private static Dictionary<string, string> ReadCatalog(ResourceManager manager, CultureInfo culture) =>
        manager.GetResourceSet(culture, true, false)!.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Localization binding test timed out.");
        failure?.Throw();
    }

    public void Dispose()
    {
        Text.SetLanguage("en");
        CultureInfo.CurrentUICulture = _previous;
        CultureInfo.DefaultThreadCurrentUICulture = _previousDefault;
    }
}
