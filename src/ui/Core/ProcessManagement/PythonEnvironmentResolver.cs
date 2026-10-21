using System;
using System.IO;
using System.Linq;

namespace LiveTranslateOverlay.Core.ProcessManagement;

public static class PythonEnvironmentResolver
{
    private static readonly string[] VenvFolderNames = { ".venv", "venv", "env", ".env" };


    public static string ResolveRequirementsPath(string appRootDir)
    {
        string portablePath = Path.GetFullPath(Path.Combine(appRootDir, "engine", "requirements.txt"));
        if (File.Exists(portablePath))
        {
            return portablePath;
        }

        string devPath = Path.GetFullPath(Path.Combine(appRootDir, "src", "engine", "requirements.txt"));
        if (File.Exists(devPath))
        {
            return devPath;
        }

        throw new FileNotFoundException(
            $"Could not find requirements.txt. Checked:\n1) {portablePath}\n2) {devPath}");
    }
    public static string ResolveScriptPath(string appRootDir, string configuredRelativePath)
    {
        if (Path.IsPathRooted(configuredRelativePath) && File.Exists(configuredRelativePath))
        {
            return Path.GetFullPath(configuredRelativePath);
        }

        // 1. Прямой путь относительно корня (например, в Portable: Root/engine/ipc/bridge.py)
        string directPath = Path.GetFullPath(Path.Combine(appRootDir, configuredRelativePath));
        if (File.Exists(directPath))
        {
            return directPath;
        }

        // 2. Путь в структуре репозитория при разработке (Root/src/engine/ipc/bridge.py)
        string devSrcPath = Path.GetFullPath(Path.Combine(appRootDir, "src", configuredRelativePath));
        if (File.Exists(devSrcPath))
        {
            return devSrcPath;
        }

        throw new FileNotFoundException(
            $"Python script not found. Checked paths:\n1) {directPath}\n2) {devSrcPath}");
    }

    public static string ResolveExecutable(string appRootDir, string resolvedScriptPath)
    {
        // 1. Portable runtime (uv python install)
        string bundledRuntimeDir = Path.Combine(appRootDir, "runtime", "python");
        if (Directory.Exists(bundledRuntimeDir))
        {
            string? bundledExe = Directory
                .EnumerateFiles(
                    bundledRuntimeDir,
                    OperatingSystem.IsWindows() ? "python.exe" : "python",
                    SearchOption.AllDirectories)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(bundledExe))
            {
                return bundledExe;
            }
        }

        string? scriptDir = Path.GetDirectoryName(resolvedScriptPath);
        string? engineRootDir = scriptDir != null ? Directory.GetParent(scriptDir)?.FullName : null;

        string[] searchDirectories = new[] { appRootDir, engineRootDir, scriptDir }
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct()
            .ToArray()!;

        foreach (string dir in searchDirectories)
        {
            foreach (string venvName in VenvFolderNames)
            {
                string venvPath = Path.Combine(dir, venvName);
                if (!Directory.Exists(venvPath)) continue;

                string candidate = OperatingSystem.IsWindows()
                    ? Path.Combine(venvPath, "Scripts", "python.exe")
                    : Path.Combine(venvPath, "bin", "python");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // 3. Fallback to system PATH
        return "python";
    }
}