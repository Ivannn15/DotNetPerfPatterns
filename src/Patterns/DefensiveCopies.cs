using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace DotNetPerfPatterns.Patterns;

/// <summary>
/// Passing a struct by 'in' avoids copying it at the call. The usual warning is that it does not
/// avoid copying it inside the callee: every member the compiler cannot prove is read-only forces a
/// copy of the whole struct first, in case the member writes to it.
///
/// That is still what the compiler emits. Whether it survives to machine code is a separate
/// question, and on .NET 10 it usually does not, so the arms here are split into two groups.
///
/// InlinedMember: three computed properties small enough for the JIT to inline.
/// SeparateMember: one cheap property the JIT is told not to inline, which is what a member too
/// large to inline would look like.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[GcServer(true)]
public class DefensiveCopies
{
    private MutableWindow[] _mutable = null!;
    private ReadonlyMemberWindow[] _readonlyMembers = null!;
    private ReadonlyWindow[] _readonlyStruct = null!;

    /// <summary>Number of aggregates read per invocation.</summary>
    [Params(1000)]
    public int WindowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(20260904);

        _mutable = new MutableWindow[WindowCount];
        _readonlyMembers = new ReadonlyMemberWindow[WindowCount];
        _readonlyStruct = new ReadonlyWindow[WindowCount];

