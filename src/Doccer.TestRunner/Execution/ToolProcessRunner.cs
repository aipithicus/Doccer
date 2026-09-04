using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner;

internal sealed record ToolProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout);

internal sealed record ToolProcessResult(
    int ExitCode,
    CapturedProcessStream StandardOutput,
    CapturedProcessStream StandardError);

internal interface IToolProcessRunner
{
    Task<ToolProcessResult> RunAsync(
        ToolProcessRequest request,
        CancellationToken cancellationToken);
}

internal sealed class BoundedToolProcessRunner : IToolProcessRunner
{
    private static readonly TimeSpan TerminationGrace = TimeSpan.FromSeconds(10);

    public async Task<ToolProcessResult> RunAsync(
        ToolProcessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "A tool-process timeout must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in request.Environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The tool process did not start.");
        }

        var standardOutputTask = BoundedProcessStreamCapture.CaptureAsync(
            process.StandardOutput.BaseStream);
        var standardErrorTask = BoundedProcessStreamCapture.CaptureAsync(
            process.StandardError.BaseStream);
        var exitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(request.Timeout);
        var cancellationSignal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            () => cancellationSignal.TrySetResult(true));

        var completedTask = await Task.WhenAny(
                exitTask,
                timeoutTask,
                cancellationSignal.Task)
            .ConfigureAwait(false);
        if (!exitTask.IsCompleted)
        {
            var terminationFailure = await TerminateAsync(process, exitTask).ConfigureAwait(false);
            if (terminationFailure is not null)
            {
                throw new InvalidOperationException(
                    "The tool process could not be terminated.",
                    terminationFailure);
            }

            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            if (ReferenceEquals(completedTask, cancellationSignal.Task))
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new TimeoutException(
                $"The tool process exceeded its {request.Timeout.TotalSeconds:0.###}-second timeout.");
        }

        await exitTask.ConfigureAwait(false);
        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        return new ToolProcessResult(
            process.ExitCode,
            await standardOutputTask.ConfigureAwait(false),
            await standardErrorTask.ConfigureAwait(false));
    }

    private static async Task<Exception?> TerminateAsync(Process process, Task exitTask)
    {
        Exception? killFailure = null;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
        catch (Exception exception)
        {
            killFailure = exception;
        }

        try
        {
            await exitTask.WaitAsync(TerminationGrace).ConfigureAwait(false);
            return process.HasExited ? null : killFailure;
        }
        catch (Exception exception)
        {
            try
            {
                process.StandardOutput.Close();
                process.StandardError.Close();
            }
            catch (Exception)
            {
            }

            return killFailure ?? new TimeoutException(
                $"The tool process did not terminate within " +
                $"{TerminationGrace.TotalSeconds:0} seconds.",
                exception);
        }
    }
}
