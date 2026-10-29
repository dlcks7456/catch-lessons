using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using System.Xml.Linq;
using Avalonia.LogicalTree;
using System.Runtime.CompilerServices;

namespace CatchLessons.Desktop;

internal static class Ui
{
    private static readonly ConditionalWeakTable<object,Dictionary<string,string>> OriginalStrings=new();
    private static string Translate(object owner,string key,string value)
    {
        var stored=OriginalStrings.GetOrCreateValue(owner);if(!stored.TryGetValue(key,out var original))stored[key]=original=UiText.Key(value);return UiText.T(original);
    }
    public static void ApplyLanguage(Control root,Control? skip=null)
    {
        foreach(var item in root.GetLogicalDescendants().Prepend(root).OfType<Control>().ToArray())
        {
            if(skip!=null&&(item==skip||item.GetLogicalAncestors().Contains(skip)))continue;
            if(item is TextBlock text&&text.Text is string label)text.Text=Translate(text,"text",label);
            if(item is ContentControl content&&content.Content is string caption)content.Content=Translate(content,"content",caption);
            if(item is HeaderedContentControl header&&header.Header is string name)header.Header=Translate(header,"header",name);
            if(item is TextBox input&&input.PlaceholderText is string placeholder)input.PlaceholderText=Translate(input,"placeholder",placeholder);
            if(ToolTip.GetTip(item) is string tip)ToolTip.SetTip(item,Translate(item,"tip",tip));
        }
    }
    private static readonly Dictionary<string,string> Icons=XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Icons.xaml")).Root!.Elements().ToDictionary(e=>e.Attributes().First(a=>a.Name.LocalName=="Key").Value,e=>e.Value);
    public static Geometry Symbol(string icon)=>Geometry.Parse(Icons["Icon-"+icon]);
    public static Button Button(string text,Action action)
    {
        var button=new Button{Content=text,Height=36,CornerRadius=new CornerRadius(10),Margin=new Thickness(3)};button.Click+=(_,_)=>action();return button;
    }
    public static Button Icon(string icon,string label,Action action)
    {
        var button=Button("",action);button.Width=36;button.Padding=new Thickness(8);ToolTip.SetTip(button,label);
        button.Content=new PathIcon{Data=Symbol(icon)};Avalonia.Automation.AutomationProperties.SetName(button,label);return button;
    }
    public static TextBox Input(string text,bool multiline=false)=>new(){Text=text,Height=multiline?140:36,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,Margin=new Thickness(0,5,0,12)};
    public static void Label(Panel panel,string text)=>panel.Children.Add(new TextBlock{Text=text,Margin=new Thickness(0,8,0,4),FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap});
    public static ComboBox Choice<T>(IEnumerable<T> items,T? selected)=>new(){ItemsSource=items,SelectedItem=selected,Height=36,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,5,0,12)};
    public static string Name(object? value)=>value?.ToString()??"";
}
