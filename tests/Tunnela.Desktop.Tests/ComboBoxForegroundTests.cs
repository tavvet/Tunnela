using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

namespace Tunnela.Desktop.Tests;

/// <summary>
/// Uses framework controls, source XAML and localization resources in memory. No product
/// Window, Application.Run, open Popup, tray, service, or network operation is involved.
/// This checks WPF value precedence and binding, not the appearance of a running desktop app.
/// </summary>
public sealed class ComboBoxForegroundTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ActualProfileAndProtocolChoicesOverrideGlobalWhiteTextInSelectionAndUnopenedPopup()
    {
        RunOnSta(() =>
        {
            // This is the framework Application, never Tunnela.Desktop.App. It provides
            // application-level resource lookup, which is essential to reproduce this bug.
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                var resourceSource = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Source", "App.xaml"));
                var windowSource = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Source", "MainWindow.xaml"));
                var comboSources = windowSource.Descendants(Presentation + "ComboBox").ToArray();
                Assert.Equal(4, comboSources.Length);

                // The negative control removes only the local Foreground binding from the
                // actual templates. The page's implicit white TextBlock style then wins,
                // reproducing the previous failure despite ComboBox.Foreground being dark.
                CheckChoices(application, resourceSource, comboSources, removeForegroundBinding: true);
                CheckChoices(application, resourceSource, comboSources, removeForegroundBinding: false);
                Assert.Null(application.MainWindow);
            }
            finally
            {
                application.Shutdown();
            }
        });
    }

    private static void CheckChoices(Application application, XDocument source, XElement[] comboSources, bool removeForegroundBinding)
    {
        application.Resources = LoadResources(source, removeForegroundBinding);
        var pageText = ColorOf((Brush)application.Resources["TextBrush"]);
        foreach (var comboSource in comboSources)
        {
            var isolatedSource = new XElement(comboSource);
            isolatedSource.SetAttributeValue(XNamespace.Xmlns + "loc", "clr-namespace:Tunnela.Desktop.Localization;assembly=Tunnela.Desktop");
            var combo = (ComboBox)XamlReader.Parse(isolatedSource.ToString());
            var model = new ChoiceModel();
            combo.DataContext = model;
            var isLanguage = BindingOperations.GetBinding(combo, Selector.SelectedValueProperty)?.Path.Path == "Language";
            var isProtocol = !isLanguage && comboSource.Attribute("SelectedValuePath")?.Value == "Tag";
            var expectedText = isLanguage ? "English" : isProtocol ? "HTTP/2" : model.Selected.DisplayName;
            Layout(combo, new Size(320, 50));
            Assert.Null(PresentationSource.FromVisual(combo));
            Assert.False(combo.IsDropDownOpen);
            Assert.Equal(2, combo.Items.Count);

            AssertTextColors(VisualDescendants<TextBlock>(combo).Where(text => text.Text == expectedText).ToArray(),
                pageText, removeForegroundBinding);

            // Materialize the real native ComboBox popup's ItemsPresenter off-screen.
            // IsOpen is never set: no popup HWND or desktop interaction is created.
            var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
            Assert.False(popup.IsOpen);
            var popupChild = Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
            Layout(popupChild, new Size(320, 160));
            Assert.Null(PresentationSource.FromVisual(popupChild));
            for (var index = 0; index < combo.Items.Count; index++)
            {
                var container = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(index));
                Layout(container, new Size(300, 40));
                var expectedItemText = isLanguage ? index == 0 ? "English" : "Русский" : isProtocol ? index == 0 ? "HTTP/2" : "HTTP/3" : model.Profiles[index].DisplayName;
                AssertTextColors(VisualDescendants<TextBlock>(container).Where(text => text.Text == expectedItemText).ToArray(),
                    pageText, removeForegroundBinding);
            }

            // The binding must track inherited control color after a state change, rather
            // than replacing the white bug with a hard-coded black foreground.
            combo.IsEnabled = false;
            Layout(combo, new Size(320, 50));
            AssertTextColors(VisualDescendants<TextBlock>(combo).Where(text => text.Text == expectedText).ToArray(),
                pageText, removeForegroundBinding);
            Assert.False(combo.IsDropDownOpen);
            Assert.False(popup.IsOpen);
        }
    }

    private static void AssertTextColors(TextBlock[] textBlocks, Color pageText, bool expectBrokenBaseline)
    {
        Assert.NotEmpty(textBlocks);
        foreach (var text in textBlocks)
        {
            var presenter = FindVisualAncestor<ContentPresenter>(text);
            Assert.NotNull(presenter);
            var inherited = ColorOf(TextElement.GetForeground(presenter));
            var actual = ColorOf(text.Foreground);
            Assert.NotEqual(pageText, inherited);
            if (expectBrokenBaseline)
            {
                Assert.Equal(pageText, actual);
                Assert.NotEqual(inherited, actual);
            }
            else
            {
                var binding = BindingOperations.GetBindingExpression(text, TextBlock.ForegroundProperty);
                Assert.NotNull(binding);
                Assert.Equal(BindingStatus.Active, binding.Status);
                Assert.False(binding.HasError);
                Assert.Equal(inherited, actual);
                Assert.NotEqual(pageText, actual);
            }
        }
    }

    private static ResourceDictionary LoadResources(XDocument source, bool removeForegroundBinding)
    {
        var resourceElements = source.Root!.Element(Presentation + "Application.Resources")!.Elements().Select(element => new XElement(element));
        var dictionary = new XElement(Presentation + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", Xaml), resourceElements);
        if (removeForegroundBinding)
        {
            foreach (var template in dictionary.Elements(Presentation + "DataTemplate")
                         .Where(element => element.Attribute(Xaml + "Key")?.Value is "ProfileChoiceTemplate" or "ProtocolChoiceTemplate"))
                foreach (var text in template.Descendants(Presentation + "TextBlock")) text.Attribute("Foreground")?.Remove();
        }
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private static void Layout(FrameworkElement element, Size size)
    {
        element.ApplyTemplate();
        element.Measure(size);
        element.Arrange(new Rect(new Point(), size));
        element.UpdateLayout();
        // Process queued binding work on the isolated STA without Application.Run.
        element.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
        element.UpdateLayout();
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T match) return match;
        return null;
    }

    private static Color ColorOf(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "In-memory STA test timed out.");
        failure?.Throw();
    }

    public sealed class ChoiceModel
    {
        public ChoiceProfile[] Profiles { get; } = [new("Сервер один", "http2"), new("Сервер два", "http3")];
        public ChoiceProfile Selected { get; set; }
        public bool CanInteract => true;
        public string Language { get; set; } = "en";
        public ChoiceModel() => Selected = Profiles[0];
    }

    public sealed record ChoiceProfile(string DisplayName, string Protocol);
}
