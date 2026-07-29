using System;
using System.Linq;
using System.Reflection;

namespace KROT.App.Services;

public static class ApplicationVersionProvider
{
    public static string Current { get; } = ReadCurrentVersion();

    private static string ReadCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ApplicationVersionProvider).Assembly;
        var informationalVersion = assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var buildSeparator = informationalVersion!.IndexOf('+');
            return buildSeparator >= 0
                ? informationalVersion.Substring(0, buildSeparator)
                : informationalVersion;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
