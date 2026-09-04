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

With logging off, `Guarded` and `SourceGenerated` are the same program: `FindPeak`
plus one `IsEnabled` that returns false. Two rows, one measurement, taken twice.

### Logging on

| Method | Readings | Mean | Allocated |
|---|---|---|---|
| Eager | 100 | 9,880.4 ns | 17,368 B |
| Guarded | 100 | 9,910.3 ns | 17,368 B |
| SourceGenerated | 100 | 9,910.6 ns | 17,368 B |
| Handled | 100 | 11,318.4 ns | **17,264 B** |

The first three are within noise of each other: when the message is written, the guard buys
nothing and costs nothing. `Handled` is slower here in this run and its spread is wide. Its time
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
Application Insights. A handler could preserve them. This one does not.

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
  while the JIT works through the serializer, and BenchmarkDotNet burns that during warmup.
* The static `CachedOptions` field in the benchmark keeps the shared context rooted for the whole run.
  Code that only ever builds options per call, with nothing holding a reference, can do worse than
  measured here, because the table holds weak references.
* Same machine and settings as pattern 1: Apple M1, macOS 15.7.3, .NET 10.0.100, Server GC, three
  process launches.

Full report: [`results/SerializerOptionsReuse.md`](results/SerializerOptionsReuse.md)
Source: [`src/Patterns/SerializerOptionsReuse.cs`](src/Patterns/SerializerOptionsReuse.cs)

## 3. A struct as a dictionary key, and what CA1815 leaves behind

A struct used as a dictionary key without `IEquatable<T>` resolves to `ObjectEqualityComparer<T>`,
which calls `object.Equals`. The struct does not override it, so the call lands on `ValueType.Equals`,
which walks the fields by reflection. `Dictionary` hashes the key first, and that path is
`ValueType.GetHashCode`, which is reflection too. Both box. Nothing in the source says so.

Five key types with the same two fields, because three separate things get run together here: what
the reflection path costs, how much of it the analyzer rule actually removes, and why a
`record struct` beats an `IEquatable<T>` written by hand.

* **Plain** overrides nothing.
* **AnalyzerFix** does exactly what CA1815 asks and nothing more: `Equals(object)`, `GetHashCode`,
  and the `==` / `!=` operators.
* **Equatable** is the real fix, `IEquatable<T>` with `HashCode.Combine`. Baseline.
* **CheapHash** is the same `IEquatable<T>` with `GetHashCode` replaced by the multiply-and-add a
  record generates.
* **Record** is a `record struct`, both members generated.

| Method | Entries | Mean | Ratio | Allocated |
|---|---|---|---|---|
| Equatable | 100 | 1.93 us | 1.00 | – |
| **Plain** | 100 | **13.29 us** | **6.92** | **18,400 B** |
| AnalyzerFix | 100 | 2.87 us | 1.50 | 3,200 B |
| CheapHash | 100 | 1.36 us | 0.71 | – |
| Record | 100 | 1.37 us | 0.71 | – |
| Equatable | 1000 | 20.74 us | 1.00 | – |
| **Plain** | 1000 | **139.94 us** | **6.75** | **184,000 B** |
| AnalyzerFix | 1000 | 33.00 us | 1.59 | 32,000 B |
| CheapHash | 1000 | 14.81 us | 0.71 | – |
| Record | 1000 | 14.60 us | 0.70 | – |

Both benchmarks look up every key in the table once, so the numbers divide cleanly: `Plain` costs
184 B per lookup at either size, `AnalyzerFix` costs 32 B, the rest cost nothing.

### Where the 184 bytes go

Measured directly with `GC.GetAllocatedBytesForCurrentThread` around single calls:

| | Bytes |
|---|---|
| Boxing the key for `ValueType.GetHashCode` | 32 |
| Two boxes for `ValueType.Equals` | 64 |
| The `FieldInfo[2]` that `ValueType.Equals` builds to walk the fields | 40 |
| Two boxed `int` from `FieldInfo.GetValue` on the second field | 48 |
| **Per lookup that hits** | **184** |

