using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Threading;
using LiveTranslateOverlay.Core.Configuration;
using LiveTranslateOverlay.Core.Models.Ipc;
using LiveTranslateOverlay.UI.Controls;
using LiveTranslateOverlay.UI.Layout;
using LiveTranslateOverlay.UI.Windows;

namespace LiveTranslateOverlay.Services;

public sealed class ActiveSubtitleSession
{
    public required int PhraseId { get; init; }
    public required SubtitleBlock Block { get; init; }
    public required DispatcherTimer HideTimer { get; init; }
    public string CurrentTranslationText { get; set; } = string.Empty;
}

public sealed class SubtitleOrchestrator
{
    private static readonly Regex SentenceSplitRegex = new(@"(?<=[.?!:])\s+", RegexOptions.Compiled);

    private readonly SubtitleLayoutManager _layoutManager;
    private readonly HistoryWindow _historyWindow;
    private readonly AppSettings _settings;
    private readonly Dictionary<int, ActiveSubtitleSession> _activeSubtitles = new();

    public SubtitleOrchestrator(
        SubtitleLayoutManager layoutManager,
        HistoryWindow historyWindow,
        AppSettings settings)
    {
        _layoutManager = layoutManager;
        _historyWindow = historyWindow;
        _settings = settings;
    }

    public void ProcessEvent(IpcEventDto ipcEvent)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            switch (ipcEvent)
            {
                case NewTextEventDto newText:
                    HandleNewText(newText);
                    break;

                case AddTextEventDto addText:
                    HandleAddText(addText);
                    break;

                case TranslationEventDto translation:
                    HandleTranslation(translation);
                    break;

                case StatusEventDto status:
                    ShowTemporaryNotice(status.Message, 1500);
                    break;

                case ErrorEventDto error:
                    ShowTemporaryNotice($"Ошибка: {error.Message}", 3500);
                    _historyWindow.AddSystemRecord($"❌ {error.Message}");
                    break;
            }
        });
    }

    public void ShowTemporaryNotice(string text, int durationMs = 1200)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var block = _layoutManager.CreateBlock(Colors.LimeGreen, Colors.Transparent);
            block.OriginalLine.SetInitialText(text);
            _layoutManager.AddBlock(block);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _layoutManager.RemoveBlock(block);
            };
            timer.Start();
        });
    }

    private void HandleNewText(NewTextEventDto dto)
    {
        Color origColor = ResolveSpeakerColor(dto.SpeakerIndex);
        Color transColor = DimColorForTranslation(origColor);

        _historyWindow.AddOrResetPhrase(dto.PhraseId, dto.Text, origColor, transColor);

        if (_activeSubtitles.TryGetValue(dto.PhraseId, out var existing))
        {
            existing.Block.OriginalLine.SetInitialText(dto.Text);
            RefreshTranslationDisplay(existing);
            RestartHideTimer(existing);
            return;
        }

        var block = _layoutManager.CreateBlock(origColor, transColor);
        block.OriginalLine.SetInitialText(dto.Text);

        var session = new ActiveSubtitleSession
        {
            PhraseId = dto.PhraseId,
            Block = block,
            HideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_settings.Ui.Animation.SubtitleTimeoutMs)
            }
        };

        session.HideTimer.Tick += (_, _) => RemoveSubtitle(dto.PhraseId);

        _activeSubtitles[dto.PhraseId] = session;
        _layoutManager.AddBlock(block);
        RefreshTranslationDisplay(session);
        session.HideTimer.Start();
    }

    private void HandleAddText(AddTextEventDto dto)
    {
        _historyWindow.AppendPhraseText(dto.PhraseId, dto.Text);

        if (_activeSubtitles.TryGetValue(dto.PhraseId, out var session))
        {
            session.Block.OriginalLine.AppendText(dto.Text);
            RefreshTranslationDisplay(session);
            RestartHideTimer(session);
        }
    }

    private void HandleTranslation(TranslationEventDto dto)
    {
        _historyWindow.UpdatePhraseTranslation(dto.PhraseId, dto.Translation);

        if (_activeSubtitles.TryGetValue(dto.PhraseId, out var session))
        {
            session.CurrentTranslationText = dto.Translation;
            RefreshTranslationDisplay(session);
            RestartHideTimer(session);
        }
    }

    private void RefreshTranslationDisplay(ActiveSubtitleSession session)
    {
        string rawTranslation = session.CurrentTranslationText;
        if (string.IsNullOrWhiteSpace(rawTranslation))
        {
            session.Block.SetTranslationText(session.Block.OriginalLine.HasTruncatedSentences ? "- - -" : string.Empty);
            return;
        }

        int maxChars = Math.Max(20, _settings.Ui.Overlay.MaxCharactersPerTranslation);
        string trimmed = rawTranslation.Trim();

        if (trimmed.Length <= maxChars)
        {
            session.Block.SetTranslationText(trimmed);
            return;
        }

        var sentences = new List<string>(SentenceSplitRegex.Split(trimmed));
        sentences.RemoveAll(string.IsNullOrWhiteSpace);

        while (string.Join(" ", sentences).Length > maxChars && sentences.Count > 1)
        {
            sentences.RemoveAt(0);
        }

        session.Block.SetTranslationText(string.Join(" ", sentences));
    }

    private void RemoveSubtitle(int phraseId)
    {
        if (_activeSubtitles.Remove(phraseId, out var session))
        {
            session.HideTimer.Stop();
            _layoutManager.RemoveBlock(session.Block);
        }
    }

    private Color ResolveSpeakerColor(int speakerIndex)
    {
        var palette = _settings.Ui.Overlay.SpeakerColors;
        if (palette.Count > 0)
        {
            string hex = palette[Math.Abs(speakerIndex) % palette.Count];
            if (Color.TryParse(hex, out Color parsed))
            {
                return parsed;
            }
        }
        return Colors.White;
    }

    private static Color DimColorForTranslation(Color color) =>
        Color.FromArgb(
            (byte)(color.A * 0.9),
            (byte)(color.R * 0.8),
            (byte)(color.G * 0.8),
            (byte)(color.B * 0.8));

    private static void RestartHideTimer(ActiveSubtitleSession session)
    {
        session.HideTimer.Stop();
        session.HideTimer.Start();
    }
}