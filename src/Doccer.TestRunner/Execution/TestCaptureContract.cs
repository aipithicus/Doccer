using System;

namespace Doccer.TestRunner;

internal static class TestCaptureContract
{
    public const long RetainedHeadBytesPerTruncatedStream = 64 * 1024;
    public const long RetainedTailBytesPerTruncatedStream = 192 * 1024;
    public const long MaximumRetainedBytesPerStream =
        RetainedHeadBytesPerTruncatedStream + RetainedTailBytesPerTruncatedStream;
    public const int MaximumArtifactFileCount = 8;
    public const long MaximumArtifactFileBytes = 4 * 1024 * 1024;
    public const long MaximumArtifactTotalBytes = 8 * 1024 * 1024;
}

internal static class TestRunRetentionContract
{
    public const int MaximumFinalizedRunCount = 16;
}

internal sealed record TestStreamCapture(
    long ObservedBytes,
    long RetainedBytes,
    bool Truncated)
{
    public static TestStreamCapture Create(long observedBytes)
    {
        if (observedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedBytes),
                "An empty stream is represented by an absent capture record.");
        }

        var retainedBytes = Math.Min(
            observedBytes,
            TestCaptureContract.MaximumRetainedBytesPerStream);
        return new TestStreamCapture(
            observedBytes,
            retainedBytes,
            observedBytes > retainedBytes);
    }

    internal void Validate()
    {
        if (ObservedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ObservedBytes));
        }

        var expectedRetained = Math.Min(
            ObservedBytes,
            TestCaptureContract.MaximumRetainedBytesPerStream);
        if (RetainedBytes != expectedRetained || Truncated != (ObservedBytes > RetainedBytes))
        {
            throw new ArgumentException("Stream capture metadata does not match the retention policy.");
        }
    }
}

internal sealed record TestArtifactCapture(
    int FileCount,
    long TotalBytes,
    long LargestFileBytes)
{
    public static TestArtifactCapture Create(
        int fileCount,
        long totalBytes,
        long largestFileBytes)
    {
        var capture = new TestArtifactCapture(fileCount, totalBytes, largestFileBytes);
        capture.Validate();
        return capture;
    }

    internal void Validate()
    {
        if (FileCount < 1 || FileCount > TestCaptureContract.MaximumArtifactFileCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FileCount),
                $"Artifact file count must be from 1 through " +
                $"{TestCaptureContract.MaximumArtifactFileCount}.");
        }

        if (TotalBytes < 0 || TotalBytes > TestCaptureContract.MaximumArtifactTotalBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TotalBytes),
                $"Artifact bytes must be from 0 through " +
                $"{TestCaptureContract.MaximumArtifactTotalBytes}.");
        }

        if (LargestFileBytes < 0 ||
            LargestFileBytes > TestCaptureContract.MaximumArtifactFileBytes ||
            LargestFileBytes > TotalBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LargestFileBytes),
                $"The largest artifact must be nonnegative, no larger than the collection, and no " +
                $"larger than {TestCaptureContract.MaximumArtifactFileBytes} bytes.");
        }
    }
}

internal static class TestEvidenceMaterializationPolicy
{
    public static bool RequiresCaseDetails(TestExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Validate();
        return result.Status != TestExecutionStatus.Passed ||
            result.StandardOutput is not null ||
            result.StandardError is not null ||
            result.Artifacts is not null;
    }
}
