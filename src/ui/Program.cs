using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Avalonia;

namespace LiveTranslateOverlay;

internal static class Program
{
    private static readonly string LibDirectory = Path.Combine(AppContext.BaseDirectory, "lib");

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [STAThread]
    public static void Main(string[] args)
    {
        RegisterLibDirectoryResolvers();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL CRASH:\n{ex}\n");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static void RegisterLibDirectoryResolvers()
    {
        if (!Directory.Exists(LibDirectory)) return;

        // 1. Разрешение нативных C/C++ библиотек (libSkiaSharp.dll, av_libglesv2.dll, libHarfBuzzSharp.dll)
        if (OperatingSystem.IsWindows())
        {
            SetDllDirectory(LibDirectory);
        }

        // 2. Разрешение управляемых .NET сборок (Avalonia.*.dll, System.*.dll) из папки lib/
        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            string candidatePath = Path.Combine(LibDirectory, $"{assemblyName.Name}.dll");
            return File.Exists(candidatePath)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidatePath)
                : null;
        };

        // 3. Дополнительный резолвер нативных библиотек для P/Invoke и DllImport
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, libraryName) =>
        {
            string fileName = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? libraryName
                : $"{libraryName}.dll";

            string candidatePath = Path.Combine(LibDirectory, fileName);
            if (File.Exists(candidatePath) && NativeLibrary.TryLoad(candidatePath, out IntPtr handle))
            {
                return handle;
            }

            return IntPtr.Zero;
        };
    }
}