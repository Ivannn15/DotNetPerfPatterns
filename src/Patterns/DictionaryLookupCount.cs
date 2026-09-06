using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace DotNetPerfPatterns.Patterns;

/// <summary>
/// Counting occurrences by key, four ways. The first three differ only in how many times they hash
/// the key to perform one update. The fourth does the same single probe as the third, but keyed by
/// the span itself, so no string is built for a token the dictionary already knows.
///
/// String hash codes are not memoized in .NET, so three dictionary operations really do mean three
/// hashes of the same key.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[GcServer(true)]
public class DictionaryLookupCount
{
    private string _payload = null!;
    private Dictionary<string, int> _counts = null!;
    private Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    /// <summary>Number of tokens in the payload.</summary>
    [Params(1000)]
    public int TokenCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(20260904);
        var tokens = new string[TokenCount];

        // Around 60 distinct keys, so 94% of the updates land on an entry that already exists.
        for (var i = 0; i < tokens.Length; i++)
        {
            tokens[i] = $"sensor-{random.Next(0, 60):D3}";
        }

        _payload = string.Join(',', tokens);

        // GetAlternateLookup needs a comparer implementing
        // IAlternateEqualityComparer<ReadOnlySpan<char>, string>. The ordinal comparers, the
        // default one and the culture-aware ones all do. A hand-written comparer will not, and
        // that is what makes the call throw.
        _counts = new Dictionary<string, int>(StringComparer.Ordinal);
        _lookup = _counts.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>
    /// ContainsKey, the indexer to read, the indexer to write. Three hashes and three probes for
    /// one update.
    /// </summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1854:Prefer the IDictionary.TryGetValue(TKey, out TValue) method",
        Justification = "This is the call CA1854 warns about. Measuring what it costs is the point.")]
    public int ContainsKeyThenIndexer()
    {
        _counts.Clear();
        var payload = _payload.AsSpan();

        foreach (var range in payload.Split(','))
        {
            var key = new string(payload[range]);
            if (_counts.ContainsKey(key))
            {
                _counts[key] = _counts[key] + 1;
            }
            else
            {
                _counts[key] = 1;
            }
        }

        return _counts.Count;
    }

    /// <summary>TryGetValue to read, the indexer to write. Two of each.</summary>
    [Benchmark(Baseline = true)]
    public int TryGetValueThenIndexer()
    {
        _counts.Clear();
        var payload = _payload.AsSpan();

        foreach (var range in payload.Split(','))
        {
            var key = new string(payload[range]);
            _counts[key] = _counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        return _counts.Count;
    }

    /// <summary>
    /// GetValueRefOrAddDefault hands back a reference into the entry, so reading and writing are
    /// the same probe. A key that was not there arrives as zero and is incremented to one.
    /// </summary>
    [Benchmark]
    public int ValueRef()
    {
        _counts.Clear();
        var payload = _payload.AsSpan();

        foreach (var range in payload.Split(','))
        {
            var key = new string(payload[range]);
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(_counts, key, out _);
            count++;
        }

        return _counts.Count;
    }

    /// <summary>
    /// The same call against the alternate lookup. Identical in every respect except the key type,
    /// which means a string is built only when a token is seen for the first time.
    /// </summary>
    [Benchmark]
    public int AlternateLookup()
    {
        _counts.Clear();
        var payload = _payload.AsSpan();

        foreach (var range in payload.Split(','))
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(_lookup, payload[range], out _);
            count++;
        }

        return _counts.Count;
    }
}
