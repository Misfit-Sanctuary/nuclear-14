// #Misfits Removed - Sandbox violation: content assemblies cannot access System.IO.* or
// Serilog.Events.* types. ILogHandler.Log() takes LogEvent (Serilog) in its signature,
// and file I/O (FileStream, Directory, Path) is also blocked by the Robust type checker.
// Client-side file logging is now handled at the engine layer in:
//   RobustToolbox/Robust.Client/GameController/GameController.cs (StartupSystemContinue)
// which uses the existing internal FileLogHandler directly.

/*
using System.IO;
using Robust.Shared.Log;
using Serilog.Events;

namespace Content.Client._Misfits.Logging;

public sealed class ClientFileLogHandler : ILogHandler, IDisposable
{
    private readonly TextWriter _writer;

    public string LogFilePath { get; }

    public ClientFileLogHandler(string userDataDirectory)
    {
        var logsDir = Path.Combine(userDataDirectory, "logs");
        Directory.CreateDirectory(logsDir);

        LogFilePath = Path.Combine(logsDir, $"client_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

        _writer = TextWriter.Synchronized(
            new StreamWriter(
                new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete),
                System.Text.Encoding.UTF8));
    }

    public void Log(string sawmillName, LogEvent message)
    {
        var levelName = LogMessage.LogLevelToName(message.Level.ToRobust());
        _writer.WriteLine("{0:o} [{1}] {2}: {3}",
            DateTime.Now, levelName, sawmillName, message.RenderMessage());

        if (message.Exception != null)
            _writer.WriteLine(message.Exception.ToString());

        _writer.Flush();
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
*/
