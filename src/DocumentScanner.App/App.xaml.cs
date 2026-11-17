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
    private ServiceProvider? provider;private SingleInstance? singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The accent is fixed in Themes/Office.xaml; do not let WPF UI replace it with the Windows accent.
        Wpf.Ui.UiApplication.Current.ApplySystemAccentColor=false;
        singleInstance=new SingleInstance();
        if(!singleInstance.IsFirst)
        {
            // Hand the scan link to the copy that is already running; never open a second workspace for the same job.
            if(!singleInstance.Forward(LaunchRequest.ForwardMessage(e.Args)))MessageBox.Show("Document Scanner is already running but did not respond. Close it and try again.");
            Shutdown();return;
        }
        var services=new ServiceCollection();services.AddSingleton<AppPaths>();services.AddSingleton<SettingsStore>();services.AddSingleton<SessionStore>();
        services.AddSingleton<IScannerService,Naps2ScannerService>();services.AddSingleton<IDocumentExporter,DocumentExporter>();
        services.AddLogging(builder=>builder.AddProvider(new FileLoggerProvider(new AppPaths())));
        services.AddSingleton<MainViewModel>();services.AddSingleton<MainWindow>();
        provider=services.BuildServiceProvider();
        DispatcherUnhandledException+=(_,args)=>{
            provider.GetRequiredService<ILogger<App>>().LogError(args.Exception,"Unhandled UI error");
            MessageBox.Show("An unexpected error occurred. Existing session files have been preserved. Restart the application if the scanner remains busy.",Text.Get("Error"));args.Handled=true;
        };
        var window=provider.GetRequiredService<MainWindow>();MainWindow=window;
        var model=provider.GetRequiredService<MainViewModel>();
        // The window starts the view model once it has loaded; the link is handled after that start-up has finished.
        var launch=LaunchRequest.From(e.Args);
        if(launch.Kind!=LaunchKind.None)window.Loaded+=async(_,_)=>await model.HandleLaunchAsync(launch);
        window.Show();
        singleInstance.Listen(message=>Dispatcher.BeginInvoke(async()=>
        {
            if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;
            window.Activate();
            // Whatever arrives on the pipe is validated exactly like a command-line argument.
            await model.HandleLaunchAsync(LaunchRequest.From([message]));
        }));
    }
    protected override void OnExit(ExitEventArgs e){provider?.Dispose();singleInstance?.Dispose();base.OnExit(e);}
}
