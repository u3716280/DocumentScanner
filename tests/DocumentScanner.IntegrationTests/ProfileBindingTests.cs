using DocumentScanner.Core;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Xunit;
namespace DocumentScanner.IntegrationTests;
public sealed class ProfileBindingTests
{
    [Fact]public Task ProfileLoadPreservesActualWpfComboBoxSelections()=>ExportTests.OnSta(async()=>{
        var model=new ScanSettingsSelection();
        ComboBox Bound(string items,string selected)
        {
            var box=new ComboBox();box.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(items){Source=model});
            box.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,new Binding(selected){Source=model,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
            return box;
        }
        var source=Bound(nameof(model.Sources),nameof(model.SelectedSource));var dpi=Bound(nameof(model.Dpis),nameof(model.SelectedDpi));
        var color=Bound(nameof(model.Colors),nameof(model.SelectedColor));var paper=Bound(nameof(model.Papers),nameof(model.SelectedPaper));
        var caps=new ScannerCapabilities([
            new(ScanSource.Flatbed,[150,300],[ScanColor.Color],["A4"]),
            new(ScanSource.Duplex,[200,600],[ScanColor.Grayscale,ScanColor.BlackAndWhite],["Letter"])
        ],true);
        var saved=new ScanSettings{Source=ScanSource.Duplex,Dpi=600,Color=ScanColor.BlackAndWhite,Paper="Letter"};
        for(int repeat=0;repeat<3;repeat++)
        {
            model.ApplyCapabilities(caps,new());await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            model.ApplyCapabilities(caps,saved);await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Assert.Equal(saved,model.Value);Assert.Equal(saved.Source,source.SelectedItem);Assert.Equal(saved.Dpi,dpi.SelectedItem);
            Assert.Equal(saved.Color,color.SelectedItem);Assert.Equal(saved.Paper,paper.SelectedItem);
        }
    });
}
