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
| **Eager** | 100 | **9,437.0 ns** | **231.38** | **12,288 B** |
| Guarded | 100 | 42.0 ns | 1.03 | – |
| SourceGenerated | 100 | 41.9 ns | 1.03 | – |
| Handled | 100 | 43.2 ns | 1.06 | – |

The ordinary call costs 231 times the method it is logging about, and allocates 12 KB that
nothing reads. A level check brings it back to within 3% of doing no logging at all.

This is a known enough problem that the .NET analyzers ship a rule for it. Building this
repository with `AnalysisLevel` set to latest fails on the `Eager` benchmark with
[CA1873](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1873):
"Evaluation of this argument may be expensive and unnecessary if logging is disabled." The
suppression in the source is there so the benchmark can measure what the rule warns about.

Note that with logging off, `Guarded` and `SourceGenerated` are the same program: `FindPeak`
plus one `IsEnabled` that returns false. Two rows, one measurement, taken twice.

### Logging on

| Method | Readings | Mean | Allocated |
|---|---|---|---|
| Eager | 100 | 9,880.4 ns | 17,368 B |
| Guarded | 100 | 9,910.3 ns | 17,368 B |
| SourceGenerated | 100 | 9,910.6 ns | 17,368 B |
| Handled | 100 | 11,318.4 ns | **17,264 B** |

The first three are within noise of each other: when the message is written, the guard buys
nothing and costs nothing. `Handled` is slower here in this run and its spread is wide; its time
is not comparable to the rest anyway, since it does not go through `ILogger`. Its allocation
number is the honest part.

### Where the bytes go

Compare the two `Eager` rows: 17,368 B with logging on, 12,288 B with it off.

* **5,080 B is the message string itself.** That is what deferred formatting saves.
* **12,288 B is spent either way.** Of that, 12,184 B is `Describe()` building its string, and
  104 B is the `object[4]` plus boxing `Value` and `Count`.

So `ILogger` skips 29% of the allocation when the level is off, and the caller pays the rest.
Only a level check removes it.

The split is worth reading carefully: at 100 readings, boxing is 24 bytes out of 12,288. The
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

### Caveats

* `Alloc Ratio` reads `NA` because the baseline allocates nothing, so there is nothing to divide by.
* "Logging off" here means the level filter drops the provider entirely. In production it more
  often means the provider is alive and the level does not pass, which costs a few nanoseconds
  more per call than measured.
* `Allocated` does not depend on the GC configuration, `Gen0` does. These runs use Server GC,
  declared on the job so it appears in the report header.
* Measured on an Apple M1 (8 cores), macOS 15.7.3, .NET 10.0.100, three process launches per
  benchmark. Absolute values will differ on your hardware. The multiplier does not hold across
  input sizes either: 231x at 100 readings, 184x at 10.

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
