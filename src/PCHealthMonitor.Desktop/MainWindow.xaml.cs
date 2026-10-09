using System.Windows;
using PCHealthMonitor.Desktop.ViewModels;

namespace PCHealthMonitor.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(DesktopHost host)
    {
        InitializeComponent();
        FitToWorkArea();
        SourceInitialized += (_, _) => Infrastructure.DarkTitleBar.Apply(this);
        var viewModel = new MainViewModel(host, Dispatcher);
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.Start();

        // Stop background work (live monitoring loop, running scan) so nothing keeps running after closing.
        Closing += (_, _) => viewModel.Dispose();
    }

    /// <summary>Keeps the initial size inside the screen's work area (small laptop screens, high DPI scaling).</summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Floor(area.Width * 0.94));
        Height = Math.Min(Height, Math.Floor(area.Height * 0.94));
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
    }
}
