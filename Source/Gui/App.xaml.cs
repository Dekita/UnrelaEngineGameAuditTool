using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DekUnrealGameAudit.Gui;

public partial class App : Application {
    public App() {
        // WPF's default behavior for an unhandled exception on the dispatcher thread (or a background
        // thread, or an unobserved Task exception) is to silently terminate the whole process - no dialog,
        // no log, the window just vanishes. Catch all three so a bug shows an error instead of a mystery
        // crash, and writes what actually happened to a log file next to the exe.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
        LogCrash(e.Exception);
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nFull details were written to crash-log.txt next to the exe.",
            "DekUnrealGameAudit", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) {
        if (e.ExceptionObject is Exception ex)
            LogCrash(ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
        LogCrash(e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(Exception ex) {
        try {
            var path = Path.Combine(AppContext.BaseDirectory, "crash-log.txt");
            File.AppendAllText(path, $"{DateTime.Now:o}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        } catch {
            // Best-effort - a failure to log a crash shouldn't itself throw.
        }
    }
}
