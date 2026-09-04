using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Doccer.TestRunner;

internal sealed record CapturedProcessStream(
    TestStreamCapture? Evidence,
    byte[]? Content);

internal static class BoundedProcessStreamCapture
{
    private static readonly byte[] TruncationMarker = Encoding.UTF8.GetBytes(
        "\n--- doccer: stream truncated; see result metadata ---\n");

    public static async Task<CapturedProcessStream> CaptureAsync(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var capture = new CaptureBuffer();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var count = await source.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (count == 0)
            {
                return capture.Complete();
            }

            capture.Append(buffer.AsSpan(0, count));
        }
    }

    private sealed class CaptureBuffer
    {
        private readonly byte[] _head =
            new byte[TestCaptureContract.RetainedHeadBytesPerTruncatedStream];
        private readonly byte[] _tail =
            new byte[TestCaptureContract.RetainedTailBytesPerTruncatedStream];
        private int _headCount;
        private int _tailCount;
        private int _tailStart;
        private long _observedBytes;

        public void Append(ReadOnlySpan<byte> value)
        {
            _observedBytes = checked(_observedBytes + value.Length);

            if (_headCount < _head.Length)
            {
                var headCount = Math.Min(value.Length, _head.Length - _headCount);
                value[..headCount].CopyTo(_head.AsSpan(_headCount));
                _headCount += headCount;
                value = value[headCount..];
            }

            AppendTail(value);
        }

        public CapturedProcessStream Complete()
        {
            if (_observedBytes == 0)
            {
                return new CapturedProcessStream(Evidence: null, Content: null);
            }

            var evidence = TestStreamCapture.Create(_observedBytes);
            var markerLength = evidence.Truncated ? TruncationMarker.Length : 0;
            var content = new byte[checked((int)evidence.RetainedBytes + markerLength)];
            _head.AsSpan(0, _headCount).CopyTo(content);
            var offset = _headCount;
            if (evidence.Truncated)
            {
                TruncationMarker.CopyTo(content, offset);
                offset += TruncationMarker.Length;
            }

            CopyTailTo(content.AsSpan(offset));
            return new CapturedProcessStream(evidence, content);
        }

        private void AppendTail(ReadOnlySpan<byte> value)
        {
            if (value.Length == 0)
            {
                return;
            }

            if (value.Length >= _tail.Length)
            {
                value[^_tail.Length..].CopyTo(_tail);
                _tailCount = _tail.Length;
                _tailStart = 0;
                return;
            }

            if (_tailCount < _tail.Length)
            {
                var initialCount = Math.Min(value.Length, _tail.Length - _tailCount);
                value[..initialCount].CopyTo(_tail.AsSpan(_tailCount));
                _tailCount += initialCount;
                value = value[initialCount..];
                if (value.Length == 0)
                {
                    return;
                }
            }

            var firstCount = Math.Min(value.Length, _tail.Length - _tailStart);
            value[..firstCount].CopyTo(_tail.AsSpan(_tailStart));
            value[firstCount..].CopyTo(_tail);
            _tailStart = (_tailStart + value.Length) % _tail.Length;
        }

        private void CopyTailTo(Span<byte> destination)
        {
            if (_tailCount < _tail.Length)
            {
                _tail.AsSpan(0, _tailCount).CopyTo(destination);
                return;
            }

            var firstCount = _tail.Length - _tailStart;
            _tail.AsSpan(_tailStart, firstCount).CopyTo(destination);
            _tail.AsSpan(0, _tailStart).CopyTo(destination[firstCount..]);
        }
    }
}
