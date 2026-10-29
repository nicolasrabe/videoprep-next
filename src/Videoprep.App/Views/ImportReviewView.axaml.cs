using Avalonia.Controls;
using Videoprep.App.ViewModels;

namespace Videoprep.App.Views;

public partial class ImportReviewView : UserControl
{
    public ImportReviewView()
    {
        InitializeComponent();
        // Why the selected row starts unchecked (the same text is in the state's tooltip).
        Grid.SelectionChanged += (_, _) => Explanation.Text = (Grid.SelectedItem as ImportRow)?.Explanation ?? "";
    }
}
