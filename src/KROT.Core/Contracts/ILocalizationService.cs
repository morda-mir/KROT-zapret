using System;

namespace KROT.Core.Contracts;

public interface ILocalizationService
{
    string Language { get; }

    event EventHandler? LanguageChanged;

    string Get(string key);

    void SetLanguage(string language);
}

