using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace DocumentScanner.App;
internal static class Ui
{
    public static Button Button(string text,Action action,bool primary=false){var b=new Button{Content=text,MinHeight=36,Margin=new(4)};if(primary){b.SetResourceReference(Control.BackgroundProperty,"LampBrush");b.Foreground=Brushes.White;}b.Click+=(_,_)=>{try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"Document Scanner",MessageBoxButton.OK,MessageBoxImage.Warning);}};return b;}
    public static TextBlock Heading(string text)=>new(){Text=text,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,16,0,4)};
    public static TextBlock Line(string text,bool strong=false)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,FontWeight=strong?FontWeights.SemiBold:FontWeights.Normal,Margin=new(0,2,0,2)};
    public static TextBlock Warning(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,Margin=new(0,2,0,10),Foreground=new SolidColorBrush(Color.FromRgb(180,35,24))};
    public static TextBlock Note(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,12),Foreground=new SolidColorBrush(Color.FromRgb(82,98,122))};
    public static TextBox Input(string text)=>new(){Text=text,MinHeight=32,Margin=new(0,3,0,10)};
    public static ComboBox Choice<T>(IEnumerable<T> values,T? selected){var b=new ComboBox{MinHeight=32,Margin=new(0,3,0,10)};b.ItemsSource=values;b.SelectedItem=selected;return b;}
    public static void Field(Panel panel,string label,Control control){var l=new Label{Content=label,Target=control};panel.Children.Add(l);panel.Children.Add(control);System.Windows.Automation.AutomationProperties.SetName(control,label);}
    public static CheckBox Check(string text,bool value)=>new(){Content=text,IsChecked=value,Margin=new(0,7,0,7)};
    public static StackPanel Panel()=>new(){Margin=new(20)};
    public static void Guard(Action action){try{action();}catch(Exception e){MessageBox.Show(e.Message,"Document Scanner",MessageBoxButton.OK,MessageBoxImage.Warning);}}
    public static async Task Guard(Func<Task> action){try{await action();}catch(Exception e){MessageBox.Show(e.Message,"Document Scanner",MessageBoxButton.OK,MessageBoxImage.Warning);}}
}

