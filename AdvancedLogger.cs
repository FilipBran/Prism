namespace Prism;

using Allumeria;

public sealed class AdvancedLogger
{
    private static string LogFile;
    private static StreamWriter Writer;
    
    public static void Init(string logFile)
    {
        LogFile = logFile;
    }

    public static void Log(string message, LogType type)
    {
        using var writer = new StreamWriter(LogFile, append: true);
        switch (type)
        {
            case LogType.Info:
                writer.WriteLine($"[{DateTime.Now}] [INFO] {message}");
                Allumeria.Logger.Info(message);
                break;
            case LogType.Debug:
                writer.WriteLine($"[{DateTime.Now}] [DEBUG] {message}");
                Allumeria.Logger.Info(message);
                break;
            case LogType.Warning:
                writer.WriteLine($"[{DateTime.Now}] [WARN] {message}");
                Allumeria.Logger.Warn(message);
                break;
            case LogType.Error:
                writer.WriteLine($"[{DateTime.Now}] [ERROR] {message}");
                Allumeria.Logger.Error(message);
                break;
        }
        writer.Flush();
    }
    
    public enum LogType { Debug, Info, Warning, Error }
}