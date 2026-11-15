using System.IO;
using System.Windows;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using DocumentScanner.Scanning;
using DocumentScanner.Export;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace DocumentScanner.App;
public partial class App : Application
{
    private ServiceProvider? provider;private Mutex? singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The accent is fixed in Themes/Office.xaml; do not let WPF UI replace it with the Windows accent.
        Wpf.Ui.UiApplication.Current.ApplySystemAccentColor=false;
        singleInstance=new Mutex(true,@"Local\DocumentScanner-1.0",out bool first);
        if(!first){MessageBox.Show("Document Scanner is already running.");Shutdown();return;}
        var services=new ServiceCollection();services.AddSingleton<AppPaths>();services.AddSingleton<SettingsStore>();services.AddSingleton<SessionStore>();
        services.AddSingleton<IScannerService,Naps2ScannerService>();services.AddSingleton<IDocumentExporter,DocumentExporter>();
        services.AddLogging(builder=>builder.AddProvider(new FileLoggerProvider(new AppPaths())));
        services.AddSingleton<MainViewModel>();services.AddSingleton<MainWindow>();
        provider=services.BuildServiceProvider();
        DispatcherUnhandledException+=(_,args)=>{
            provider.GetRequiredService<ILogger<App>>().LogError(args.Exception,"Unhandled UI error");
            MessageBox.Show("An unexpected error occurred. Existing session files have been preserved. Restart the application if the scanner remains busy.",Text.Get("Error"));args.Handled=true;
        };
        var window=provider.GetRequiredService<MainWindow>();MainWindow=window;window.Show();
    }
    protected override void OnExit(ExitEventArgs e){provider?.Dispose();singleInstance?.Dispose();base.OnExit(e);}
}
