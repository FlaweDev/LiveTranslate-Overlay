using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LiveTranslateOverlay.Core.Configuration;
using LiveTranslateOverlay.Services;
using LiveTranslateOverlay.UI.Windows;

namespace LiveTranslateOverlay;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var (appRoot, baseConfigPath, overrideConfigPath) = ResolvePaths();
            var settings = AppSettings.Load(baseConfigPath, overrideConfigPath);

            if (settings.General.Pytorch.AutoInstall)
            {
                var installer = new PythonDependenciesInstaller(settings, appRoot);
                bool ready = await installer.AreAllDependenciesInstalledAsync();

                if (!ready)
                {
                    var cudaReport = await installer.CheckCudaCompatibilityAsync();
                    var installTcs = new TaskCompletionSource<bool>();

                    var installWindow = new DependencyInstallWindow(
                        installer,
                        settings.General.Pytorch,
                        cudaReport,
                        onInstallCompleted: () => installTcs.TrySetResult(true),
                        onInstallCancelled: () => installTcs.TrySetResult(false));

                    installWindow.Show();

                    bool installed = await installTcs.Task;
                    if (!installed)
                    {
                        desktop.Shutdown();
                        return;
                    }
                }
            }

            StartMainApplication(desktop, settings, appRoot, baseConfigPath, overrideConfigPath);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void StartMainApplication(
        IClassicDesktopStyleApplicationLifetime desktop,
        AppSettings settings,
        string appRoot,
        string baseConfigPath,
        string? overrideConfigPath)
    {
        var bridgeService = new PythonBridgeService(settings, appRoot, baseConfigPath, overrideConfigPath);
        var overlayWindow = new OverlayWindow(settings, bridgeService);

        desktop.MainWindow = overlayWindow;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        overlayWindow.Show();
    }

    private static (string AppRoot, string BaseConfigPath, string? OverrideConfigPath) ResolvePaths()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string? resolvedRoot = null;
        string? resolvedBaseConfig = null;

        string portableConfig = Path.Combine(baseDir, "appsettings.json");
        if (File.Exists(portableConfig))
        {
            resolvedRoot = baseDir;
            resolvedBaseConfig = portableConfig;
        }
        else
        {
            string portableSubConfig = Path.Combine(baseDir, "config", "appsettings.json");
            if (File.Exists(portableSubConfig))
            {
                resolvedRoot = baseDir;
                resolvedBaseConfig = portableSubConfig;
            }
            else
            {
                DirectoryInfo? current = new DirectoryInfo(baseDir);
                while (current != null)
                {
                    string devConfig = Path.Combine(current.FullName, "config", "appsettings.json");
                    if (File.Exists(devConfig))
                    {
                        resolvedRoot = current.FullName;
                        resolvedBaseConfig = devConfig;
                        break;
                    }
                    current = current.Parent;
                }
            }
        }

        if (resolvedRoot == null || resolvedBaseConfig == null)
        {
            throw new FileNotFoundException("Could not locate appsettings.json.");
        }

        string? overrideConfig = null;
        bool isDevEnvironment =
    #if DEBUG
            true;
    #else
            string.Equals(
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
                "Development",
                StringComparison.OrdinalIgnoreCase);
    #endif

        if (isDevEnvironment)
        {
            string configDir = Path.GetDirectoryName(resolvedBaseConfig) ?? resolvedRoot;
            string candidateOverride = Path.Combine(configDir, "appsettings.Development.json");
            if (File.Exists(candidateOverride))
            {
                overrideConfig = candidateOverride;
            }
        }

        return (resolvedRoot, resolvedBaseConfig, overrideConfig);
    }
}