using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace DotNetPerfPatterns.Logging;

/// <summary>
/// The interpolated string handler approach, for the comparison in pattern 1.
///
/// It removes the wasted work: with the level off the compiler skips the appends entirely, so
/// the holes are never evaluated. This implementation also flattens the message into a plain
/// string, losing the named placeholders that make logs searchable. That is a property of this
/// handler rather than of the technique, but it is the usual outcome, and Microsoft ships no
/// such handler for ILogger. Shown here because it is the first thing people reach for.
/// </summary>
internal sealed class StringLog(LogLevel threshold)
{
    // LogLevel.None is above every real level, so the comparison alone switches logging off.
    public bool IsEnabled(LogLevel level) => level >= threshold;

    // Must stay an instance method: [InterpolatedStringHandlerArgument("")] passes this logger
    // into the handler constructor, which is where the level check happens.
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "See above.")]
    public void Log(
        LogLevel level,
        [InterpolatedStringHandlerArgument("", "level")] ref LogInterpolatedStringHandler handler)
    {
        if (!handler.IsEnabled)
        {
            return;
        }

        Write(handler.ToStringAndClear());
    }

    /// <summary>Not inlined, so the JIT can't see the message goes unused.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Write(string message) => _ = message.Length;
}

/// <summary>
/// Builds the message only if the logger will take it. When shouldAppend is false the compiler
/// skips the Append calls, so nothing is formatted and no buffer is rented.
///
/// The overloads follow <see cref="DefaultInterpolatedStringHandler"/>, including the object
/// overload and scoped span parameters. Without them $"took {elapsed:N1}ms", $"{name,-20}" and
/// calls passing spans fail to compile.
/// </summary>
[InterpolatedStringHandler]
[EditorBrowsable(EditorBrowsableState.Never)]
internal ref struct LogInterpolatedStringHandler
{
    private DefaultInterpolatedStringHandler _inner;
    private readonly bool _isEnabled;

    public LogInterpolatedStringHandler(
        int literalLength,
        int formattedCount,
        StringLog log,
        LogLevel level,
        out bool shouldAppend)
    {
        _isEnabled = log.IsEnabled(level);
        shouldAppend = _isEnabled;

        // Invariant culture: log output is read by machines as often as by people, and
        // CurrentCulture would turn 53.1234 into 53,1234 on half the machines in Europe.
        _inner = _isEnabled
            ? new DefaultInterpolatedStringHandler(
                literalLength, formattedCount, CultureInfo.InvariantCulture)
            : default;
    }

    public readonly bool IsEnabled => _isEnabled;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLiteral(string value) => _inner.AppendLiteral(value);

    public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);

    public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);

    public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);

    public void AppendFormatted<T>(T value, int alignment, string? format)
        => _inner.AppendFormatted(value, alignment, format);

    public void AppendFormatted(object? value, int alignment = 0, string? format = null)
        => _inner.AppendFormatted<object?>(value, alignment, format);

    public void AppendFormatted(string? value) => _inner.AppendFormatted(value);

    public void AppendFormatted(string? value, int alignment = 0, string? format = null)
        => _inner.AppendFormatted(value, alignment, format);

    public void AppendFormatted(scoped ReadOnlySpan<char> value) => _inner.AppendFormatted(value);

    public void AppendFormatted(scoped ReadOnlySpan<char> value, int alignment = 0, string? format = null)
        => _inner.AppendFormatted(value, alignment, format);

    public void Clear() => _inner.Clear();

    public string ToStringAndClear() => _inner.ToStringAndClear();
}