        for (var i = 0; i < WindowCount; i++)
        {
            var samples = new double[16];
            for (var j = 0; j < samples.Length; j++)
            {
                samples[j] = random.NextDouble() * 100;
            }

            _mutable[i] = MutableWindow.From(samples);
            _readonlyMembers[i] = ReadonlyMemberWindow.From(samples);
            _readonlyStruct[i] = ReadonlyWindow.From(samples);
        }
    }

    /// <summary>A readonly struct by 'in'. Nothing can write to it, so no copy is ever needed.</summary>
    [Benchmark(Baseline = true)]
    public double ReadonlyStructIn()
    {
        var total = 0d;
        for (var i = 0; i < _readonlyStruct.Length; i++)
        {
            ref readonly var window = ref _readonlyStruct[i];
            total += window.Mean + window.Range + window.StandardDeviation;
        }

        return total;
    }

    /// <summary>
    /// The same fields in a struct that is not readonly, by 'in'. The compiler emits a copy before
    /// each of the three property reads.
    /// </summary>
    [Benchmark]
    public double MutableIn()
    {
        var total = 0d;
        for (var i = 0; i < _mutable.Length; i++)
        {
            ref readonly var window = ref _mutable[i];
            total += window.Mean + window.Range + window.StandardDeviation;
        }

        return total;
    }

    /// <summary>The struct still not readonly, but with the three properties marked readonly.</summary>
    [Benchmark]
    public double ReadonlyMembersIn()
    {
        var total = 0d;
        for (var i = 0; i < _readonlyMembers.Length; i++)
        {
            ref readonly var window = ref _readonlyMembers[i];
            total += window.Mean + window.Range + window.StandardDeviation;
        }

        return total;
    }

    /// <summary>The mutable struct by value. One copy on the way in, none after it.</summary>
    [Benchmark]
    public double MutableByValue()
    {
        var total = 0d;
        for (var i = 0; i < _mutable.Length; i++)
        {
            var window = _mutable[i];
            total += window.Mean + window.Range + window.StandardDeviation;
        }

        return total;
    }

    /// <summary>
    /// A cheap member the JIT is not allowed to inline, on a readonly struct, reached through an
    /// 'in' parameter. Nothing here needs copying.
    /// </summary>
    [Benchmark]
    public double ReadonlyStructSeparateMember()
    {
        var total = 0d;
        for (var i = 0; i < _readonlyStruct.Length; i++)
        {
            total += Midpoint(in _readonlyStruct[i]);
        }

        return total;
    }

    /// <summary>
    /// The same member on a struct that is not readonly, reached the same way. The compiler cannot
    /// prove Midpoint leaves the struct alone, so it copies all 56 bytes before the call.
    /// </summary>
    [Benchmark]
    public double MutableSeparateMember()
    {
        var total = 0d;
        for (var i = 0; i < _mutable.Length; i++)
        {
            total += Midpoint(in _mutable[i]);
        }

        return total;
    }

    /// <summary>
    /// The copy has to happen in a method that takes 'in'. Reading _mutable[i].Midpoint() directly
    /// needs no copy at all, because an array element is a writable location.
    /// </summary>
    private static double Midpoint(in MutableWindow window) => window.Midpoint();

    private static double Midpoint(in ReadonlyWindow window) => window.Midpoint();

    [SuppressMessage(
        "Performance",
        "CA1815:Override equals and operator equals on value types",
        Justification = "Never compared. Equality members would change what is measured.")]
    private struct MutableWindow
    {
        private double _sum;
        private double _sumSquares;
        private double _min;
        private double _max;
        private double _first;
        private double _last;
        private int _count;

        public double Mean => _count == 0 ? 0 : _sum / _count;

        public double Range => _max - _min;

        public double StandardDeviation
        {
            get
            {
                if (_count == 0)
                {
                    return 0;
                }

                var mean = _sum / _count;
                return Math.Sqrt(Math.Max(0, (_sumSquares / _count) - (mean * mean)));
            }
        }

        /// <summary>Cheap on purpose, so the comparison is dominated by the copy and not by work.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public double Midpoint() => (_min + _max) / 2;

        public static MutableWindow From(double[] samples)
        {
            var window = default(MutableWindow);
            window._min = double.MaxValue;
            window._max = double.MinValue;
            window._first = samples[0];

            foreach (var sample in samples)
            {
                window._sum += sample;
                window._sumSquares += sample * sample;
                window._min = Math.Min(window._min, sample);
                window._max = Math.Max(window._max, sample);
                window._last = sample;
                window._count++;
            }

            return window;
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1815:Override equals and operator equals on value types",
        Justification = "Never compared. Equality members would change what is measured.")]
    private struct ReadonlyMemberWindow
    {
        private double _sum;
        private double _sumSquares;
        private double _min;
        private double _max;
        private double _first;
        private double _last;
        private int _count;

        public readonly double Mean => _count == 0 ? 0 : _sum / _count;

        public readonly double Range => _max - _min;

        public readonly double StandardDeviation
        {
            get
            {
                if (_count == 0)
                {
                    return 0;
                }

                var mean = _sum / _count;
                return Math.Sqrt(Math.Max(0, (_sumSquares / _count) - (mean * mean)));
            }
        }

        public static ReadonlyMemberWindow From(double[] samples)
        {
            var window = default(ReadonlyMemberWindow);
            window._min = double.MaxValue;
            window._max = double.MinValue;
            window._first = samples[0];

            foreach (var sample in samples)
            {
                window._sum += sample;
                window._sumSquares += sample * sample;
                window._min = Math.Min(window._min, sample);
                window._max = Math.Max(window._max, sample);
                window._last = sample;
                window._count++;
            }

            return window;
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1815:Override equals and operator equals on value types",
        Justification = "Never compared. Equality members would change what is measured.")]
    private readonly struct ReadonlyWindow(
        double sum, double sumSquares, double min, double max, double first, double last, int count)
    {
        private readonly double _sum = sum;
        private readonly double _sumSquares = sumSquares;
        private readonly double _min = min;
        private readonly double _max = max;
        private readonly double _first = first;
        private readonly double _last = last;
        private readonly int _count = count;

        public double Mean => _count == 0 ? 0 : _sum / _count;

        public double Range => _max - _min;

        public double StandardDeviation
        {
            get
            {
                if (_count == 0)
                {
                    return 0;
                }

                var mean = _sum / _count;
                return Math.Sqrt(Math.Max(0, (_sumSquares / _count) - (mean * mean)));
            }
        }

        /// <summary>Cheap on purpose, so the comparison is dominated by the copy and not by work.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public double Midpoint() => (_min + _max) / 2;

        public static ReadonlyWindow From(double[] samples)
        {
            double sum = 0, sumSquares = 0, min = double.MaxValue, max = double.MinValue;
            var count = 0;

            foreach (var sample in samples)
            {
                sum += sample;
                sumSquares += sample * sample;
                min = Math.Min(min, sample);
                max = Math.Max(max, sample);
                count++;
            }

            return new ReadonlyWindow(sum, sumSquares, min, max, samples[0], samples[^1], count);
        }
    }
}
