using System.Runtime.InteropServices;
using System.Windows;
using DocumentScanner.Core;
namespace DocumentScanner.App;

/// <summary>
/// Entry point. A second copy (every Scan click in ERPNext starts one) hands its link to the running copy and exits
/// before WPF parses App.xaml and the WPF UI themes, which is the slowest part of a cold start.
/// </summary>
public static class Program
{
    private const int AsfwAny = -1;
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    public static int Main(string[] args)
    {
        StartupTiming.Mark(StartupTiming.Stage.Main);
        var instance = new SingleInstance();
        if (!instance.IsFirst)
        {
            // This process was started by the user's click, so it may give the running copy the right to come to the front.
            AllowSetForegroundWindow(AsfwAny);
            bool forwarded = instance.Forward(LaunchRequest.ForwardMessage(args));
            instance.Dispose();
            if (!forwarded) MessageBox.Show("Document Scanner is already running but did not respond. Close it from the notification area and try again.", "Document Scanner");
            return forwarded ? 0 : 1;
        }
        // Drawn by Windows straight away, while WPF is still loading; App closes it when the main window has rendered.
        var splash = new SplashScreen(typeof(Program).Assembly, "Resources/splash.png");
        splash.Show(false);
        var app = new App { Instance = instance, Splash = splash };
        app.InitializeComponent();
        return app.Run();
    }
}

/// <summary>Milliseconds from process start to each start-up stage, written to the log once the window has rendered.</summary>
public static class StartupTiming
{
    public enum Stage { Main, AppStartup, WindowCreated, FirstRender }
    private static readonly double?[] marks = new double?[4];

    public static void Mark(Stage stage)
    {
        try { marks[(int)stage] ??= (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds; }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }

    public static string Summary() =>
        string.Join(' ', Enum.GetValues<Stage>().Select(s => $"{s}={(marks[(int)s] is double ms ? Math.Round(ms).ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}"));
}
