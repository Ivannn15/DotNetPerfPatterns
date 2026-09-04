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

## 2. JsonSerializerOptions: the advice is right, the reason is not

The familiar guidance is to cache `JsonSerializerOptions` because every instance builds its own
metadata. Since .NET 8 that stopped being true. Options are matched by structural equality against a
process-wide table of 64 weakly-referenced caching contexts, so two instances configured the same way
share one set of `JsonTypeInfo`. A per-call instance costs an allocation and a comparison, not a rebuild.

The advice still holds, but the expensive case is somewhere else. **Converters are compared by
reference.** A fresh converter instance makes the options structurally unique, they match nothing in
the table, and the metadata is rebuilt from scratch on every call.

Five call sites, baseline is the cached instance:

* **Cached** is one static instance, reused. What the guidance asks for.
* **PerCall** is a new instance every call, no converter.
* **Copied** is `new JsonSerializerOptions(existing)`.
* **PerCallSharedConverter** is a new instance every call holding a converter that is itself shared.
* **PerCallNewConverter** is the same code with `new RoundingConverter()` instead.

| Method | Readings | Mean | Ratio | Allocated |
|---|---|---|---|---|
| Cached | 1 | 223.1 ns | 1.01 | 512 B |
| PerCall | 1 | 596.0 ns | 2.69 | 718 B |
| Copied | 1 | 546.1 ns | 2.46 | 718 B |
| PerCallSharedConverter | 1 | 637.6 ns | 2.87 | 830 B |
| **PerCallNewConverter** | 1 | **26,057.5 ns** | **117.42** | **20,671 B** |
| Cached | 50 | 8,649.9 ns | 1.00 | 8,984 B |
| PerCall | 50 | 8,984.4 ns | 1.04 | 9,186 B |
| Copied | 50 | 9,617.7 ns | 1.12 | 9,186 B |
| PerCallSharedConverter | 50 | 8,421.1 ns | **0.98** | 9,106 B |
| **PerCallNewConverter** | 50 | **31,209.8 ns** | **3.62** | **28,955 B** |

### The two rows that matter

`PerCallSharedConverter` and `PerCallNewConverter` differ by one word. Both build options on every
call, both configure the same converter type. One shares the cache, one does not, and on a
single-reading payload that word costs **117x the time and 25x the allocations**.

Compare that to `PerCall`, which allocates a fresh options object every call and lands at 1.04 on the
larger payload. Creating the options is not the expensive part. Making them structurally unique is.

The gap narrows as the payload grows, because the rebuild is a fixed cost paid once per call while
serialization work scales. At 50 readings it is still 3.6x, and it does not go away.

`Copied` is the case people assume is free. The copy constructor starts without a cache reference but
resolves to the same shared context on first use, so what you pay for is copying the fields, not
rebuilding metadata.

This is [CA1869](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1869),
and the converter-identity variant is [dotnet/runtime#122548](https://github.com/dotnet/runtime/issues/122548),
where .NET 10 made the rebuild worse by emitting IL for it.

### Caveats

* These are steady-state numbers. The first serialization of a type in a process costs milliseconds
  while the JIT works through the serializer; BenchmarkDotNet burns that during warmup.
* The static `CachedOptions` field in the benchmark keeps the shared context rooted for the whole run.
  Code that only ever builds options per call, with nothing holding a reference, can do worse than
  measured here, because the table holds weak references.
* Same machine and settings as pattern 1: Apple M1, macOS 15.7.3, .NET 10.0.100, Server GC, three
  process launches.

Full report: [`results/SerializerOptionsReuse.md`](results/SerializerOptionsReuse.md)
Source: [`src/Patterns/SerializerOptionsReuse.cs`](src/Patterns/SerializerOptionsReuse.cs)

## Still to come

* A struct without `IEquatable<T>` used as a dictionary key
* `SearchValues` instead of an array of characters
* Defensive copies from a non-`readonly` struct passed by `in`
* Regex: the static-call cache, `IsMatch`, and `[GeneratedRegex]`
* One dictionary lookup instead of three

## License

MIT. See [LICENSE](LICENSE).
