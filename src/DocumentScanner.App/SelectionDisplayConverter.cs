using System.Globalization;
using System.Windows.Data;
using DocumentScanner.Core;
namespace DocumentScanner.App;
// Keep user-facing names in the collapsed custom ComboBox; never render record diagnostics/device IDs.
public sealed class SelectionDisplayConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value switch
    {
        ScanProfile profile=>profile.Name,
        DeviceInfo device=>device.DisplayName,
        ErpSite site=>site.Name,
        ExportFormat format=>format.ToString().ToUpperInvariant(),
        null=>"",
        _=>value.ToString()??""
    };
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
