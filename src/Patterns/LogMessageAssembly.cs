using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using DotNetPerfPatterns.Logging;
using Microsoft.Extensions.Logging;

namespace DotNetPerfPatterns.Patterns;

/// <summary>
/// ILogger defers formatting until it knows the message will be written. It does not defer the
/// arguments. Everything inside the call parentheses runs whether or not the level is enabled,
/// value types are boxed into the params array, and all of it is discarded.
///
/// Five call sites, with logging off and on:
///   WorkOnly        the method with no logging at all, as the baseline;
///   Eager           the usual ILogger call, arguments evaluated every time;
///   Guarded         the same call behind IsEnabled;
///   SourceGenerated the [LoggerMessage] generator;
///   Handled         an interpolated string handler, the approach Microsoft did not ship.
///
/// Server GC is declared on the job, not only in the csproj, so it shows up in the report
/// legend. It matches how the services these patterns come from are deployed.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[GcServer(true)]
public class LogMessageAssembly
{
    private ILogger _logger = null!;
    private ILoggerFactory _factory = null!;
    private StringLog _stringLog = null!;
    private Reading[] _readings = null!;

    /// <summary>Number of readings listed in a single log message.</summary>
    [Params(10, 100)]
    public int ReadingCount { get; set; }

    /// <summary>
    /// Production runs with this off. The "on" case is here to check the guard costs nothing
    /// when the message is actually written.
    /// </summary>
    [Params(false, true)]
    public bool LoggingEnabled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (_logger, _factory) = DiscardingLogger.Create(LoggingEnabled);
        _stringLog = new StringLog(LoggingEnabled ? LogLevel.Information : LogLevel.None);

        // Fixed seed. Without it string lengths drift between runs and the allocation numbers
        // stop being comparable.
        var random = new Random(20260902);

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

    [GlobalCleanup]
    public void Cleanup() => _factory.Dispose();

    /// <summary>The method with logging removed. Everything else is measured against this.</summary>
    [Benchmark(Baseline = true)]
    public int WorkOnly() => FindPeak(_readings).Id;

    // The call below is repeated verbatim on purpose: the call site is what's being compared.

    /// <summary>
    /// The usual ILogger call. The template isn't formatted when the level is off, but Describe
    /// still runs and Value still boxes.
    /// </summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1848:Use the LoggerMessage delegates",
        Justification = "This is the call CA1848 warns about. Measuring what it costs is the point.")]
    [SuppressMessage(
        "Performance",
        "CA1873:Avoid potentially expensive logging",
        Justification = "CA1873 describes this benchmark exactly: the argument is expensive and " +
                        "unnecessary when logging is off. The measurement puts a number on it.")]
    public int Eager()
    {
        var peak = FindPeak(_readings);

        _logger.LogInformation(
            "peak reading {Sensor} at {Value} out of {Count} readings: {Readings}",
            peak.Sensor,
            peak.Value,
            _readings.Length,
            Describe(_readings));

        return peak.Id;
    }

    /// <summary>Same call behind a level check. Costs an if around every log statement.</summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1848:Use the LoggerMessage delegates",
        Justification = "Deliberately the non-generated call, to isolate what the guard alone saves.")]
    public int Guarded()
    {
        var peak = FindPeak(_readings);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "peak reading {Sensor} at {Value} out of {Count} readings: {Readings}",
                peak.Sensor,
                peak.Value,
                _readings.Length,
                Describe(_readings));
        }

        return peak.Id;
    }

    /// <summary>
    /// The [LoggerMessage] generator. It takes typed parameters and emits its own IsEnabled
    /// check, but that does not remove the boxing: when a sink builds the message text, the
    /// generated code still assembles an object array. The allocation numbers match Eager
    /// exactly. What it does save is the boxing a sink pays when it reads named properties
    /// without formatting text.
    ///
    /// The guard is still needed here because Describe is evaluated by the caller either way.
    /// </summary>
    [Benchmark]
    public int SourceGenerated()
    {
        var peak = FindPeak(_readings);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.PeakReading(
                peak.Sensor,
                peak.Value,
                _readings.Length,
                Describe(_readings));
        }

        return peak.Id;
    }

    /// <summary>
    /// The interpolated string handler. This one is the only variant that avoids the object
    /// array entirely, which is the 104-byte gap against the others when logging is on.
    ///
    /// It is also the one to avoid: this implementation flattens the message into a plain
    /// string, losing the named placeholders that make logs searchable. A handler could keep
    /// them, but this one does not, and it bypasses ILogger altogether, so its timing is not
    /// strictly comparable to the rest.
    /// </summary>
    [Benchmark]
    public int Handled()
    {
        var peak = FindPeak(_readings);

        _stringLog.Log(
            LogLevel.Information,
            $"peak reading {peak.Sensor} at {peak.Value} out of {_readings.Length} readings: {Describe(_readings)}");

        return peak.Id;
    }

    private static Reading FindPeak(Reading[] readings)
    {
        var peak = readings[0];
        for (var i = 1; i < readings.Length; i++)
        {
            if (readings[i].Value > peak.Value)
            {
                peak = readings[i];
            }
        }

        return peak;
    }

    /// <summary>The expensive argument. Allocates linearly in reading count.</summary>
    private static string Describe(Reading[] readings)
        => string.Join(", ", readings.Select(c => $"{c.Sensor}={c.Value}/{c.SampleCount}"));

    private readonly record struct Reading(int Id, string Sensor, double Value, int SampleCount);
}

internal static partial class ReadingLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "peak reading {Sensor} at {Value} out of {Count} readings: {Readings}")]
    public static partial void PeakReading(
        this ILogger logger,
        string sensor,
        double value,
        int count,
        string readings);
}
