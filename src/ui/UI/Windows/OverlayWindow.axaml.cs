using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LiveTranslateOverlay.Core.Configuration;
using LiveTranslateOverlay.Core.Models.Ipc;
using LiveTranslateOverlay.Core.Platform;
using LiveTranslateOverlay.Services;
using LiveTranslateOverlay.UI.Layout;

namespace LiveTranslateOverlay.UI.Windows;

public partial class OverlayWindow : Window
{
    private readonly AppSettings _settings;
    private readonly PythonBridgeService _bridgeService;
    private readonly SubtitleLayoutManager _layoutManager;
    private readonly SubtitleOrchestrator _orchestrator;
    private readonly HistoryWindow _historyWindow;
    private ModelDownloadWindow? _downloadWindow;

    public OverlayWindow()
    {
        InitializeComponent();
        _settings = new AppSettings();
        _bridgeService = null!;
        _layoutManager = null!;
        _orchestrator = null!;
        _historyWindow = null!;
    }

    public OverlayWindow(AppSettings settings, PythonBridgeService bridgeService)
    {
        InitializeComponent();

        _settings = settings;
        _bridgeService = bridgeService;
        _layoutManager = new SubtitleLayoutManager(SubtitleContainer, _settings.Ui);
        _historyWindow = new HistoryWindow();
        _orchestrator = new SubtitleOrchestrator(_layoutManager, _historyWindow, _settings);

        _historyWindow.BindActions(
            onTogglePause: paused => _bridgeService.IsPaused = paused,
            onRestartModel: () => _bridgeService.Restart());

        _bridgeService.EventReceived += OnBridgeEventReceived;
        _bridgeService.StatusNoticeRequested += msg => _orchestrator.ShowTemporaryNotice(msg);

        _historyWindow.Closed += (_, _) => Close();
        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowClickThroughHelper.EnableClickThrough(this);
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        var screen = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        Width = screen.Width;
        Height = screen.Height / _settings.Ui.Overlay.WindowHeightDivisor;
        Position = new PixelPoint(screen.X, screen.Y + screen.Height - (int)Height);

        double fontSize = screen.Width * _settings.Ui.Overlay.FontSizeMultiplier;
        _layoutManager.ConfigureDimensions(Width, Height, fontSize);

        _historyWindow.Show();
        _bridgeService.Start();
    }

    private void OnBridgeEventReceived(IpcEventDto ipcEvent)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            switch (ipcEvent)
            {
                case DownloadRequiredEventDto req:
                    ShowModelDownloadDialog(req);
                    break;

                case DownloadProgressEventDto progress:
                    _downloadWindow?.UpdateProgress(progress);
                    break;

                case DownloadCompletedEventDto:
                    _downloadWindow?.MarkCompleted();
                    _downloadWindow = null;
                    _historyWindow.AddSystemRecord("Модели успешно загружены.");
                    break;

                default:
                    _orchestrator.ProcessEvent(ipcEvent);
                    break;
            }
        });
    }

    private void ShowModelDownloadDialog(DownloadRequiredEventDto dto)
    {
        _downloadWindow?.Close();
        _downloadWindow = new ModelDownloadWindow(
            dto.Models,
            onConfirmed: async () => await _bridgeService.SendCommandAsync(new ConfirmDownloadCommandDto()),
            onCancelled: async () =>
            {
                await _bridgeService.SendCommandAsync(new CancelCommandDto());
                Close();
            });

        _downloadWindow.Show(_historyWindow);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _bridgeService.Dispose();
        if (_downloadWindow is { IsVisible: true })
        {
            _downloadWindow.Close();
        }
        if (_historyWindow is { IsVisible: true })
        {
            _historyWindow.Close();
        }
    }
}