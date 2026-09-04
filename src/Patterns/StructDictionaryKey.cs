using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace DotNetPerfPatterns.Patterns;

/// <summary>
/// A struct used as a dictionary key without IEquatable&lt;T&gt; resolves to ObjectEqualityComparer,
/// so every lookup goes through ValueType.GetHashCode and ValueType.Equals. Both walk the fields by
/// reflection and box. Nothing in the source says so. The code compiles and behaves correctly.
///
/// Five key types with the same two fields, to separate three things that get conflated: what the
/// reflection path costs, how much of it CA1815 actually removes, and why a record struct comes out
/// ahead of an IEquatable&lt;T&gt; written by hand.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[GcServer(true)]
public class StructDictionaryKey
{
    private Dictionary<PlainKey, int> _plain = null!;
    private Dictionary<AnalyzerFixKey, int> _analyzerFix = null!;
    private Dictionary<EquatableKey, int> _equatable = null!;
    private Dictionary<CheapHashKey, int> _cheapHash = null!;
    private Dictionary<RecordKey, int> _record = null!;

    private PlainKey[] _plainLookups = null!;
    private AnalyzerFixKey[] _analyzerFixLookups = null!;
    private EquatableKey[] _equatableLookups = null!;
    private CheapHashKey[] _cheapHashLookups = null!;
    private RecordKey[] _recordLookups = null!;

    /// <summary>Number of entries in the dictionary.</summary>
    [Params(100, 1000)]
    public int EntryCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _plain = new Dictionary<PlainKey, int>(EntryCount);
        _analyzerFix = new Dictionary<AnalyzerFixKey, int>(EntryCount);
        _equatable = new Dictionary<EquatableKey, int>(EntryCount);
        _cheapHash = new Dictionary<CheapHashKey, int>(EntryCount);
        _record = new Dictionary<RecordKey, int>(EntryCount);

        _plainLookups = new PlainKey[EntryCount];
        _analyzerFixLookups = new AnalyzerFixKey[EntryCount];
        _equatableLookups = new EquatableKey[EntryCount];
        _cheapHashLookups = new CheapHashKey[EntryCount];
        _recordLookups = new RecordKey[EntryCount];

        for (var i = 0; i < EntryCount; i++)
        {
            var stored = $"sensor-{i:D4}";

            _plain[new PlainKey(stored, i)] = i;
            _analyzerFix[new AnalyzerFixKey(stored, i)] = i;
            _equatable[new EquatableKey(stored, i)] = i;
            _cheapHash[new CheapHashKey(stored, i)] = i;
            _record[new RecordKey(stored, i)] = i;

            // A separate string instance with the same content. A key built from parsed input is
            // never the same reference as the one already in the table, and reference equality would
            // short-circuit the string comparison in every variant below.
            var probe = new string(stored.AsSpan());

            _plainLookups[i] = new PlainKey(probe, i);
            _analyzerFixLookups[i] = new AnalyzerFixKey(probe, i);
            _equatableLookups[i] = new EquatableKey(probe, i);
            _cheapHashLookups[i] = new CheapHashKey(probe, i);
            _recordLookups[i] = new RecordKey(probe, i);
        }

