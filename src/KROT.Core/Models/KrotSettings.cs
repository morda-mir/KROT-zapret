using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace KROT.Core.Models;

public sealed class KrotSettings
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Language { get; set; } = "ru";

    public bool AutoStart { get; set; }

    public bool RestoreEnabledState { get; set; }

    public bool DetailedLogs { get; set; }

    public string LastUpdateNotificationVersion { get; set; } = string.Empty;

    public DateTime? LastUpdateNotificationUtc { get; set; }

    public string TelegramProxySecret { get; set; } = CreateTelegramProxySecret();

    public bool TelegramProxyConfigured { get; set; }

    public List<ServiceSelection> Services { get; set; } = new()
    {
        new ServiceSelection { Id = ServiceId.Discord, IsEnabled = true },
        new ServiceSelection { Id = ServiceId.YouTube, IsEnabled = true },
        new ServiceSelection { Id = ServiceId.Telegram, IsEnabled = false }
    };

    public static bool IsValidTelegramProxySecret(string? secret) =>
        secret != null
        && secret.Length == 32
        && secret.All(Uri.IsHexDigit);

    public static string CreateTelegramProxySecret()
    {
        var bytes = new byte[16];
        using (var generator = RandomNumberGenerator.Create())
        {
            generator.GetBytes(bytes);
        }

        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }
}
