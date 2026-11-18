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
    private ServiceProvider? provider;private SingleInstance? singleInstance;private TrayIcon? tray;
    /// <summary>The single-instance lock Program.Main already took. Null when App is started some other way.</summary>
    public SingleInstance? Instance{get;init;}
    public SplashScreen? Splash{get;init;}
    protected override void OnStartup(StartupEventArgs e)
    {
        StartupTiming.Mark(StartupTiming.Stage.AppStartup);
        base.OnStartup(e);
        // The accent is fixed in Themes/Office.xaml; do not let WPF UI replace it with the Windows accent.
        Wpf.Ui.UiApplication.Current.ApplySystemAccentColor=false;
        singleInstance=Instance??new SingleInstance();
        if(!singleInstance.IsFirst)
        {
            // Hand the scan link to the copy that is already running; never open a second workspace for the same job.
            if(!singleInstance.Forward(LaunchRequest.ForwardMessage(e.Args)))MessageBox.Show("Document Scanner is already running but did not respond. Close it and try again.");
            Shutdown();return;
        }
        // The window only hides when closed, so the application ends when it is really closed (Exit in the tray menu).
        ShutdownMode=ShutdownMode.OnMainWindowClose;
        var paths=new AppPaths();
        var services=new ServiceCollection();services.AddSingleton(paths);services.AddSingleton<SettingsStore>();services.AddSingleton<SessionStore>();
        services.AddSingleton<IScannerService,Naps2ScannerService>();services.AddSingleton<IDocumentExporter,DocumentExporter>();
        services.AddLogging(builder=>builder.AddProvider(new FileLoggerProvider(paths)));
        services.AddSingleton<MainViewModel>();services.AddSingleton<MainWindow>();
        provider=services.BuildServiceProvider();
        var log=provider.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException+=(_,args)=>{
            log.LogError(args.Exception,"Unhandled UI error");
            MessageBox.Show("An unexpected error occurred. Existing session files have been preserved. Restart the application if the scanner remains busy.",Text.Get("Error"));args.Handled=true;
        };
        var window=provider.GetRequiredService<MainWindow>();MainWindow=window;
        StartupTiming.Mark(StartupTiming.Stage.WindowCreated);
        var model=provider.GetRequiredService<MainViewModel>();
        window.ContentRendered+=(_,_)=>
        {
            StartupTiming.Mark(StartupTiming.Stage.FirstRender);
            Splash?.Close(TimeSpan.FromMilliseconds(150));
            log.LogInformation("Startup timing ms_since_process_start {Timing}",StartupTiming.Summary());
            // Created after the first paint, so loading Windows Forms for the icon never delays the window.
            tray??=new TrayIcon(window.ShowFromTray,()=>window.RequestExit());
            window.CloseToTray=true;
        };
        window.HiddenToTray+=(_,_)=>tray?.ShowHiddenHint();
        // The window starts the view model once it has loaded; the link is handled after that start-up has finished.
        var launch=LaunchRequest.From(e.Args);
        if(launch.Kind!=LaunchKind.None)window.Loaded+=async(_,_)=>await model.HandleLaunchAsync(launch);
        window.Show();
        singleInstance.Listen(message=>Dispatcher.BeginInvoke(async()=>
        {
            window.ShowFromTray();
            // Whatever arrives on the pipe is validated exactly like a command-line argument.
            await model.HandleLaunchAsync(LaunchRequest.From([message]));
        }));
        // Signing out or shutting down Windows does not raise Closing; keep the pages for recovery without asking.
        SessionEnding+=(_,_)=>model.SaveForShutdown();
    }
    protected override void OnExit(ExitEventArgs e){tray?.Dispose();provider?.Dispose();singleInstance?.Dispose();base.OnExit(e);}
}
