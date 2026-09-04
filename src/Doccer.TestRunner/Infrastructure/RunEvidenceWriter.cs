using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner;

internal sealed class RunEvidenceWriter : IAsyncDisposable
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly TestArtifactLayout _artifacts;
    private readonly FileStream _eventStream;
    private readonly StreamWriter _eventWriter;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private long _sequence;

    private RunEvidenceWriter(TestArtifactLayout artifacts, FileStream eventStream)
    {
        _artifacts = artifacts;
        _eventStream = eventStream;
        _eventWriter = new StreamWriter(eventStream, Utf8WithoutBom, leaveOpen: true)
        {
            AutoFlush = false,
            NewLine = "\n",
        };
    }

    public static async Task<RunEvidenceWriter> CreateAsync(
        TestArtifactLayout artifacts,
        TestRunPlanSnapshot plan)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(plan);
        await WriteTextAtomicAsync(artifacts.PlanPath, plan.ToJson()).ConfigureAwait(false);

        var eventStream = new FileStream(
            artifacts.EventsPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        return new RunEvidenceWriter(artifacts, eventStream);
    }

    public async Task AppendEventAsync(
        string runId,
        DateTimeOffset occurredAtUtc,
        TestRunEventKind kind,
        string? workItemId = null,
        TestExecutionStatus? status = null)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var itemEvent = TestRunEvent.Create(
                runId,
                checked(++_sequence),
                occurredAtUtc,
                kind,
                workItemId,
                status);
            await _eventWriter.WriteLineAsync(itemEvent.ToJsonLine()).ConfigureAwait(false);
            await _eventWriter.FlushAsync().ConfigureAwait(false);
            await _eventStream.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task MaterializeCaseAsync(
        TestCaseArtifactLayout layout,
        TestExecutionResult result,
        CapturedProcessStream standardOutput,
        CapturedProcessStream standardError)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(result);
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!TestEvidenceMaterializationPolicy.RequiresCaseDetails(result))
            {
                RemoveEmptyCase(layout);
                return;
            }

            Directory.CreateDirectory(layout.CaseDirectory);
            if (standardOutput.Content is not null)
            {
                await WriteBytesAtomicAsync(layout.StandardOutputPath, standardOutput.Content)
                    .ConfigureAwait(false);
            }

            if (standardError.Content is not null)
            {
                await WriteBytesAtomicAsync(layout.StandardErrorPath, standardError.Content)
                    .ConfigureAwait(false);
            }

            await WriteTextAtomicAsync(layout.ResultPath, result.ToJson()).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task WriteSummaryAsync(TestRunSummary summary)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await WriteTextAtomicAsync(_artifacts.SummaryPath, summary.ToJson())
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _eventWriter.FlushAsync().ConfigureAwait(false);
            _eventWriter.Dispose();
            await _eventStream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private void RemoveEmptyCase(TestCaseArtifactLayout layout)
    {
        if (Directory.Exists(layout.CaseDirectory) &&
            !Directory.EnumerateFileSystemEntries(layout.CaseDirectory).Any())
        {
            Directory.Delete(layout.CaseDirectory);
        }

        var casesRoot = Path.Combine(_artifacts.RunDirectory, "c");
        if (Directory.Exists(casesRoot) &&
            !Directory.EnumerateFileSystemEntries(casesRoot).Any())
        {
            Directory.Delete(casesRoot);
        }
    }

    private static Task WriteTextAtomicAsync(string path, string value) =>
        WriteBytesAtomicAsync(path, Utf8WithoutBom.GetBytes(value));

    private static async Task WriteBytesAtomicAsync(string path, ReadOnlyMemory<byte> content)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new IOException($"Evidence path '{path}' has no parent directory.");
        Directory.CreateDirectory(parent);
        var temporaryPath = Path.ChangeExtension(path, ".tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temporaryPath, path);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception)
            {
            }

            throw;
        }
    }
}
