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
                ["Action.Enable"] = "Включить",
                ["Action.Disable"] = "Выключить",
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
                ["Service.AiServices"] = "Нейросети",
                ["Menu.Language"] = "Язык",
                ["Menu.Logs"] = "Подробные логи",
                ["Menu.ViewLogs"] = "Посмотреть логи",
                ["Menu.Support"] = "Discord поддержки",
                ["Menu.Zapret"] = "Официальный Zapret",
                ["Menu.Licenses"] = "Лицензии",
                ["Menu.Version"] = "Версия 0.1.0-alpha",
                ["Footer.Runtime"] = "KROT управляет только запущенными им процессами. Системные сетевые настройки не изменяются.",
                ["Channel.Discord.Text"] = "Discord: текст и API",
                ["Channel.Discord.Media"] = "Discord: медиа и CDN",
                ["Channel.Discord.Voice"] = "Discord: голос",
                ["Channel.YouTube.Site"] = "YouTube: основной сайт",
                ["Channel.YouTube.Video"] = "YouTube: видеопоток",
                ["Channel.YouTube.Quic"] = "YouTube: QUIC и HTTP/3",
                ["Channel.Ai.Site"] = "Нейросети: сайт",
                ["Channel.Ai.Stream"] = "Нейросети: потоковый ответ",
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
                ["Action.Enable"] = "Enable",
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
                ["Service.AiServices"] = "AI services",
                ["Menu.Language"] = "Language",
                ["Menu.Logs"] = "Detailed logs",
                ["Menu.ViewLogs"] = "View logs",
                ["Menu.Support"] = "Support Discord",
                ["Menu.Zapret"] = "Official Zapret",
                ["Menu.Licenses"] = "Licenses",
                ["Menu.Version"] = "Version 0.1.0-alpha",
                ["Footer.Runtime"] = "KROT controls only the processes it starts. System network settings are not changed.",
                ["Channel.Discord.Text"] = "Discord text and API",
                ["Channel.Discord.Media"] = "Discord media and CDN",
                ["Channel.Discord.Voice"] = "Discord voice",
                ["Channel.YouTube.Site"] = "YouTube website",
                ["Channel.YouTube.Video"] = "YouTube video stream",
                ["Channel.YouTube.Quic"] = "YouTube QUIC and HTTP/3",
                ["Channel.Ai.Site"] = "AI service website",
                ["Channel.Ai.Stream"] = "AI streaming response",
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