The `string` field costs nothing to read, because `GetValue` hands back the existing reference. The
`int` is what gets boxed, twice, once per operand.

A lookup that misses costs 32 B, not 184. The hash does not match, so `Equals` is never reached. This
benchmark is all hits, which is the expensive end of the range.

### What CA1815 asks for is not enough

[CA1815](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1815) says a
value type "should override Equals" and "should override the equality (==) and inequality (!=)
operators". It never mentions `IEquatable<T>`. `AnalyzerFix` is that advice followed to the letter,
and it is still 1.5x slower than the real fix and still allocates 32 B on every lookup.

The reason is that overriding `Equals(object)` gets rid of one box, not both. The receiver no longer
needs boxing, because the struct now has its own override. The argument still does, because the
signature takes `object`. `EqualityComparer<T>.Default` only picks the non-boxing implementation when
the type implements `IEquatable<T>`, which the rule does not ask for.

Two further things about this rule. It is **not enabled by default**, and by default it only looks at
externally visible types, so the private key structs in this benchmark would not have tripped it at
all.

### Why the record wins, and it is not the equality

The obvious guess is that the compiler writes a better `Equals`. It does not. `CheapHash` and
`Record` have different `Equals` implementations and land on the same number, 0.71 in both rows.
`Equatable` and `CheapHash` have the same `Equals` and differ by 29%.

The whole gap is `GetHashCode`. `HashCode.Combine` runs xxHash32: four rounds plus a final avalanche,
seeded randomly per process. What a record generates is one multiply and one add per field. For a
dictionary key that is called on every lookup, and the cheaper hash wins.

Before copying that: the record's hash is **not** randomised per process. Here the string field brings
its own randomisation through `string.GetHashCode`, but a key made only of integers would hash
predictably across runs, which matters if untrusted input can choose the keys.

### What actually triggers the reflection path

Not "non-blittable", which is the wording the CA1815 documentation uses. The runtime falls back to
reflection if **any** of these hold: the struct contains GC references, or it has padding, or it is
an `[InlineArray]`, or it already overrides `Equals` or `GetHashCode`, or it contains a `float` or a
`double`. That last one surprises people. `struct { double, double }` has no padding and no
references and still takes the slow path, because the fast path compares bits and `-0.0` would not
equal `0.0`.

So `struct { int, int }` shows almost none of this, and `struct { byte, int }` shows all of it. Check
your own key type before assuming the number transfers.

One more trap on that path: the reflection `ValueType.GetHashCode` hashes only the **first non-null
field**. For `PlainKey` that is `Sensor`, and `Channel` never reaches the hash at all. Harmless when
the first field is unique, quietly catastrophic when it is not.

### Caveats

* The three key types could have produced different bucket distributions, which would mean measuring
  collisions rather than comparison cost. They did not: 1103 buckets for each, 656 to 671 occupied,
  longest chain 5 to 6, mean chain position within 1.5% across all of them.
* Lookup keys are built as separate `string` instances with the same content, so the string
  comparison actually runs. Reusing the stored instances would short-circuit it on reference equality
  and understate every variant.
* All lookups hit. See the byte table above for what a miss costs.
* `Plain` is the noisiest row here, with a standard deviation around 11% of its mean at both sizes.
  The reflection path calls into the runtime through a QCall that is not cached, and it varies.
* Same machine and settings as the earlier patterns: Apple M1, macOS 15.7.3, .NET 10.0.100, Server
  GC, five process launches.

Full report: [`results/StructDictionaryKey.md`](results/StructDictionaryKey.md)
Source: [`src/Patterns/StructDictionaryKey.cs`](src/Patterns/StructDictionaryKey.cs)

## 4. SearchValues, and a null result that was wrong

`IndexOfAny` over a set of more than five characters builds an ASCII bitmap on every call.
`SearchValues<char>` builds it once. Below six values `IndexOfAny` has dedicated paths and there is
nothing worth caching, which is the same threshold
[CA1870](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1870) uses:
the analyzer's own constant is `MinLengthWorthReplacing = 6`.

Ten delimiters, one of them planted three characters from the end so the scan covers the string.

