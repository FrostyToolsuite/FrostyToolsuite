namespace Frosty.Sdk.Interfaces;

public interface ILogger
{
    public void LogInfo(string message);
    public void LogWarning(string message);
    public void LogError(string message);

    public void LogProgress(double progress);
}