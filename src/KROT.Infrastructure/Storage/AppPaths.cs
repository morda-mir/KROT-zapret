using System;
using System.IO;

namespace KROT.Infrastructure.Storage;

public static class AppPaths
{
    public static string UserDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KROT zapret");

    public static string CommonDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KROT zapret");

    public static string SettingsFile => Path.Combine(UserDataRoot, "settings.json");

    public static string LogsDirectory => Path.Combine(UserDataRoot, "logs");

    public static string ServiceStateDirectory => Path.Combine(CommonDataRoot, "service-state");

    public static string ServiceLogsDirectory => Path.Combine(ServiceStateDirectory, "logs");
}
