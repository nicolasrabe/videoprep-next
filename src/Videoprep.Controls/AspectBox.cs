using Avalonia;
using Avalonia.Controls;

namespace Videoprep.Controls;

/// <summary>A box that is always as tall as its width says (16:9 by default): the picture of a video keeps its shape when the window resizes.</summary>
public class AspectBox : Decorator
{
    public static readonly StyledProperty<double> RatioProperty = AvaloniaProperty.Register<AspectBox, double>(nameof(Ratio), 16.0 / 9.0);

    static AspectBox() => AffectsMeasure<AspectBox>(RatioProperty);

    /// <summary>Width divided by height.</summary>
    public double Ratio { get => GetValue(RatioProperty); set => SetValue(RatioProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width;
        var size = new Size(w, w / Math.Max(Ratio, 0.1));
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
