namespace KROT.Core.Contracts;

public interface IConfigurableLogService : ILogService
{
    bool Detailed { get; set; }
}