        // Every arm is measured on hits only, and a miss is much cheaper on the reflection path.
        // Verify that here rather than claiming it in the README.
        if (Plain() != EntryCount || Equatable() != EntryCount || Record() != EntryCount
            || AnalyzerFix() != EntryCount || CheapHash() != EntryCount)
        {
            throw new InvalidOperationException("A lookup missed. The arms are no longer comparable.");
        }
    }

    /// <summary>Hand-written IEquatable with HashCode.Combine, which is the usual fix.</summary>
    [Benchmark(Baseline = true)]
    public int Equatable()
    {
        var found = 0;
        foreach (var key in _equatableLookups)
        {
            if (_equatable.TryGetValue(key, out _))
            {
                found++;
            }
        }

        return found;
    }

    /// <summary>No IEquatable and nothing overridden, so both halves of a lookup use reflection.</summary>
    [Benchmark]
    public int Plain()
    {
        var found = 0;
        foreach (var key in _plainLookups)
        {
            if (_plain.TryGetValue(key, out _))
            {
                found++;
            }
        }

        return found;
    }

    /// <summary>
    /// What the CA1815 analyzer checks for: an Equals(object) override and the equality operators.
    /// GetHashCode comes with it because CS0659 requires it. None of that is IEquatable&lt;T&gt;, which
    /// is what the comparer looks for, so the argument is still boxed on every call.
    /// </summary>
    [Benchmark]
    public int AnalyzerFix()
    {
        var found = 0;
        foreach (var key in _analyzerFixLookups)
        {
            if (_analyzerFix.TryGetValue(key, out _))
            {
                found++;
            }
        }

        return found;
    }

    /// <summary>
    /// The same IEquatable as the baseline, with GetHashCode replaced by the multiply-and-add the
    /// compiler emits for a record. Isolates the hash function from the equality check.
    /// </summary>
    [Benchmark]
    public int CheapHash()
    {
        var found = 0;
        foreach (var key in _cheapHashLookups)
        {
            if (_cheapHash.TryGetValue(key, out _))
            {
                found++;
            }
        }

        return found;
    }

    /// <summary>A record struct, where both members are generated.</summary>
    [Benchmark]
    public int Record()
    {
        var found = 0;
        foreach (var key in _recordLookups)
        {
            if (_record.TryGetValue(key, out _))
            {
                found++;
            }
        }

        return found;
    }

    private readonly struct PlainKey(string sensor, int channel)
    {
        public string Sensor { get; } = sensor;

        public int Channel { get; } = channel;
    }

    private readonly struct AnalyzerFixKey(string sensor, int channel)
    {
        public string Sensor { get; } = sensor;

        public int Channel { get; } = channel;

        public static bool operator ==(AnalyzerFixKey left, AnalyzerFixKey right) => left.Equals(right);

        public static bool operator !=(AnalyzerFixKey left, AnalyzerFixKey right) => !left.Equals(right);

        public override bool Equals(object? obj)
            => obj is AnalyzerFixKey other && Channel == other.Channel && Sensor == other.Sensor;

        public override int GetHashCode() => HashCode.Combine(Sensor, Channel);
    }

    private readonly struct EquatableKey(string sensor, int channel) : IEquatable<EquatableKey>
    {
        public string Sensor { get; } = sensor;

        public int Channel { get; } = channel;

        public static bool operator ==(EquatableKey left, EquatableKey right) => left.Equals(right);

        public static bool operator !=(EquatableKey left, EquatableKey right) => !left.Equals(right);

        public bool Equals(EquatableKey other)
            => Channel == other.Channel && Sensor == other.Sensor;

        public override bool Equals(object? obj) => obj is EquatableKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Sensor, Channel);
    }

    private readonly struct CheapHashKey(string sensor, int channel) : IEquatable<CheapHashKey>
    {
        public string Sensor { get; } = sensor;

        public int Channel { get; } = channel;

        public static bool operator ==(CheapHashKey left, CheapHashKey right) => left.Equals(right);

        public static bool operator !=(CheapHashKey left, CheapHashKey right) => !left.Equals(right);

        public bool Equals(CheapHashKey other)
            => Channel == other.Channel && Sensor == other.Sensor;

        public override bool Equals(object? obj) => obj is CheapHashKey other && Equals(other);

        // What the compiler generates for a record struct, field by field.
        public override int GetHashCode()
            => (EqualityComparer<string>.Default.GetHashCode(Sensor) * -1521134295)
                + EqualityComparer<int>.Default.GetHashCode(Channel);
    }

    private readonly record struct RecordKey(string Sensor, int Channel);
}
