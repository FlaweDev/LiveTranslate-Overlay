/**
 * Infrastructure service responsible for verifying the Python environment,
 * checking CUDA driver compatibility, and sequentially installing PyTorch
 * followed by requirements.txt packages.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LiveTranslateOverlay.Core.Configuration;
using LiveTranslateOverlay.Core.ProcessManagement;

namespace LiveTranslateOverlay.Services;

public sealed record CudaCompatibilityReport(
    bool IsCompatible,
    string TargetCudaTag,
    string? DetectedDriverCuda,
    string? DetectedToolkitCuda,
    string? WarningMessage);

public sealed record DependencyInstallProgress(
    string StageTitle,
    string LogLine,
    double? Percentage,
    bool IsError = false);

public sealed class PythonDependenciesInstaller
{
    private static readonly Regex NvidiaSmiCudaRegex = new(@"CUDA Version:\s*(\d+\.\d+)", RegexOptions.Compiled);
    private static readonly Regex NvccReleaseRegex = new(@"release\s*(\d+\.\d+)", RegexOptions.Compiled);
    private static readonly Regex PercentRegex = new(@"(\d{1,3})%", RegexOptions.Compiled);

    private readonly AppSettings _settings;
    private readonly string _appRootDir;

    public PythonDependenciesInstaller(AppSettings settings, string appRootDir)
    {
        _settings = settings;
        _appRootDir = Path.GetFullPath(appRootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public async Task<bool> AreAllDependenciesInstalledAsync()
    {
        string pythonExe = ResolvePythonExe();
        const string fastCheckScript =
            "import importlib.util, sys; " +
            "mods = ['torch', 'torchaudio', 'nemo', 'soundcard', 'soundfile', 'resampy', 'llama_cpp']; " +
            "sys.exit(0 if all(importlib.util.find_spec(m) is not None for m in mods) else 1)";

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"-c \"{fastCheckScript}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(startInfo);
            if (proc == null) return false;
            await proc.WaitForExitAsync().ConfigureAwait(false);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<CudaCompatibilityReport> CheckCudaCompatibilityAsync()
    {
        string targetTag = _settings.General.Pytorch.CudaVersion.Trim().ToLowerInvariant();
        string? driverCudaStr = await QueryCommandRegexAsync("nvidia-smi", "", NvidiaSmiCudaRegex).ConfigureAwait(false);
        string? toolkitCudaStr = await QueryCommandRegexAsync("nvcc", "--version", NvccReleaseRegex).ConfigureAwait(false);

        if (targetTag == "cpu")
        {
            return new CudaCompatibilityReport(true, targetTag, driverCudaStr, toolkitCudaStr, null);
        }

        if (!TryParseCuTagVersion(targetTag, out Version? requiredCuda))
        {
            return new CudaCompatibilityReport(
                true,
                targetTag,
                driverCudaStr,
                toolkitCudaStr,
                $"Нестандартный тег версии ({targetTag}). Автоматическая проверка совместимости пропущена.");
        }

        if (string.IsNullOrEmpty(driverCudaStr))
        {
            return new CudaCompatibilityReport(
                false,
                targetTag,
                null,
                toolkitCudaStr,
                $"⚠️ Видеокарта NVIDIA или драйвер не обнаружены (nvidia-smi недоступен), но выбрана сборка '{targetTag}' (CUDA {requiredCuda.Major}.{requiredCuda.Minor}). " +
                "Установите драйверы NVIDIA или смените 'cudaVersion' на 'cpu' в appsettings.json.");
        }

        if (Version.TryParse(driverCudaStr, out Version? driverVersion) && driverVersion < requiredCuda)
        {
            return new CudaCompatibilityReport(
                false,
                targetTag,
                driverCudaStr,
                toolkitCudaStr,
                $"⚠️ Несовместимость версий CUDA!\n" +
                $"Выбранная сборка PyTorch ({targetTag}) требует поддержку CUDA {requiredCuda.Major}.{requiredCuda.Minor}, " +
                $"но ваш текущий драйвер NVIDIA поддерживает максимум CUDA {driverCudaStr}.\n" +
                $"Рекомендуется обновить драйвер видеокарты или изменить 'cudaVersion' в appsettings.json на 'cu{driverVersion.Major}{driverVersion.Minor}'.");
        }

        return new CudaCompatibilityReport(true, targetTag, driverCudaStr, toolkitCudaStr, null);
    }

    /// <summary>
    /// Выполняет последовательную установку: 1) PyTorch + torchaudio, 2) requirements.txt.
    /// </summary>
    public async Task<bool> InstallAllAsync(
        Action<DependencyInstallProgress> onProgress,
        CancellationToken cancellationToken)
    {
        string pythonExe = ResolvePythonExe();
        string requirementsPath = PythonEnvironmentResolver.ResolveRequirementsPath(_appRootDir);
        string bundledUv = Path.Combine(_appRootDir, "runtime", "uv.exe");
        bool useUv = File.Exists(bundledUv);

        // --- ЭТАП 1: Установка PyTorch и Torchaudio ---
string torchPackages = string.Join(" ", _settings.General.Pytorch.Packages);
string indexUrl = _settings.General.Pytorch.GetResolvedIndexUrl();

string stage1Title = $"Этап 1/2: Установка PyTorch ({_settings.General.Pytorch.CudaVersion})...";
string stage1Args = useUv
    ? $"pip install --python \"{pythonExe}\" --break-system-packages {torchPackages} --index-url {indexUrl}"
    : $"-m pip install --break-system-packages {torchPackages} --index-url {indexUrl} --progress-bar on";

bool stage1Ok = await RunPackageManagerCommandAsync(
    useUv ? bundledUv : pythonExe,
    stage1Args,
    stage1Title,
    baseProgressOffset: 0,
    progressScale: 0.5,
    onProgress,
    cancellationToken).ConfigureAwait(false);

if (!stage1Ok) return false;

// --- ЭТАП 2: Установка остальных библиотек из requirements.txt ---
string stage2Title = "Этап 2/2: Установка библиотек из requirements.txt (NeMo, llama.cpp и др.)...";
string stage2Args = useUv
    ? $"pip install --python \"{pythonExe}\" --break-system-packages -r \"{requirementsPath}\""
    : $"-m pip install --break-system-packages -r \"{requirementsPath}\" --progress-bar on";

        bool stage2Ok = await RunPackageManagerCommandAsync(
            useUv ? bundledUv : pythonExe,
            stage2Args,
            stage2Title,
            baseProgressOffset: 50,
            progressScale: 0.5,
            onProgress,
            cancellationToken).ConfigureAwait(false);

        return stage2Ok;
    }

    private async Task<bool> RunPackageManagerCommandAsync(
        string executable,
        string arguments,
        string stageTitle,
        double baseProgressOffset,
        double progressScale,
        Action<DependencyInstallProgress> onProgress,
        CancellationToken cancellationToken)
    {
        onProgress(new DependencyInstallProgress(
            stageTitle,
            $"> {Path.GetFileName(executable)} {arguments}",
            baseProgressOffset));

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = _appRootDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        void HandleLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            double? scaledPct = null;
            var match = PercentRegex.Match(line);
            if (match.Success &&
                double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double rawPct))
            {
                scaledPct = baseProgressOffset + (rawPct * progressScale);
            }

            onProgress(new DependencyInstallProgress(stageTitle, line.Trim(), scaledPct));
        }

        process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data);

        process.Start();
        ChildProcessGuard.Attach(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            return false;
        }
    }

    private string ResolvePythonExe()
    {
        string scriptPath = PythonEnvironmentResolver.ResolveScriptPath(_appRootDir, _settings.General.PythonScriptPath);
        return PythonEnvironmentResolver.ResolveExecutable(_appRootDir, scriptPath);
    }

    private static bool TryParseCuTagVersion(string tag, out Version? version)
    {
        version = null;
        if (!tag.StartsWith("cu") || tag.Length < 5) return false;

        string digits = tag[2..];
        if (!int.TryParse(digits, out _)) return false;

        string majorStr = digits[..^1];
        string minorStr = digits[^1..];
        return Version.TryParse($"{majorStr}.{minorStr}", out version);
    }

    private static async Task<string?> QueryCommandRegexAsync(string fileName, string args, Regex regex)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            string stdout = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            string stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await proc.WaitForExitAsync().ConfigureAwait(false);

            var match = regex.Match(stdout + "\n" + stderr);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}