| Method | Payload | Mean | Ratio |
|---|---|---|---|
| Cached | 128 | 7.49 ns | 1.00 |
| CachedArray | 128 | 16.70 ns | 2.26 |
| InlineArray | 128 | 16.30 ns | 2.21 |
| Cached | 512 | 24.80 ns | 1.00 |
| CachedArray | 512 | 32.49 ns | 1.31 |
| InlineArray | 512 | 32.89 ns | 1.33 |
| Cached | 1024 | 49.73 ns | 1.00 |
| CachedArray | 1024 | 57.96 ns | 1.17 |
| InlineArray | 1024 | 58.76 ns | 1.18 |
| Cached | 4096 | 194.06 ns | 1.00 |
| CachedArray | 4096 | 215.82 ns | 1.11 |
| InlineArray | 4096 | 215.64 ns | 1.11 |

### The first version of this benchmark reported no difference at all

It had two payload sizes, 128 and 4096, and three process launches. At 4096 it gave 208.2 ns for
`Cached` against 208.4 ns for `CachedArray`, and the obvious reading was that the setup cost
amortises away once the scan is long enough.

That reading was wrong, and the report said so if you read past the mean. The `Cached` row at 4096
had a standard deviation of 13 ns against 4.5 ns for the two rows next to it, and a mean 3 ns above
its own median. It was one bad row, sitting in the denominator of every ratio on that line.

Two more sizes and five launches instead of three, and the gap is there at every size.

### What the four sizes show

Fitting a line through them separates two effects that the two-point version could not tell apart:

| | Fixed cost | Per character |
|---|---|---|
| Cached | 1.3 ns | 0.0471 ns |
| CachedArray | 7.9 ns | 0.0507 ns |
| InlineArray | 8.1 ns | 0.0506 ns |

The bitmap costs about 6.6 ns to build, and it is a **fixed** cost. It does not shrink as the scan
grows. What shrinks is its share: 123% of the total at 128 characters, 11% at 4096.

The second column is the part worth noticing. The array path is also 7% slower per character, and
both array variants agree on it to three decimals while differing from `Cached`. That is not noise.
Caching the values is not only saving the setup, it is also getting a scan loop chosen for that
specific set.

So the advice is worth following wherever a scan is short and frequent, which is most parsing of
headers, tokens and delimited fields. On one long scan of a large buffer it is worth about 11%.
The variable that matters is the length of a single scan, not the size of the input.

### The array at the call site does not allocate

`InlineArray` and `CachedArray` are the same speed, and the memory diagnoser shows nothing for
either. A constant list of characters passed to a `ReadOnlySpan<char>` parameter compiles to a
`RuntimeHelpers.CreateSpan` against a metadata blob, with no `newarr` in the IL. Hoisting it into a
`static readonly` field, which a lot of performance writing still recommends, buys nothing.

Two conditions on that. It has been true since Visual Studio 17.5, and it depends on the parameter
being a span. Collection expression syntax is not what does it: `new[] { ... }` in the same position
compiles identically, and `char[] d = [...]` assigned to an array variable still allocates.

### Caveats

* `Alloc Ratio` reads `NA` because nothing here allocates.
* Apple M1, so this is ARM64 NEON. `IndexOfAny` has separate AVX-512 paths, and the ratios on a
  recent x86 server will not be these.
* The two array rows at 4096 have a standard deviation around 5% of their mean, the widest in the
  table. The differences being described there are 11%, so the ordering holds, but that row is the
  one to re-measure first if the numbers matter to you.
* `Cached` reaches the search through a virtual call on `SearchValues<char>` and the array variants
  do not, which counts against the baseline rather than for it.
* .NET 10.0.100, Server GC, five process launches.

Full report: [`results/SearchValuesLookup.md`](results/SearchValuesLookup.md)
Source: [`src/Patterns/SearchValuesLookup.cs`](src/Patterns/SearchValuesLookup.cs)

## Still to come

* Defensive copies from a non-`readonly` struct passed by `in`
* Regex: the static-call cache, `IsMatch`, and `[GeneratedRegex]`
* One dictionary lookup instead of three

## License

MIT. See [LICENSE](LICENSE).
