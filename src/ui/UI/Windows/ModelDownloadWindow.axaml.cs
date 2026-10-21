using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LiveTranslateOverlay.Core.Models.Ipc;

namespace LiveTranslateOverlay.UI.Windows;

public partial class ModelDownloadWindow : Window
{
    private sealed class ModelProgressRow
    {
        public required ProgressBar ProgressBar { get; init; }
        public required TextBlock StatsText { get; init; }
    }

    private readonly Dictionary<string, ModelProgressRow> _rowsByKey = new();
    private readonly Action _onConfirmed;
    private readonly Action _onCancelled;
    private bool _isCompletedOrConfirmed;

    public ModelDownloadWindow()
    {
        InitializeComponent();
        _onConfirmed = () => { };
        _onCancelled = () => { };
    }

    public ModelDownloadWindow(
        IReadOnlyList<MissingModelItemDto> missingModels,
        Action onConfirmed,
        Action onCancelled)
    {
        InitializeComponent();
        _onConfirmed = onConfirmed;
        _onCancelled = onCancelled;

        PopulateModelsList(missingModels);

        Closing += (_, _) =>
        {
            if (!_isCompletedOrConfirmed)
            {
                _onCancelled();
            }
        };
    }

    public void UpdateProgress(DownloadProgressEventDto progress)
    {
        if (!_rowsByKey.TryGetValue(progress.Key, out var row)) return;

        row.ProgressBar.Value = progress.Percentage;

        string downloadedMb = FormatBytes(progress.DownloadedBytes);
        string totalMb = progress.TotalBytes > 0 ? FormatBytes(progress.TotalBytes) : "?";
        row.StatsText.Text = $"{progress.Percentage:F1}%  ({downloadedMb} / {totalMb})  •  {progress.SpeedMbps:F2} МБ/с";

        OverallStatusText.Text = $"Загрузка: {progress.Name}...";
    }

    public void MarkCompleted()
    {
        _isCompletedOrConfirmed = true;
        OverallStatusText.Text = "Все модели успешно загружены! Запуск движка...";
        Close();
    }

    public void ShowError(string message)
    {
        OverallStatusText.Text = $"❌ Ошибка загрузки: {message}";
        OverallStatusText.Foreground = new SolidColorBrush(Color.Parse("#FF5252"));
        ConfirmButton.IsEnabled = true;
        ConfirmButton.Content = "Повторить";
    }

    private void PopulateModelsList(IReadOnlyList<MissingModelItemDto> models)
    {
        ModelsListContainer.Children.Clear();
        _rowsByKey.Clear();

        long totalBytes = 0;

        foreach (var model in models)
        {
            totalBytes += model.SizeBytes;

            var titleBlock = new TextBlock
            {
                Text = $"{model.Name} ({FormatBytes(model.SizeBytes)})",
                Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
                FontWeight = FontWeight.SemiBold,
                FontSize = 14
            };

            var pathBlock = new TextBlock
            {
                Text = $"Путь: {model.TargetPath}",
                Foreground = new SolidColorBrush(Color.Parse("#8A94A6")),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };

            var urlBlock = new TextBlock
            {
                Text = $"URL: {model.Url}",
                Foreground = new SolidColorBrush(Color.Parse("#656F80")),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Height = 6,
                Foreground = new SolidColorBrush(Color.Parse("#00E5FF")),
                Background = new SolidColorBrush(Color.Parse("#25293A")),
                Margin = new Thickness(0, 4, 0, 0)
            };

            var statsText = new TextBlock
            {
                Text = "Ожидание...",
                Foreground = new SolidColorBrush(Color.Parse("#00E5FF")),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cardStack = new StackPanel
            {
                Spacing = 4,
                Children = { titleBlock, pathBlock, urlBlock, progressBar, statsText }
            };

            var cardBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#191B26")),
                BorderBrush = new SolidColorBrush(Color.Parse("#3000E5FF")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10),
                Child = cardStack
            };

            _rowsByKey[model.Key] = new ModelProgressRow
            {
                ProgressBar = progressBar,
                StatsText = statsText
            };

            ModelsListContainer.Children.Add(cardBorder);
        }

        if (totalBytes > 0)
        {
            OverallStatusText.Text = $"Общий объем загрузки: ~{FormatBytes(totalBytes)}";
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _isCompletedOrConfirmed = true;
        ConfirmButton.IsEnabled = false;
        ConfirmButton.Content = "Загрузка...";
        OverallStatusText.Text = "Начинаем загрузку моделей...";
        _onConfirmed();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _isCompletedOrConfirmed = true;
        _onCancelled();
        Close();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "Неизвестно";
        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1024.0)
        {
            return $"{mb / 1024.0:F2} ГБ";
        }
        return $"{mb:F1} МБ";
    }
}