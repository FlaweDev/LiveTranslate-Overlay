using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using LiveTranslateOverlay.Core.Configuration;
using LiveTranslateOverlay.Core.Text;
using LiveTranslateOverlay.UI.Animation;

namespace LiveTranslateOverlay.UI.Controls;

public sealed class AnimatedOriginalLine : Canvas
{
    private sealed class WordToken
    {
        public required string Text { get; init; }
        public required OutlinedTextControl Control { get; init; }
        public required double LocalX { get; init; }
        public required double Width { get; init; }
    }

    private readonly Color _color;
    private readonly double _fontSize;
    private readonly double _spaceWidth;
    private readonly double _containerWidth;
    private readonly UiSettings _uiSettings;
    private readonly TranslateTransform _lineTransform = new(0, 0);
    private readonly List<WordToken> _activeWords = new();

    public bool HasTruncatedSentences { get; private set; }

    public AnimatedOriginalLine(Color color, double fontSize, double containerWidth, UiSettings uiSettings)
    {
        IsHitTestVisible = false;
        _color = color;
        _fontSize = fontSize;
        _containerWidth = containerWidth;
        _uiSettings = uiSettings;
        _spaceWidth = OutlinedTextControl.MeasureSpaceWidth(fontSize);
        RenderTransform = _lineTransform;
    }

    public void SetInitialText(string text)
    {
        Children.Clear();
        _activeWords.Clear();
        HasTruncatedSentences = false;

        var rawWords = TextSentenceTokenizer.SplitToWords(text);
        if (rawWords.Count == 0) return;

        var words = TextSentenceTokenizer.TrimOverflowingSentences(
            rawWords,
            _uiSettings.Overlay.MaxCharactersPerSubtitle,
            out bool truncated);
        HasTruncatedSentences = truncated;

        double cursorX = 0;
        foreach (string word in words)
        {
            var ctrl = new OutlinedTextControl(word, _color, _fontSize);
            SetLeft(ctrl, cursorX);
            SetTop(ctrl, 0);
            Children.Add(ctrl);

            _activeWords.Add(new WordToken
            {
                Text = word,
                Control = ctrl,
                LocalX = cursorX,
                Width = ctrl.TextWidth
            });

            cursorX += ctrl.TextWidth + _spaceWidth;
        }

        _lineTransform.X = CalculateTargetCenterOffset();
    }

    public void AppendText(string addedText)
    {
        var newWords = TextSentenceTokenizer.SplitToWords(addedText);
        if (newWords.Count == 0) return;

        if (_activeWords.Count == 0)
        {
            SetInitialText(addedText);
            return;
        }

        double cursorX = _activeWords[^1].LocalX + _activeWords[^1].Width + _spaceWidth;

        foreach (string word in newWords)
        {
            var ctrl = new OutlinedTextControl(word, _color, _fontSize) { Opacity = 0 };
            SetLeft(ctrl, cursorX);
            SetTop(ctrl, 0);
            Children.Add(ctrl);

            _activeWords.Add(new WordToken
            {
                Text = word,
                Control = ctrl,
                LocalX = cursorX,
                Width = ctrl.TextWidth
            });

            SubtitleAnimator.AnimateOpacity(ctrl, 1.0, _uiSettings.Animation.WordFadeInDurationMs);
            cursorX += ctrl.TextWidth + _spaceWidth;
        }

        TruncateOverflowingLeftSentences();

        double targetX = CalculateTargetCenterOffset();
        double distance = Math.Abs(targetX - _lineTransform.X);
        var animCfg = _uiSettings.Animation;

        int durationMs = animCfg.MinHorizontalShiftDurationMs;
        if (distance > 0.5)
        {
            durationMs = (int)((distance / animCfg.HorizontalShiftPixelsPerSecond) * 1000.0);
            durationMs = Math.Clamp(durationMs, animCfg.MinHorizontalShiftDurationMs, animCfg.MaxHorizontalShiftDurationMs);
        }

        SubtitleAnimator.AnimateX(_lineTransform, targetX, durationMs);
    }

    private void TruncateOverflowingLeftSentences()
    {
        int maxChars = Math.Max(15, _uiSettings.Overlay.MaxCharactersPerSubtitle);
        if (CalculateActiveCharacterCount() <= maxChars) return;

        var sentences = TextSentenceTokenizer.GroupIntoSentences(_activeWords, w => w.Text);

        while (CalculateActiveCharacterCount() > maxChars && sentences.Count > 1)
        {
            var oldestSentence = sentences[0];
            sentences.RemoveAt(0);
            HasTruncatedSentences = true;

            foreach (var token in oldestSentence)
            {
                _activeWords.Remove(token);
                var ctrl = token.Control;
                SubtitleAnimator.AnimateOpacity(
                    ctrl,
                    0.0,
                    _uiSettings.Animation.SentenceFadeOutDurationMs,
                    easeIn: true,
                    onCompleted: () => Children.Remove(ctrl));
            }
        }
    }

    private double CalculateTargetCenterOffset()
    {
        if (_activeWords.Count == 0) return _containerWidth / 2.0;

        double minX = _activeWords[0].LocalX;
        double maxX = _activeWords[^1].LocalX + _activeWords[^1].Width;
        double centerLocal = (minX + maxX) / 2.0;

        return (_containerWidth / 2.0) - centerLocal;
    }

    private int CalculateActiveCharacterCount()
    {
        if (_activeWords.Count == 0) return 0;
        int total = 0;
        foreach (var word in _activeWords)
        {
            total += word.Text.Length;
        }
        return total + (_activeWords.Count - 1);
    }
}