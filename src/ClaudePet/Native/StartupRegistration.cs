using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ClaudePet.Native;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudePet";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
            return;

        if (enabled)
        {
            var command = BuildStartupCommand();
            if (command is not null)
                key.SetValue(ValueName, command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string? BuildStartupCommand()
    {
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (processPath is null)
            return null;

        // A framework-dependent build launched as `dotnet ClaudePet.dll` runs
        // under the generic `dotnet` host, so ProcessPath resolves to
        // dotnet.exe itself rather than this app - registering just that path
        // with no argument would start dotnet.exe with nothing to run on
        // login. Append the DLL path in that case; a self-contained/apphost
        // launch (ProcessPath already pointing at ClaudePet.exe) needs no
        // argument.
        var processFileName = Path.GetFileNameWithoutExtension(processPath);
        if (string.Equals(processFileName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dllPath = Assembly.GetExecutingAssembly().Location;
            return $"\"{processPath}\" \"{dllPath}\"";
        }

        return $"\"{processPath}\"";
    }
}
