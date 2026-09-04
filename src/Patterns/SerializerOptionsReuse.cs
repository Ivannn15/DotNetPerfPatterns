using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace DotNetPerfPatterns.Patterns;

/// <summary>
/// The usual advice is to cache JsonSerializerOptions because each instance builds its own metadata.
/// Since .NET 8 that is no longer how it works: options are matched by structural equality against a
/// process-wide table of 64 weakly-referenced caching contexts, so two instances configured the same
/// way share one set of JsonTypeInfo. A per-call instance costs an allocation and a structural
/// comparison, not a rebuild.
///
/// The advice still holds, but for a different reason. Converters are compared by reference. A fresh
/// converter instance makes the options structurally unique, nothing matches, and the metadata is
/// rebuilt from scratch on every call. In .NET 10 that rebuild also emits IL (dotnet/runtime#122548).
///
/// Five call sites, baseline is the cached instance:
///   Cached                 one static instance, reused;
///   PerCall                a new instance per call, no converter;
///   Copied                 new JsonSerializerOptions(existing), which still finds the shared cache;
///   PerCallSharedConverter a new instance per call holding a shared converter, still matches;
///   PerCallNewConverter    a new instance per call with a new converter, matches nothing.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[GcServer(true)]
public class SerializerOptionsReuse
{
    private static readonly RoundingConverter SharedConverter = new();

    private static readonly JsonSerializerOptions CachedOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private Reading[] _readings = null!;

    /// <summary>Number of readings in the payload being serialized.</summary>
    [Params(1, 50)]
    public int ReadingCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Fixed seed. Without it the payload size drifts between runs and the allocation numbers
        // stop being comparable.
        var random = new Random(20260904);

        _readings = new Reading[ReadingCount];
        for (var i = 0; i < _readings.Length; i++)
        {
            _readings[i] = new Reading(
                Id: i,
                Sensor: $"sensor-{i:D4}",
                Value: Math.Round(random.NextDouble() * 100, 4),
                SampleCount: random.Next(1, 1000));
        }
    }

    /// <summary>One instance, built once, reused. What the guidance asks for.</summary>
    [Benchmark(Baseline = true)]
    public int Cached() => JsonSerializer.Serialize(_readings, CachedOptions).Length;

    /// <summary>
    /// A new instance per call. Costs the allocation and the structural comparison, then lands on the
    /// same shared metadata as everyone else. Flat overhead, so the multiplier shrinks as the payload
    /// grows.
    /// </summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1869:Cache and reuse JsonSerializerOptions instances",
        Justification = "This is the call CA1869 warns about. Measuring what it costs is the point.")]
    public int PerCall()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        return JsonSerializer.Serialize(_readings, options).Length;
    }

    /// <summary>
    /// Copying an existing instance to tweak a setting. The copy starts without a cache reference but
    /// resolves to the same shared context on first use, so the extra cost is copying the fields, not
    /// rebuilding metadata.
    /// </summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1869:Cache and reuse JsonSerializerOptions instances",
        Justification = "The copy constructor is the version people assume is free.")]
    public int Copied()
    {
        var options = new JsonSerializerOptions(CachedOptions);

        return JsonSerializer.Serialize(_readings, options).Length;
    }

    /// <summary>
    /// Per-call options holding a converter that is itself shared. Structural equality still matches,
    /// so this behaves like PerCall despite the custom converter.
    /// </summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1869:Cache and reuse JsonSerializerOptions instances",
        Justification = "Deliberately per-call, to isolate the converter's identity as the variable.")]
    public int PerCallSharedConverter()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { SharedConverter }
        };

        return JsonSerializer.Serialize(_readings, options).Length;
    }

    /// <summary>
    /// The same code with a fresh converter. Converters are compared by reference, so these options
    /// match nothing in the cache table and the metadata is rebuilt every call. One word of difference
    /// from the benchmark above.
    /// </summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1869:Cache and reuse JsonSerializerOptions instances",
        Justification = "The expensive case the pattern is about.")]
    public int PerCallNewConverter()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new RoundingConverter() }
        };

        return JsonSerializer.Serialize(_readings, options).Length;
    }

    private readonly record struct Reading(int Id, string Sensor, double Value, int SampleCount);
}

/// <summary>Stands in for the kind of small custom converter services actually ship.</summary>
internal sealed class RoundingConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDouble();

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        => writer.WriteNumberValue(Math.Round(value, 2));
}
