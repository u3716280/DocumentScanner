using System.IO;
using System.Globalization;
using System.Resources;
using System.Windows.Markup;
namespace DocumentScanner.App;
public static class Text
{
    private static readonly ResourceManager Resources=new("DocumentScanner.App.Resources.Strings",typeof(Text).Assembly);
    public static string Get(string key)=>Resources.GetString(key,CultureInfo.CurrentUICulture)??key;
}
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)=>Text.Get(key);
}
