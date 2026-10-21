using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LiveTranslateOverlay.UI.Controls;

public sealed class OutlinedTextControl : Canvas
{
    private static readonly int[] OutlineOffsets = { -1, 0, 1 };

    private readonly List<TextBlock> _allBlocks = new(9);
    private readonly TextBlock _fillBlock;
    private readonly double _maxWidth;

    public double TextWidth { get; private set; }
    public double TextHeight { get; private set; }

    public OutlinedTextControl(
        string text,
        Color fillColor,
        double fontSize,
        bool wrapText = false,
        double maxWidth = double.PositiveInfinity)
    {
        IsHitTestVisible = false;
        _maxWidth = maxWidth;

        var outlineBrush = new SolidColorBrush(Darken(fillColor, 0.5f));
        var fillBrush = new SolidColorBrush(fillColor);
        double stroke = fontSize * 0.05;
        var wrapping = wrapText ? TextWrapping.Wrap : TextWrapping.NoWrap;
        var alignment = wrapText ? TextAlignment.Center : TextAlignment.Left;

        foreach (int dx in OutlineOffsets)
        {
            foreach (int dy in OutlineOffsets)
            {
                if (dx == 0 && dy == 0) continue;

                var outlineBlock = CreateLayerBlock(text, fontSize, outlineBrush, wrapping, alignment, maxWidth);
                SetLeft(outlineBlock, dx * stroke);
                SetTop(outlineBlock, dy * stroke);
                _allBlocks.Add(outlineBlock);
                Children.Add(outlineBlock);
            }
        }

        _fillBlock = CreateLayerBlock(text, fontSize, fillBrush, wrapping, alignment, maxWidth);
        SetLeft(_fillBlock, 0);
        SetTop(_fillBlock, 0);
        _allBlocks.Add(_fillBlock);
        Children.Add(_fillBlock);

        UpdateSize();
    }

    public void SetText(string text)
    {
        foreach (var block in _allBlocks)
        {
            block.Text = text;
        }
        UpdateSize();
    }

    public static double MeasureSpaceWidth(double fontSize)
    {
        var singleA = new TextBlock { Text = "A", FontSize = fontSize, FontWeight = FontWeight.Black };
        singleA.Measure(Size.Infinity);

        var doubleA = new TextBlock { Text = "A A", FontSize = fontSize, FontWeight = FontWeight.Black };
        doubleA.Measure(Size.Infinity);

        double width = doubleA.DesiredSize.Width - (singleA.DesiredSize.Width * 2);
        return width > 0 ? width : fontSize * 0.3;
    }

    public static double MeasureLineHeight(double fontSize)
    {
        var sample = new TextBlock { Text = "Ay", FontSize = fontSize, FontWeight = FontWeight.Black };
        sample.Measure(Size.Infinity);
        return sample.DesiredSize.Height;
    }

    private void UpdateSize()
    {
        _fillBlock.Measure(new Size(_maxWidth, double.PositiveInfinity));
        TextWidth = _fillBlock.DesiredSize.Width;
        TextHeight = _fillBlock.DesiredSize.Height;
        Width = TextWidth;
        Height = TextHeight;
    }

    private static TextBlock CreateLayerBlock(
        string text,
        double fontSize,
        IBrush foreground,
        TextWrapping wrapping,
        TextAlignment alignment,
        double maxWidth) =>
        new()
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeight.Bold,
            Foreground = foreground,
            TextWrapping = wrapping,
            TextAlignment = alignment,
            MaxWidth = maxWidth,
            IsHitTestVisible = false
        };

    private static Color Darken(Color c, float factor) =>
        Color.FromArgb(c.A, (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
}