using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LiveTranslateOverlay.UI.Layout;

namespace LiveTranslateOverlay.UI.Windows;

public partial class HistoryWindow : Window
{
    private sealed class HistoryEntryView
    {
        public string OriginalText { get; set; } = string.Empty;
        public required Run OriginalTextRun { get; init; }
        public required TextBlock TranslationBlock { get; init; }
    }

    private readonly Dictionary<int, HistoryEntryView> _entries = new();
    private readonly DispatcherTimer _clockTimer;
    private readonly FloatingToolbarDockController _dockController;

    private Action<bool>? _onTogglePause;
    private Action? _onRestartModel;
    private bool _isPaused;

    public HistoryWindow()
    {
        InitializeComponent();

        _dockController = new FloatingToolbarDockController(
            OverlayCanvas,
            FloatingToolbar,
            DragHandle,
            TopPreviewBorder,
            BottomPreviewBorder);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _clockTimer.Tick += (_, _) => LiveClockText.Text = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();
        LiveClockText.Text = DateTime.Now.ToString("HH:mm:ss");

        Opened += (_, _) => _dockController.ApplyDockPosition();
        Closed += (_, _) => _clockTimer.Stop();
    }

    public void BindActions(Action<bool> onTogglePause, Action onRestartModel)
    {
        _onTogglePause = onTogglePause;
        _onRestartModel = onRestartModel;
    }

    public void AddOrResetPhrase(int phraseId, string text, Color origColor, Color transColor)
    {
        bool autoScroll = IsScrolledNearBottom();

        if (_entries.TryGetValue(phraseId, out var existing))
        {
            existing.OriginalText = text;
            existing.OriginalTextRun.Text = text;
            if (autoScroll) ScrollToBottom();
            return;
        }

        string timeStr = DateTime.Now.ToString("HH:mm:ss");
        var origBrush = new SolidColorBrush(origColor);
        var transBrush = new SolidColorBrush(transColor);

        var timestampRun = new Run
        {
            Text = $"[{timeStr}] ",
            Foreground = origBrush,
            FontWeight = FontWeight.Bold
        };

        var origTextRun = new Run
        {
            Text = text,
            Foreground = origBrush,
            FontWeight = FontWeight.SemiBold
        };

        var origBlock = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Inlines = new InlineCollection { timestampRun, origTextRun }
        };

        var transBlock = new TextBlock
        {
            FontSize = 14,
            Foreground = transBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
            IsVisible = false
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#191B26")),
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, origColor.R, origColor.G, origColor.B)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10, 6),
            Child = new StackPanel { Spacing = 2, Children = { origBlock, transBlock } }
        };

        _entries[phraseId] = new HistoryEntryView
        {
            OriginalText = text,
            OriginalTextRun = origTextRun,
            TranslationBlock = transBlock
        };

        HistoryContainer.Children.Add(card);
        if (autoScroll) ScrollToBottom();
    }

    public void AppendPhraseText(int phraseId, string addedText)
    {
        if (!_entries.TryGetValue(phraseId, out var entry)) return;

        bool autoScroll = IsScrolledNearBottom();
        entry.OriginalText += addedText;
        entry.OriginalTextRun.Text = entry.OriginalText;

        if (autoScroll) ScrollToBottom();
    }

    public void UpdatePhraseTranslation(int phraseId, string translationText)
    {
        if (!_entries.TryGetValue(phraseId, out var entry)) return;

        bool autoScroll = IsScrolledNearBottom();
        entry.TranslationBlock.Text = translationText;
        entry.TranslationBlock.IsVisible = !string.IsNullOrWhiteSpace(translationText);

        if (autoScroll) ScrollToBottom();
    }

    public void AddSystemRecord(string? note = null)
    {
        bool autoScroll = IsScrolledNearBottom();
        string timeStr = DateTime.Now.ToString("HH:mm:ss");
        string label = string.IsNullOrWhiteSpace(note)
            ? $"[{timeStr}] ── Отметка времени ──"
            : $"[{timeStr}] {note}";

        HistoryContainer.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.Parse("#D4D8E2")),
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 2)
        });

        if (autoScroll) ScrollToBottom();
    }

    private void OnPauseResumeClick(object? sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        PauseResumeButton.Content = _isPaused ? "▶ Продолжить распознавание" : "⏸ Пауза распознавания";
        PauseResumeButton.BorderBrush = new SolidColorBrush(Color.Parse(_isPaused ? "#FF007F" : "#4000E5FF"));
        _onTogglePause?.Invoke(_isPaused);
    }

    private void OnRestartModelClick(object? sender, RoutedEventArgs e)
    {
        AddSystemRecord("Перезапуск модели...");
        _onRestartModel?.Invoke();
    }

    private void OnRecordTimeClick(object? sender, RoutedEventArgs e) => AddSystemRecord();

    private bool IsScrolledNearBottom()
    {
        double maxOffset = HistoryScrollViewer.Extent.Height - HistoryScrollViewer.Viewport.Height;
        return maxOffset <= 0 || HistoryScrollViewer.Offset.Y >= (maxOffset - 45);
    }

    private void ScrollToBottom()
    {
        Dispatcher.UIThread.Post(() => HistoryScrollViewer.ScrollToEnd(), DispatcherPriority.Background);
    }
}