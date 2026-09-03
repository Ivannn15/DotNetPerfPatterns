# .NET Performance Patterns

[![CI](https://github.com/Ivannn15/DotNetPerfPatterns/actions/workflows/ci.yml/badge.svg)](https://github.com/Ivannn15/DotNetPerfPatterns/actions/workflows/ci.yml)

BenchmarkDotNet benchmarks for performance patterns I keep hitting in production ASP.NET Core
services. Each one has the version people normally write next to the alternatives, measured
against a baseline of the same method with the pattern removed.

All input is synthetic and generated in `GlobalSetup` with a fixed seed.

## Running

```bash
cd src
dotnet run -c Release
```

Needs the .NET 10 SDK. One pattern at a time:

```bash
dotnet run -c Release -- --filter '*LogMessageAssembly*'
```

Reports land in `src/BenchmarkDotNet.Artifacts/results/`. The copies under `results/` are the
same files, kept in the repository so the numbers quoted below can be checked against a full
report rather than an excerpt.

## 1. ILogger defers formatting, not arguments

`ILogger` does not format the message template until it knows the message will be written. It
does not defer the arguments. Everything inside the call parentheses runs whether or not the
level is enabled, value types are boxed into the `params object?[]`, and all of it is thrown
away when the level is off.

Five call sites, measured with logging off and on:

* **WorkOnly** is the method with the logging removed, used as the baseline.
* **Eager** is the ordinary `logger.LogInformation(template, args)`.
* **Guarded** is the same call behind `logger.IsEnabled(...)`.
* **SourceGenerated** uses the `[LoggerMessage]` source generator, which takes typed
  parameters and emits its own level check.
* **Handled** uses an [interpolated string handler](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/interpolated-string-handler).
  Cheapest of the five, and the one you should not reach for. See below.

### Logging off, which is how production usually runs

| Method | Readings | Mean | Ratio | Allocated |
|---|---|---|---|---|
| WorkOnly | 100 | 40.8 ns | 1.00 | – |
| **Eager** | 100 | **9,613.7 ns** | **235.76** | **13,536 B** |
| Guarded | 100 | 42.5 ns | 1.04 | – |
| SourceGenerated | 100 | 44.7 ns | 1.10 | – |
| Handled | 100 | 43.6 ns | 1.07 | – |

The ordinary call costs 236 times the method it is logging about, and allocates 13.5 KB that
nothing reads. A level check brings it back to within a few percent of doing no logging at all.

Note that with logging off, `Guarded` and `SourceGenerated` are the same program: `FindPeak`
plus one `IsEnabled` that returns false. Two rows, one measurement, taken twice.

### Logging on

| Method | Readings | Mean | Allocated |
|---|---|---|---|
| Eager | 100 | 10,760.4 ns | 19,232 B |
| Guarded | 100 | 10,166.7 ns | 19,232 B |
| SourceGenerated | 100 | 10,164.2 ns | 19,232 B |
| Handled | 100 | 10,073.0 ns | **19,128 B** |

All four are within noise of each other. Nothing here is free any more, and that is the point:
when the message is written, the guard buys nothing and costs nothing.

### Where the bytes go

Compare the two `Eager` rows: 19,232 B with logging on, 13,536 B with it off.

* **5,696 B is the message string itself.** 2,836 characters at two bytes each, exactly. This is
  what deferred formatting saves.
* **13,536 B is spent either way.** Of that, 13,432 B is `Describe()` building its string, and
  104 B is the `object[4]` plus boxing `Value` and `Count`.

So `ILogger` skips 30% of the allocation when the level is off, and the caller pays the rest.
Only a level check removes it.

The split is worth reading carefully: at 100 readings, boxing is 24 bytes out of 13,536. The
expensive argument is what matters, not the boxing.

### About `[LoggerMessage]`

It takes typed parameters and generates its own level check, and it is the right default for new
code. It does not remove the boxing here: `SourceGenerated` allocates exactly what `Eager` does,
byte for byte, in both tables above. When a sink formats the message text, the generated code
still assembles an object array to pass to the formatter.

Where it does win is with a sink that reads the named properties without building text, which is
what structured backends do. That configuration is not measured here.

### About the handler

`Handled` is the only variant that avoids the object array, which is the 104-byte gap in the
"logging on" table. It is also the one to avoid: this implementation flattens the message into a
plain string, losing the named placeholders that make logs searchable in Seq, Splunk or
Application Insights. A handler could preserve them; this one does not.

Its timing is not strictly comparable to the rest either, because it bypasses `ILogger` entirely.
The allocation number is the honest part.

### Caveats

* `Alloc Ratio` reads `NA` because the baseline allocates nothing, so there is nothing to divide by.
* "Logging off" here means the level filter drops the provider entirely. In production it more
  often means the provider is alive and the level does not pass, which costs a few nanoseconds
  more per call than measured.
* `Allocated` does not depend on the GC configuration, `Gen0` does. These runs use Server GC,
  declared on the job so it appears in the report header.
* Measured on an Apple M1 (8 cores), macOS 15.7.3, .NET 10.0.100, three process launches per
  benchmark. Absolute values will differ on your hardware. The multiplier does not hold across
  input sizes either: 236x at 100 readings, 208x at 10.

Full report: [`results/LogMessageAssembly.md`](results/LogMessageAssembly.md)
Source: [`src/Patterns/LogMessageAssembly.cs`](src/Patterns/LogMessageAssembly.cs)

## Still to come

* An `O(n)` rebuild nested inside an `O(n)` loop
* Reflection-based JSON serialization on a hot path
* Re-sorting an index on every request when it never changes
* Column auto-fit dominating spreadsheet generation
* A generated file kept in memory twice
* A validator constructed per request from a default DI lifetime

## License

MIT. See [LICENSE](LICENSE).
