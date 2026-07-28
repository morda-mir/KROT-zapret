using System;

namespace KROT.Core.Models;

public sealed class RuntimeOutputEvent : EventArgs
{
    public string Role { get; set; } = string.Empty;

    public string Line { get; set; } = string.Empty;

    public bool IsError { get; set; }
}
