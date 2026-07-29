using System;
using System.Collections.Generic;
using KROT.Core.Contracts;

namespace KROT.Localization;

public sealed class DictionaryLocalizationService : ILocalizationService
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Resources =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ru"] = new Dictionary<string, string>
            {
                ["App.Title"] = "KROT zapret",
                ["App.Subtitle"] = "локальная диагностика и профили",
                ["AutoStart"] = "Автозапуск",
                ["Warning.DisableVpnProxy"] = "Отключи VPN/прокси",
                ["Action.Enable"] = "Включить",
                ["Action.Starting"] = "Запуск",
                ["Action.Cancel"] = "Отменить",
                ["Action.Disable"] = "Отключить",
                ["Action.Open"] = "Открыть",
                ["Action.Exit"] = "Выход",
                ["Action.Toggle"] = "Включить / выключить",
                ["Status.Off"] = "Доступ выключен",
                ["Status.Starting"] = "Проверка выбранных сервисов",
                ["Status.TestingDirect"] = "Проверка прямого доступа",
                ["Status.Searching"] = "Подбор рабочих профилей",
                ["Status.Running"] = "Доступ включён",
                ["Status.Stopping"] = "Остановка",
                ["Status.Error"] = "Не удалось запустить доступ",
                ["Service.Discord"] = "Discord",
                ["Service.YouTube"] = "YouTube",
                ["Menu.Language"] = "Язык",
                ["Menu.Logs"] = "Подробные логи",
                ["Menu.ViewLogs"] = "Посмотреть логи",
                ["Menu.About"] = "О приложении",
                ["Menu.ProjectRepository"] = "KROT zapret",
                ["Menu.Zapret"] = "Zapret",
                ["Menu.Licenses"] = "Лицензия",
                ["Menu.Version"] = "Версия",
                ["Update.Available"] = "Доступна версия {0}",
                ["Update.NoDescription"] = "Описание релиза не указано.",
                ["Update.OpenRelease"] = "Нажмите, чтобы открыть страницу релиза.",
                ["Update.Tray.Title"] = "Доступно обновление KROT",
                ["Update.Tray.Text"] = "Версия {0} доступна для загрузки.",
                ["Footer.Runtime"] = "KROT управляет только запущенными им процессами. Системные сетевые настройки не изменяются.",
                ["Channel.Discord.Text"] = "Текст",
                ["Channel.Discord.Media"] = "Медиа",
                ["Channel.Discord.Voice"] = "Голос",
                ["Channel.Discord.Voice.Waiting"] = "Зайдите в голосовой канал",
                ["Channel.Discord.Voice.Checking"] = "Проверяю подключение",
                ["Channel.Error.Logs"] = "Ошибка, смотрите логи.",
                ["Channel.YouTube.Site"] = "Сайт",
                ["Channel.YouTube.Video"] = "Видео",
                ["Channel.YouTube.Quic"] = "YouTube: QUIC и HTTP/3",
                ["ChannelState.Disabled"] = "отключено",
                ["ChannelState.Unknown"] = "нет данных",
                ["ChannelState.WaitingForActivity"] = "ожидание активности",
                ["ChannelState.Testing"] = "проверка",
                ["ChannelState.Searching"] = "подбор обхода",
                ["ChannelState.WorkingDirect"] = "работает напрямую",
                ["ChannelState.WorkingPreset"] = "обход работает",
                ["ChannelState.Failed"] = "не отвечает",
                ["Logs.Refresh"] = "Обновить",
                ["Logs.Copy"] = "Копировать",
                ["Logs.OpenFolder"] = "Открыть папку",
                ["Logs.ClearOld"] = "Очистить старые",
                ["Logs.Empty"] = "Лог пока пуст.",
                ["Journal.Ready"] = "Готово к запуску",
                ["Journal.Direct"] = "Проверка прямого доступа",
                ["Journal.Profiles"] = "Проверка сохранённых профилей",
                ["Journal.Enabled"] = "Доступ включён в отладочном режиме",
                ["Journal.Disabled"] = "Доступ выключен"
            },
            ["en"] = new Dictionary<string, string>
            {
                ["App.Title"] = "KROT zapret",
                ["App.Subtitle"] = "local diagnostics and profiles",
                ["AutoStart"] = "Run at startup",
                ["Warning.DisableVpnProxy"] = "Disable VPN/proxy",
                ["Action.Enable"] = "Enable",
                ["Action.Starting"] = "Starting",
                ["Action.Cancel"] = "Cancel",
                ["Action.Disable"] = "Disable",
                ["Action.Open"] = "Open",
                ["Action.Exit"] = "Exit",
                ["Action.Toggle"] = "Enable / disable",
                ["Status.Off"] = "Access is off",
                ["Status.Starting"] = "Checking selected services",
                ["Status.TestingDirect"] = "Checking direct access",
                ["Status.Searching"] = "Searching for working profiles",
                ["Status.Running"] = "Access enabled",
                ["Status.Stopping"] = "Stopping",
                ["Status.Error"] = "Could not enable access",
                ["Service.Discord"] = "Discord",
                ["Service.YouTube"] = "YouTube",
                ["Menu.Language"] = "Language",
                ["Menu.Logs"] = "Detailed logs",
                ["Menu.ViewLogs"] = "View logs",
                ["Menu.About"] = "About",
                ["Menu.ProjectRepository"] = "KROT zapret",
                ["Menu.Zapret"] = "Zapret",
                ["Menu.Licenses"] = "License",
                ["Menu.Version"] = "Version",
                ["Update.Available"] = "Version {0} is available",
                ["Update.NoDescription"] = "No release description was provided.",
                ["Update.OpenRelease"] = "Click to open the release page.",
                ["Update.Tray.Title"] = "KROT update available",
                ["Update.Tray.Text"] = "Version {0} is available to download.",
                ["Footer.Runtime"] = "KROT controls only the processes it starts. System network settings are not changed.",
                ["Channel.Discord.Text"] = "Text",
                ["Channel.Discord.Media"] = "Media",
                ["Channel.Discord.Voice"] = "Voice",
                ["Channel.Discord.Voice.Waiting"] = "Join a voice channel",
                ["Channel.Discord.Voice.Checking"] = "Checking connection",
                ["Channel.Error.Logs"] = "Error, see logs.",
                ["Channel.YouTube.Site"] = "Site",
                ["Channel.YouTube.Video"] = "Video",
                ["Channel.YouTube.Quic"] = "YouTube QUIC and HTTP/3",
                ["ChannelState.Disabled"] = "disabled",
                ["ChannelState.Unknown"] = "no data",
                ["ChannelState.WaitingForActivity"] = "waiting for activity",
                ["ChannelState.Testing"] = "checking",
                ["ChannelState.Searching"] = "selecting a bypass",
                ["ChannelState.WorkingDirect"] = "working directly",
                ["ChannelState.WorkingPreset"] = "bypass is working",
                ["ChannelState.Failed"] = "not responding",
                ["Logs.Refresh"] = "Refresh",
                ["Logs.Copy"] = "Copy",
                ["Logs.OpenFolder"] = "Open folder",
                ["Logs.ClearOld"] = "Clear old logs",
                ["Logs.Empty"] = "The log is empty.",
                ["Journal.Ready"] = "Ready",
                ["Journal.Direct"] = "Checking direct access",
                ["Journal.Profiles"] = "Checking saved profiles",
                ["Journal.Enabled"] = "Access enabled in debug mode",
                ["Journal.Disabled"] = "Access disabled"
            }
        };

    public DictionaryLocalizationService(string language)
    {
        Language = Normalize(language);
    }

    public string Language { get; private set; }

    public event EventHandler? LanguageChanged;

    public string Get(string key)
    {
        if (Resources[Language].TryGetValue(key, out var value))
        {
            return value;
        }

        return Resources["en"].TryGetValue(key, out value) ? value : key;
    }

    public void SetLanguage(string language)
    {
        var normalized = Normalize(language);
        if (string.Equals(Language, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Language = normalized;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Normalize(string language) =>
        language.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
}
