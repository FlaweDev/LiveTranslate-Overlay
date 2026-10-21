using System;
using Avalonia.Controls;
using Avalonia.Media;
using LiveTranslateOverlay.Core.Configuration;
using LiveTranslateOverlay.UI.Animation;

namespace LiveTranslateOverlay.UI.Controls;

public sealed class SubtitleBlock : Canvas
{
    private const double PairVerticalGap = 5.0;
    private const double AppearVerticalOffset = 40.0;

    private readonly double _containerWidth;
    private readonly TranslateTransform _blockTransform = new(0, 0);

    public AnimatedOriginalLine OriginalLine { get; }
    public OutlinedTextControl TranslationControl { get; }
    public int SlotIndex { get; set; }
    public double BlockHeight { get; }

    public SubtitleBlock(
        Color origColor,
        Color transColor,
        double origFontSize,
        double containerWidth,
        UiSettings uiSettings)
    {
        IsHitTestVisible = false;
        _containerWidth = containerWidth;
        Width = containerWidth;

        double transFontSize = origFontSize * 0.85;
        double origHeight = OutlinedTextControl.MeasureLineHeight(origFontSize);
        double transHeight = OutlinedTextControl.MeasureLineHeight(transFontSize);

        BlockHeight = origHeight + PairVerticalGap + transHeight;
        Height = BlockHeight;

        OriginalLine = new AnimatedOriginalLine(origColor, fontSize: origFontSize, containerWidth, uiSettings);
        SetLeft(OriginalLine, 0);
        SetTop(OriginalLine, 0);
        Children.Add(OriginalLine);

        TranslationControl = new OutlinedTextControl(
            string.Empty,
            transColor,
            transFontSize,
            wrapText: true,
            maxWidth: containerWidth * 0.9);
        SetTop(TranslationControl, origHeight + PairVerticalGap);
        CenterTranslation();
        Children.Add(TranslationControl);

        RenderTransform = _blockTransform;
    }

    public void SetTranslationText(string text)
    {
        TranslationControl.SetText(text);
        CenterTranslation();
    }

    public void AnimateAppearance(double targetY, int durationMs)
    {
        Opacity = 0.0;
        _blockTransform.Y = targetY + AppearVerticalOffset;

        SubtitleAnimator.AnimateOpacity(this, 1.0, durationMs);
        SubtitleAnimator.AnimateY(_blockTransform, targetY, durationMs);
    }

    public void AnimateToY(double targetY, int durationMs) =>
        SubtitleAnimator.AnimateY(_blockTransform, targetY, durationMs);

    public void AnimateDisappearance(int durationMs, Action onCompleted) =>
        SubtitleAnimator.AnimateOpacity(this, 0.0, durationMs, easeIn: true, onCompleted: onCompleted);

    private void CenterTranslation()
    {
        SetLeft(TranslationControl, (_containerWidth - TranslationControl.TextWidth) / 2.0);
    }
}