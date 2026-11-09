using System.ComponentModel;
using System.Diagnostics;
using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.SystemControl;

/// <summary>
/// Запускает программу. Успехом считается то, что Windows запустила процесс.
/// Что программа делает дальше, приложению неизвестно, и оно этого не утверждает.
/// </summary>
public sealed class LaunchProgramExecutor : ActionExecutor<LaunchProgramPayload>
{
    public override ActionType ActionType => ActionType.LaunchProgram;

    protected override Task<ExecutionOutcome> ExecuteAsync(
        LaunchProgramPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo(payload.ProgramPath) { UseShellExecute = true };

            if (!string.IsNullOrWhiteSpace(payload.Arguments))
            {
                startInfo.Arguments = payload.Arguments;
            }

            // Рабочая папка нужна только для программы с полным путём; для notepad.exe и подобных путь ищет система.
            if (Path.IsPathFullyQualified(payload.ProgramPath))
            {
                if (!File.Exists(payload.ProgramPath))
                {
                    return Task.FromResult(ExecutionOutcome.Failed($"Файл не найден: {payload.ProgramPath}"));
                }

                startInfo.WorkingDirectory = Path.GetDirectoryName(payload.ProgramPath) ?? string.Empty;
            }

            using var process = Process.Start(startInfo);

            return Task.FromResult(ExecutionOutcome.Success());
        }
        catch (Win32Exception ex)
        {
            return Task.FromResult(ExecutionOutcome.Failed($"Не удалось запустить программу: {ex.Message}"));
        }
    }
}
