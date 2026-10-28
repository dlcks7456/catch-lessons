using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace CatchLessons;
internal static class WpfLocalizer
{
    private static readonly ConditionalWeakTable<DependencyObject,Dictionary<DependencyProperty,string>> Originals=new();
    private static void Translate(DependencyObject item,DependencyProperty property)
    {
        if(BindingOperations.IsDataBound(item,property)||item.GetValue(property) is not string value||value.Length==0)return;
        var values=Originals.GetOrCreateValue(item);if(!values.TryGetValue(property,out var original))values[property]=original=value;
        item.SetValue(property,UiText.T(original));
    }
    public static void Apply(DependencyObject root)
    {
        if(root is TextBlock text&&text.Name is not ("PartialText" or "AgentWarningText" or "SessionIdText" or "ProcessingDetail"))Translate(root,TextBlock.TextProperty);
        if(root is ContentControl)Translate(root,ContentControl.ContentProperty);
        if(root is HeaderedContentControl)Translate(root,HeaderedContentControl.HeaderProperty);
        if(root is FrameworkElement)Translate(root,FrameworkElement.ToolTipProperty);
        if(root is FrameworkElement)Translate(root,System.Windows.Automation.AutomationProperties.NameProperty);
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().ToArray())Apply(child);
    }
}

