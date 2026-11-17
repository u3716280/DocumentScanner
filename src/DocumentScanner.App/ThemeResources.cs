using System.Windows;
using Wpf.Ui.Markup;
namespace DocumentScanner.App;
internal static class ThemeResources
{
    /// <summary>
    /// App.xaml merges the theme once for the whole application. A window created without an
    /// Application (integration tests, design hosts) has nothing to inherit, so it loads its own copy.
    /// Call before InitializeComponent.
    /// </summary>
    public static void Ensure(FrameworkElement root)
    {
        if(root.TryFindResource("LampBrush")!=null)return;
        // Registers the pack: URI scheme, which WPF UI's absolute dictionary URIs need when no Application exists.
        _=System.IO.Packaging.PackUriHelper.UriSchemePack;
        var tokens=new ResourceDictionary{Source=new Uri("/DocumentScanner;component/Themes/Office.xaml",UriKind.Relative)};
        var merged=root.Resources.MergedDictionaries;
        merged.Add(new ThemesDictionary());
        merged.Add(new ControlsDictionary());
        merged.Add(tokens); // Last, so its accent keys win over the library defaults.
    }
}
