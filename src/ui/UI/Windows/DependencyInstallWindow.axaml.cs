using System;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LiveTranslateOverlay.Core.Configuration;
using LiveTranslateOverlay.Services;

namespace LiveTranslateOverlay.UI.Windows;

public partial class DependencyInstallWindow : Window
{
    private readonly PythonDependenciesInstaller _installer;
    private readonly Action _onInstallCompleted;
    private readonly Action _onInstallCancelled;
    private readonly CancellationTokenSource _cts = new();
    private readonly StringBuilder _logBuilder = new();
    private bool _finishedSuccessfully;

    public DependencyInstallWindow()
    {
        InitializeComponent();
        _installer = null!;
        _onInstallCompleted = () => { };
        _onInstallCancelled = () => { };
    }

    public DependencyInstallWindow(
        PythonDependenciesInstaller installer,
        PyTorchSettings pytorchSettings,
        CudaCompatibilityReport report,
        Action onInstallCompleted,
        Action onInstallCancelled)
    {
        InitializeComponent();
        _installer = installer;
        _onInstallCompleted = onInstallCompleted;
        _onInstallCancelled = onInstallCancelled;

        string pkgs = string.Join(", ", pytorchSettings.Packages);
        PackageInfoText.Text =
            $"1) Пакеты PyTorch: {pkgs} ({pytorchSettings.GetResolvedIndexUrl()})\n" +
            $"2) Пакеты движка: requirements.txt (NeMo ASR, Sortformer, llama-cpp-python и др.)";

        string driverInfo = report.DetectedDriverCuda ?? "Не обнаружен";
        string toolkitInfo = report.DetectedToolkitCuda ?? "Не установлен";
        CudaInfoText.Text = $"Целевая сборка: {report.TargetCudaTag}  |  Драйвер CUDA: {driverInfo}  |  CUDA Toolkit: {toolkitInfo}";

        if (!string.IsNullOrWhiteSpace(report.WarningMessage))
        {
            WarningBanner.IsVisible = true;
            WarningText.Text = report.WarningMessage;
        }

        Closing += (_, _) =>
        {
            _cts.Cancel();
            if (!_finishedSuccessfully)
            {
                _onInstallCancelled();
            }
        };
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        InstallButton.Content = "Установка...";
        InstallProgressBar.IsIndeterminate = true;
        _logBuilder.Clear();

        bool success = await _installer.InstallAllAsync(
            progress => Dispatcher.UIThread.Post(() => UpdateUiProgress(progress)),
            _cts.Token);

        InstallProgressBar.IsIndeterminate = false;

        if (success)
        {
            _finishedSuccessfully = true;
            InstallProgressBar.Value = 100;
            CurrentStatusText.Text = "Все библиотеки успешно установлены!";
            _onInstallCompleted();
            Close();
        }
        else if (!_cts.IsCancellationRequested)
        {
            CurrentStatusText.Text = "❌ Ошибка при установке зависимостей. Проверьте лог выше.";
            InstallButton.IsEnabled = true;
            InstallButton.Content = "Повторить попытку";
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }

    private void UpdateUiProgress(DependencyInstallProgress progress)
    {
        _logBuilder.AppendLine(progress.LogLine);
        if (_logBuilder.Length > 15000)
        {
            _logBuilder.Remove(0, 5000);
        }

        LogOutputText.Text = _logBuilder.ToString();
        LogScrollViewer.ScrollToEnd();

        CurrentStatusText.Text = progress.StageTitle;
        if (progress.Percentage.HasValue)
        {
            InstallProgressBar.IsIndeterminate = false;
            InstallProgressBar.Value = progress.Percentage.Value;
        }
        else
        {
            InstallProgressBar.IsIndeterminate = true;
        }
    }
}