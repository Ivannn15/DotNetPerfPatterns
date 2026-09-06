using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace DotNetPerfPatterns.Patterns;

/// <summary>
/// One pattern, five ways to reach it. The pattern is anchored, so RegexFindOptimizations reduces
/// the search to a single attempt at position zero and there is no scanning phase to measure. What
/// is left is the difference between dispatching interpreter opcodes and running straight-line code.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[GcServer(true)]
public partial class RegexConstruction
{
    private const string Pattern = @"^[a-z]{3,8}-\d{4}(?:\.\d{1,3})?$";

    private static readonly Regex Interpreted = new(Pattern);

    private static readonly Regex Compiled = new(Pattern, RegexOptions.Compiled);

    private string[] _inputs = null!;

    /// <summary>
    /// Number of strings matched per invocation. One is the case that matters for NewPerCall,
    /// where construction is not amortized over anything.
    /// </summary>
    [Params(1, 100)]
    public int InputCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(20260904);
        _inputs = new string[InputCount];

        for (var i = 0; i < _inputs.Length; i++)
        {
            var length = random.Next(3, 9);
            var name = string.Create(length, random, static (span, rng) =>
            {
                for (var j = 0; j < span.Length; j++)
                {
                    span[j] = (char)rng.Next('a', 'z' + 1);
                }
            });

            // One in five fails the pattern, so both outcomes are measured. Index zero matches, so
            // the single-input case is not the failing one.
            _inputs[i] = i % 5 == 4
                ? $"{name}_{random.Next(1000, 9999)}"
                : $"{name}-{random.Next(1000, 9999)}";
        }
    }

    /// <summary>The source generator. Builds the matcher at compile time.</summary>
    [Benchmark(Baseline = true)]
    public int Generated()
    {
        var matches = 0;
        foreach (var input in _inputs)
        {
            if (GeneratedIdentifier().IsMatch(input))
            {
                matches++;
            }
        }

        return matches;
    }

    /// <summary>A new Regex per call. The constructor always parses, whatever is in the cache.</summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "SYSLIB1045:Convert to 'GeneratedRegexAttribute'",
        Justification = "The analyzer flags construction from a constant pattern too, and this arm "
                        + "exists to measure it.")]
    public int NewPerCall()
    {
        var matches = 0;
        foreach (var input in _inputs)
        {
            if (new Regex(Pattern).IsMatch(input))
            {
                matches++;
            }
        }

        return matches;
    }

    /// <summary>
    /// The static method, which does not rebuild anything. It reads a one-element cache of the most
    /// recently used Regex and compares a four-field key, with no lock on that path. A lock is taken
    /// only when something new goes into the cache, which holds 15 entries by default.
    ///
    /// This measures the best case for that comparison: Pattern is a const, so the cache key holds
    /// the same interned reference and the string compare exits on reference equality. A pattern
    /// built at runtime is compared character by character. CurrentCulture is read on every call
    /// either way.
    /// </summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "SYSLIB1045:Convert to 'GeneratedRegexAttribute'",
        Justification = "This is the call the analyzer flags. Measuring what it costs is the point.")]
    public int StaticMethod()
    {
        var matches = 0;
        foreach (var input in _inputs)
        {
            if (Regex.IsMatch(input, Pattern))
            {
                matches++;
            }
        }

        return matches;
    }

    /// <summary>One interpreted instance, built once and reused.</summary>
    [Benchmark]
    public int CachedInstance()
    {
        var matches = 0;
        foreach (var input in _inputs)
        {
            if (Interpreted.IsMatch(input))
            {
                matches++;
            }
        }

        return matches;
    }

    /// <summary>
    /// One instance with RegexOptions.Compiled, which emits IL in the constructor rather than
    /// lazily, and silently falls back to the interpreter where dynamic code is not allowed.
    /// </summary>
    [Benchmark]
    public int CachedCompiled()
    {
        var matches = 0;
        foreach (var input in _inputs)
        {
            if (Compiled.IsMatch(input))
            {
                matches++;
            }
        }

        return matches;
    }

    [GeneratedRegex(Pattern)]
    private static partial Regex GeneratedIdentifier();
}
