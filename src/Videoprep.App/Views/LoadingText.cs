using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Videoprep.App.Services;

namespace Videoprep.App.Views;

/// <summary>
/// RONDA 4: a turning spinner and "Cargando &lt;qué&gt;..." (es/en), shown in the same place as the content it stands for.
/// <c>What</c> is the already translated thing ("la ficha"), the sentence follows the language by itself.
/// </summary>
public class LoadingText : UserControl
{
    public static readonly StyledProperty<string> WhatProperty = AvaloniaProperty.Register<LoadingText, string>(nameof(What), "");

    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };

    static LoadingText() => WhatProperty.Changed.AddClassHandler<LoadingText>((s, _) => s.UpdateText());

    public LoadingText()
    {
        var spinner = new Arc
        {
            Width = 14, Height = 14, StartAngle = 0, SweepAngle = 270, StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform(),
        };
        spinner.Classes.Add("spin");
        spinner.Bind(Shape.StrokeProperty, spinner.GetResourceObservable("Mu"));
        _text.Classes.Add("muted");
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { spinner, _text } };
        UpdateText();
    }

    /// <summary>What is loading, already translated ("la ficha").</summary>
    public string What { get => GetValue(WhatProperty); set => SetValue(WhatProperty, value); }

    /// <summary>The whole sentence ("Cargando la ficha...").</summary>
    public string Sentence => _text.Text ?? "";

    private void UpdateText() => _text.Text = Loc.Instance.Format("loading_fmt", What);
}
