/**
 * Manages the lifecycle of the Python backend process and provides
 * bidirectional JSON-over-Stdio communication using strongly-typed DTOs.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LiveTranslateOverlay.Core.Configuration;
using LiveTranslateOverlay.Core.Models.Ipc;
using LiveTranslateOverlay.Core.ProcessManagement;

namespace LiveTranslateOverlay.Services;

public sealed class PythonBridgeService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly string _appRootDir;
    private readonly string _configFilePath;
    private readonly string? _overrideConfigFilePath;
    private Process? _process;
    private volatile bool _isPaused;

    public event Action<IpcEventDto>? EventReceived;
    public event Action<string>? StatusNoticeRequested;

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            _isPaused = value;
            StatusNoticeRequested?.Invoke(_isPaused ? "Распознавание на паузе" : "Распознавание возобновлено");
        }
    }

    public PythonBridgeService(
        AppSettings settings,
        string appRootDir,
        string configFilePath,
        string? overrideConfigFilePath = null)
    {
        _settings = settings;
        _appRootDir = Path.GetFullPath(appRootDir);
        _configFilePath = Path.GetFullPath(configFilePath);
        _overrideConfigFilePath = !string.IsNullOrWhiteSpace(overrideConfigFilePath) && File.Exists(overrideConfigFilePath)
            ? Path.GetFullPath(overrideConfigFilePath)
            : null;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
    }

    public void Start()
    {
        Stop();

        string scriptFullPath = PythonEnvironmentResolver.ResolveScriptPath(
            _appRootDir,
            _settings.General.PythonScriptPath);

        string pythonExe = PythonEnvironmentResolver.ResolveExecutable(_appRootDir, scriptFullPath);
        string workingDir = Path.GetDirectoryName(scriptFullPath) ?? _appRootDir;

        string args = $"-u \"{scriptFullPath}\" --config \"{_configFilePath}\" --root \"{_appRootDir}\"";
        if (_overrideConfigFilePath != null)
        {
            args += $" --override-config \"{_overrideConfigFilePath}\"";
        }

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = utf8NoBom,
            StandardOutputEncoding = utf8NoBom,
            StandardErrorEncoding = utf8NoBom
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";

        try
        {
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            ChildProcessGuard.Attach(_process);
        }
        catch (Exception ex)
        {
            EventReceived?.Invoke(new ErrorEventDto($"Ошибка запуска Python: {ex.Message}"));
        }
    }

    public void Restart()
    {
        StatusNoticeRequested?.Invoke("Перезапуск модели...");
        Start();
    }

    public async Task SendCommandAsync(IpcCommandDto command)
    {
        if (_process == null || _process.HasExited) return;

        string json = JsonSerializer.Serialize<IpcCommandDto>(command);
        await _process.StandardInput.WriteLineAsync(json).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync().ConfigureAwait(false);
    }

    public void Stop()
    {
        if (_process == null) return;

        try
        {
            _process.OutputDataReceived -= OnOutputDataReceived;
            _process.ErrorDataReceived -= OnErrorDataReceived;

            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore access errors during shutdown
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public void Dispose() => Stop();

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;

        try
        {
            var dto = JsonSerializer.Deserialize<IpcEventDto>(e.Data);
            if (dto == null) return;

            if (_isPaused && dto is NewTextEventDto or AddTextEventDto or TranslationEventDto)
            {
                return;
            }

            EventReceived?.Invoke(dto);
        }
        catch (JsonException)
        {
            // Ignore malformed non-JSON lines
        }
    }

    private static void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            Debug.WriteLine($"[Python Stderr]: {e.Data}");
        }
    }
}