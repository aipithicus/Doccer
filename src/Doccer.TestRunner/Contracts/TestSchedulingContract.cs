using System;

namespace Doccer.TestRunner;

internal static class TestSchedulingContract
{
    public const int MaximumParallelism = 256;

    public static int DefaultMaxParallel =>
        Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
}
