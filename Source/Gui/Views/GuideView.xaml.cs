using System.Windows;
using System.Windows.Controls;

namespace DekUnrealGameAudit.Gui.Views;

/// <summary>Static walkthrough of the tool's workflow - no settings to load/save, unlike the other tabs.</summary>
public partial class GuideView : UserControl {
    public GuideView() {
        InitializeComponent();
    }

    /// <summary>Hides the bottom fade hint once there's nothing left below the fold - otherwise it would sit
    /// on top of (and dim) the actual last line once you scroll all the way down.</summary>
    private void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) {
        var scrollableHeight = e.ExtentHeight - e.ViewportHeight;
        BottomFadeOverlay.Visibility = e.VerticalOffset >= scrollableHeight - 1
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
