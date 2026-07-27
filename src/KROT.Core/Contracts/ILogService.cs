using System;

namespace KROT.Core.Contracts;

public interface ILogService
{
    void Info(string eventName, string message);

    void Error(string eventName, string message, Exception? exception = null);
}

