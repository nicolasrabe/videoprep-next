using Avalonia.Controls;

namespace Videoprep.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        // Follow the end of the log as lines arrive.
        LogBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        };
    }
}

public partial class OptionsView : UserControl
{
    public OptionsView() => InitializeComponent();
}

public partial class SubscriptionEditView : UserControl
{
    public SubscriptionEditView() => InitializeComponent();
}
