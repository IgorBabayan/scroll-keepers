using System.Windows;
using System.Windows.Controls;

namespace ScrollKeeper.Presentation.Documents;

/// <summary>
/// Attached property that shows ESOUI BBCode in a <see cref="FlowDocumentScrollViewer"/>:
/// <c>documents:BbCode.Text="{Binding Description}"</c>.
/// </summary>
public static class BbCode
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(BbCode), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
            viewer.Document = e.NewValue is string text ? BbCodeDocumentBuilder.Build(text) : null;
    }
}
