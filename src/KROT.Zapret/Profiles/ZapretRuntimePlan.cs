using System;
using System.Collections.Generic;

namespace KROT.Zapret.Profiles;

public sealed class ZapretRuntimePlan
{
    public IReadOnlyList<string> MainArguments { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> VoiceArguments { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> PresetIds { get; set; } = Array.Empty<string>();

    public bool HasMain => MainArguments.Count > 0;

    public bool HasVoice => VoiceArguments.Count > 0;
}
