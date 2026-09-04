using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace DotNetPerfPatterns.Patterns;

/// <summary>
/// IndexOfAny over a set of more than five characters builds an ASCII bitmap on every call.
/// SearchValues builds it once. Below six values IndexOfAny has dedicated paths and there is
/// nothing to cache, which is the same threshold CA1870 uses.
///
/// Four payload sizes rather than two, because the claim under test is about a fixed setup cost.
/// If it is fixed, the gap stays flat as the scan grows. If it amortises, the gap falls.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[GcServer(true)]
public class SearchValuesLookup
{
    private const string Delimiters = ",;:|/\\ \t\r\n";

    private static readonly char[] DelimiterArray = Delimiters.ToCharArray();

    private static readonly SearchValues<char> DelimiterValues = SearchValues.Create(Delimiters);

    private string _payload = null!;

    /// <summary>Length of the string being scanned.</summary>
    [Params(128, 512, 1024, 4096)]
    public int PayloadLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // InlineArray repeats the set as literals, which is what keeps it a compile-time constant.
        // Nothing else ties the two together. Debug.Assert would not help: benchmarks run in Release.
        if (!DelimiterArray.AsSpan().SequenceEqual([',', ';', ':', '|', '/', '\\', ' ', '\t', '\r', '\n']))
        {
            throw new InvalidOperationException("InlineArray has drifted from Delimiters.");
        }

        // Fixed seed, and the only delimiter sits near the end, so the scan covers the whole string.
        var random = new Random(20260904);
        var chars = new char[PayloadLength];

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)random.Next('a', 'z' + 1);
        }

        chars[^3] = ';';
        _payload = new string(chars);
    }

    /// <summary>SearchValues built once, reused. What CA1870 asks for.</summary>
    [Benchmark(Baseline = true)]
    public int Cached() => _payload.AsSpan().IndexOfAny(DelimiterValues);

    /// <summary>A static array, so the set costs nothing to reach, but the bitmap is still rebuilt.</summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1870:Use a cached SearchValues instance",
        Justification = "This is the call CA1870 warns about. Measuring what it costs is the point.")]
    public int CachedArray() => _payload.AsSpan().IndexOfAny(DelimiterArray);

    /// <summary>
    /// The set written out at the call site, which is how it usually gets typed. Constant elements
    /// bound to a ReadOnlySpan parameter compile to a metadata blob, so this does not allocate.
    /// </summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1870:Use a cached SearchValues instance",
        Justification = "Same call, written the way it appears in most code.")]
    public int InlineArray()
        => _payload.AsSpan().IndexOfAny([',', ';', ':', '|', '/', '\\', ' ', '\t', '\r', '\n']);
}
