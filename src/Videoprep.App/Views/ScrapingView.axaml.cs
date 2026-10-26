using Avalonia.Controls;
using Videoprep.App.ViewModels;

namespace Videoprep.App.Views;

public partial class ScrapingView : UserControl
{
    public ScrapingView()
    {
        InitializeComponent();
        ExploreButton.Click += (_, _) => (DataContext as ScrapingSampleViewModel)?.ShowExploreView();
        SubsButton.Click += (_, _) => (DataContext as ScrapingSampleViewModel)?.ShowSubscriptionsView();
    }
}
