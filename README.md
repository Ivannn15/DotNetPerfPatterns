# .NET Performance Patterns

[![CI](https://github.com/Ivannn15/DotNetPerfPatterns/actions/workflows/ci.yml/badge.svg)](https://github.com/Ivannn15/DotNetPerfPatterns/actions/workflows/ci.yml)

BenchmarkDotNet benchmarks for performance patterns I keep hitting in production ASP.NET Core
services. Each one has the version people normally write next to the alternatives, measured
against a baseline of the same method with the pattern removed.

Three of the seven contradicted the headline advice outright: one rule holds for a different reason
than the one usually given, one holds only until the JIT gets involved, and one step could not be
separated from noise at all. Two more killed a piece of folklore attached to them. Those are in here
with the numbers that say so, because a benchmark that only ever confirms what you expected is not
being read carefully enough.

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

The reports under `results/` were produced with more launches than the default, and the header of
each one records the settings it was run with. Patterns 1 and 2 used three launches:

```bash
dotnet run -c Release -- --filter '*LogMessageAssembly*' --launchCount 3
```

Patterns 3 and 4 measure differences of a few nanoseconds and needed more:

```bash
dotnet run -c Release -- --filter '*StructDictionaryKey*' \
    --launchCount 9 --warmupCount 10 --iterationCount 20
```

Patterns 5 and 7 needed more again:

```bash
dotnet run -c Release -- --filter '*DefensiveCopies*' \
    --launchCount 15 --warmupCount 15 --iterationCount 30
```

Pattern 6 has one arm that constructs a `Regex` per call, so it needs fewer:

```bash
dotnet run -c Release -- --filter '*RegexConstruction*' \
    --launchCount 5 --warmupCount 6 --iterationCount 15
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
`ValueType.GetHashCode`, which reads the fields through the runtime rather than through your code.
Both of them box. Nothing in the source says so.

Five key types with the same two fields, because three separate things get conflated here: what the
reflection path costs, how much of it the analyzer rule removes, and why a `record struct` beats an
`IEquatable<T>` written by hand.

* **Plain** overrides nothing.
* **AnalyzerFix** is what the CA1815 analyzer checks for: an `Equals(object)` override and the
  `==` / `!=` operators, plus `GetHashCode`, which comes along because CS0659 requires it.
* **Equatable** is the real fix, `IEquatable<T>` with `HashCode.Combine`.
* **CheapHash** is that same `IEquatable<T>` with `GetHashCode` replaced by the multiply-and-add a
  record generates.
* **Record** is a `record struct`, both members generated.

The baseline is `Equatable` rather than `Plain`, so a ratio above 1.00 means slower than the fix
rather than slower than the bug.

| Method | Entries | Mean | Ratio | Allocated |
|---|---|---|---|---|
| Equatable | 100 | 1.85 us | 1.00 | – |
| **Plain** | 100 | **14.42 us** | **7.80** | **18,400 B** |
| AnalyzerFix | 100 | 2.82 us | 1.53 | 3,200 B |
| CheapHash | 100 | 1.36 us | 0.74 | – |
| Record | 100 | 1.34 us | 0.73 | – |
| Equatable | 1000 | 20.60 us | 1.00 | – |
| **Plain** | 1000 | **131.67 us** | **6.39** | **184,000 B** |
| AnalyzerFix | 1000 | 32.53 us | 1.58 | 32,000 B |
| CheapHash | 1000 | 14.78 us | 0.72 | – |
| Record | 1000 | 14.75 us | 0.72 | – |

Call it seven times slower rather than 7.80. The `Plain` rows move by a full point of ratio between
runs, for reasons in the caveats. Everything else here is stable to two decimals.

Both sizes look up every key in the table once, so the allocation divides cleanly: `Plain` costs
184 B per lookup at either size, `AnalyzerFix` costs 32 B, the other three cost nothing.

### Where the 184 bytes go

Two of these numbers are measured with `GC.GetAllocatedBytesForCurrentThread` around a single call:
a lone `GetHashCode` on the reflection path costs 32 B, and a lone `Equals` that returns true costs
152 B. The split of that 152 is not separately measurable from outside, and is worked out from what
`ValueType.Equals` does and what the objects weigh on a 64-bit runtime:

| | Bytes | |
|---|---|---|
| Boxing the key for `ValueType.GetHashCode` | 32 | measured |
| Two boxes for `ValueType.Equals` | 64 | derived |
| The `FieldInfo[2]` that `ValueType.Equals` builds to walk the fields | 40 | derived |
| Two boxed `int` from `FieldInfo.GetValue` on the second field | 48 | derived |
| **Per lookup that hits** | **184** | measured |

The `string` field costs nothing to read, because `GetValue` hands back the existing reference. The
`int` is what gets boxed, once per operand.

A lookup that misses usually costs 32 B rather than 184, because `Dictionary` compares the stored
32-bit hash before it calls `Equals`, so `Equals` is never reached. Usually, not always: see the
last section for when two different keys hash the same.

This benchmark is all hits, which is the expensive end of that range, and `GlobalSetup` throws if any
arm returns a different count rather than leaving that as a claim in the README.

### What CA1815 asks for is not enough

[CA1815](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1815) says
"your value type should implement Equals" and that "you should also provide an implementation of the
equality and inequality operators". Its example implements `IEquatable<T>` as well, but the analyzer
does not look for that: it checks for an `object.Equals` override and for the two operators, and it
is satisfied without `IEquatable<T>`.

`AnalyzerFix` is the rule satisfied exactly. It is still 1.5x slower than the real fix and still
allocates 32 B on every lookup.

Overriding `Equals(object)` removes one box, not two. The receiver no longer needs boxing, because
the struct now has its own override. The argument still does, because the signature takes `object`.
`EqualityComparer<T>.Default` only picks the non-boxing comparer when the type implements
`IEquatable<T>`, which is the thing the rule does not check.

Two further things about this rule. It is **not enabled by default**, and by default it only looks at
externally visible types, so the private key structs in this benchmark would not have tripped it.

### Why the record wins, and it is not the equality

The obvious guess is that the compiler writes a better `Equals`. It does not. `CheapHash` and
`Record` have different `Equals` implementations and land on the same number. `Equatable` and
`CheapHash` have the identical `Equals` and differ by around 28%.

The whole gap is `GetHashCode`. `HashCode.Combine` for two arguments runs two rounds of xxHash32
plus a final avalanche, seeded randomly per process. What a record generates is one multiply and one
add per field. For a dictionary key that runs on every lookup, and the cheaper one wins.

Two things before copying that hash. It mixes worse than `HashCode.Combine`, which is the trade being
made and not a free win. And it is not randomized per process: here the string field brings its own
randomization through `string.GetHashCode`, but a key made only of integers would hash identically
across runs, which matters when untrusted input chooses the keys.

### What actually sends a struct down the reflection path

Not "non-blittable", which is the wording the CA1815 documentation uses. The runtime falls back if
**any** of these hold: the struct contains GC references, or it has padding, or it is an
`[InlineArray]`, or the type already overrides `Equals` or `GetHashCode`, or it contains a `float` or
a `double`, or it contains another struct that fails the same test.

The floating-point one surprises people. `struct { double, double }` has no padding and no references
and still takes the slow path, because the fast path compares raw bits, and by bits `-0.0` does not
equal `0.0`. NaN payloads are a second reason for the same rule.

So `struct { int, int }` shows almost none of this and `struct { byte, int }` shows all of it. Check
your own key type rather than assuming the numbers transfer.

One more trap on that path. The reflection `GetHashCode` hashes only the **first non-null field**.
For `PlainKey` that is `Sensor`, and `Channel` never reaches the hash. Harmless when the first field
is unique, and quietly expensive when it is not: two keys sharing a `Sensor` collide, which is the
case where a miss costs the full 184 B instead of 32 B.

### Caveats

* `Plain` is the noisy row: standard deviation 12 to 15% of its mean, and its ratio moved from 6.68
  to 7.80 at 100 entries between two runs of the same build. On that path `GetHashCode` asks the
  runtime which hashing strategy the type needs through an uncached call on every invocation, and the
  cost of that varies. Errors stay under 5% of the mean, but the ratio should be read as "about
  seven", not to two decimals. The other eight rows move by less than 2%.
* The five key types could have produced different bucket distributions, which would mean measuring
  collisions rather than comparison cost. At 1000 entries they did not: 1103 buckets in every case,
  656 to 671 of them occupied, longest chain 5 to 6, mean chain position within 1.5% of each other.
* Lookup keys are built as separate `string` instances with the same content, so the string
  comparison actually runs. Reusing the stored instances would short-circuit it on reference equality
  and understate every arm.
* `Alloc Ratio` reads `NA` because the baseline allocates nothing.
* Not run against .NET 9, so this does not show whether anything changed between the two.
* Apple M1, macOS 15.7.3, .NET 10.0.100, Server GC, nine process launches.

Full report: [`results/StructDictionaryKey.md`](results/StructDictionaryKey.md)
Source: [`src/Patterns/StructDictionaryKey.cs`](src/Patterns/StructDictionaryKey.cs)

## 4. SearchValues, and a row that would not reproduce

`IndexOfAny` over a set of more than five characters builds an ASCII bitmap on every call.
`SearchValues<char>` builds it once. Below six values `IndexOfAny` has dedicated paths, and that is
also where [CA1870](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1870)
stops firing: the analyzer's own threshold is `MinLengthWorthReplacing = 6`. It does still fire below
six when the expression allocates, such as `"ab".ToCharArray()`.

Ten delimiters, one of them planted three characters from the end so the scan crosses the string.

| Method | Payload | Mean | StdDev | Ratio |
|---|---|---|---|---|
| Cached | 128 | 7.05 ns | 0.33 | 1.00 |
| CachedArray | 128 | 15.16 ns | 0.13 | 2.16 |
| InlineArray | 128 | 15.03 ns | 0.09 | 2.14 |
| Cached | 512 | 24.35 ns | 0.33 | 1.00 |
| CachedArray | 512 | 32.39 ns | 0.38 | 1.33 |
| InlineArray | 512 | 32.33 ns | 0.21 | 1.33 |
| Cached | 1024 | 50.80 ns | 0.43 | 1.00 |
| CachedArray | 1024 | 57.96 ns | 0.31 | 1.14 |
| InlineArray | 1024 | 58.23 ns | 0.85 | 1.15 |
| Cached | 4096 | 197.21 ns | **10.16** | 1.00 |
| CachedArray | 4096 | 202.18 ns | 2.31 | 1.03 |
| InlineArray | 4096 | 201.76 ns | 2.35 | 1.03 |

### The cost is fixed, and the ratio is not

Subtract the baseline from the array version at each of the three smaller sizes: 8.11 ns, 8.04 ns,
7.16 ns. Building the bitmap costs about 8 ns and that does not change as the scan gets longer. What
changes is what 8 ns is worth: it more than doubles the total at 128 characters, and it is 14% of it
at 1024.

So the rule of thumb is worth following wherever a scan is short and runs constantly, which covers
most parsing of headers, tokens and delimited fields. The variable is the length of a single scan.
This benchmark does one scan per call, so it does not separate scan length from input length. A
parser that walks a 4 KB buffer in a hundred short hops pays the 8 ns a hundred times.

### The 4096 row is not a measurement

The first version of this benchmark used two payload sizes and three launches, and its 4096 row
reported the array version as the same speed as `SearchValues`. That looked like the setup cost
amortizing away, and it was worth writing up.

It was not real. Run it again and that row says something different every time: the gap there came
out at 0.1 ns, then 34.8 ns, then 5.0 ns across three runs of the same build, while the three shorter
scans stayed within a nanosecond of 8 in every run. In the published report the baseline at 4096 has a
standard deviation of 10.2 ns against 2.3 for the two rows beside it, which is larger than the effect
it is supposed to resolve.

The per-run numbers are in
[`results/SearchValuesLookup-repeats.md`](results/SearchValuesLookup-repeats.md). The row stays in
the table because deleting inconvenient rows is worse, but nothing here is inferred from it.

There is a reason not to expect a difference in the per-character rate, either. Both paths end up in
the same generic instantiation of the same scanning method, so after the bitmap exists the work is
identical machine code. Any per-character gap that shows up in a fit across these four points is an
artifact of the 4096 row, not a property of the code.

### The array at the call site does not allocate

`InlineArray` and `CachedArray` are the same speed, and the memory diagnoser shows nothing for
either. A list of constant characters whose target type is `ReadOnlySpan<char>` compiles to a
`RuntimeHelpers.CreateSpan` against a metadata blob, with no `newarr` in the IL. Hoisting it into a
`static readonly` field, which a lot of performance writing still recommends, buys nothing.

Three conditions on that. It needs a compiler from Visual Studio 17.5 or later and a .NET 7 or later
target. It depends on the target type being `ReadOnlySpan<T>`, not `Span<T>`. And the collection
expression syntax is not what does it: `new[] { ... }` in the same position compiles identically,
while `char[] d = [...]` assigned to an array variable still allocates.

### Caveats

* `Alloc Ratio` reads `NA` because nothing here allocates.
* Apple M1, so this is ARM64 NEON. `IndexOfAny` has separate AVX-512 paths, and the ratios on a
  recent x86 server will not be these.
* Besides 4096, the widest row is the baseline at 128, at 4.7% standard deviation, where a whole call
  is seven nanoseconds. The 2.16 ratio in the first row carries that spread with it.
* `Cached` reaches the search through a virtual call on `SearchValues<char>` and the array variants
  do not, which counts against the baseline rather than for it.
* .NET 10.0.100, Server GC, nine process launches, twenty iterations.

Full report: [`results/SearchValuesLookup.md`](results/SearchValuesLookup.md)
Source: [`src/Patterns/SearchValuesLookup.cs`](src/Patterns/SearchValuesLookup.cs)

## 5. Defensive copies, and where they stopped happening

Passing a struct by `in` avoids the copy at the call. The standard warning is that it does not avoid
copies inside: every member the compiler cannot prove leaves the struct alone forces a copy of the
whole thing first, in case that member writes to it. The advice that follows is to mark the struct
`readonly`, or at least its members.

The compiler still emits that copy. On .NET 10 it usually does not survive to machine code.

Six arms over the same 56-byte aggregate, seven fields, reached through an `in` parameter. The first
four read three computed properties small enough for the JIT to inline. The last two call one cheap
member marked `[MethodImpl(MethodImplOptions.NoInlining)]`, which is what a member too large to
inline looks like from the caller's side.

| Method | Mean | Ratio |
|---|---|---|
| ReadonlyStructIn | 2.125 us | 1.00 |
| MutableIn | 2.121 us | 1.00 |
| ReadonlyMembersIn | 2.110 us | 0.99 |
| MutableByValue | 2.108 us | 0.99 |
| **ReadonlyStructSeparateMember** | **1.262 us** | **0.59** |
| **MutableSeparateMember** | **1.967 us** | **0.93** |

The last two arms do less arithmetic than the first four, so their ratios against the baseline say
nothing. The only comparison those two rows support is with each other.

The first four are the same number. A `readonly struct`, a mutable one, a mutable one with
`readonly` members, and a plain by-value pass all land within 1% of each other. For members the JIT
inlines, the advice buys nothing on this runtime.

In the last two, the mutable arm costs 56% more than the `readonly` one, and they differ in exactly
one thing: whether the struct is `readonly`.

### The copy is always in the IL

Roslyn emits it whenever a non-`readonly` member of a non-`readonly` struct is reached through a
readonly reference, which an `in` parameter is. It does that regardless of what the JIT will later
do, and it knows nothing about inlining. So the difference between the two groups above is not the
compiler changing its mind. It is whether the JIT can delete what the compiler emitted.

### Why the first four are identical

Physical promotion, which arrived in .NET 8 and is on by default. Its decomposition step replaces a
struct copy that only feeds reads of individual fields with loads of the fields actually read, and
drops the copy.

This is checkable rather than a story. Take a method that reads three properties off an
`in MutableWindow` and disassemble it with `DOTNET_TieredCompilation=0`. The loads come straight off
the incoming byref and there is no copy. Add `DOTNET_JitEnablePhysicalPromotion=0` and eight vector
loads and stores appear, which is the three copies the IL asked for.

### Why the last two are not

A call that is not inlined needs a real address, so the copy has to materialize somewhere. Physical
promotion cannot help: setting `DOTNET_JitEnablePhysicalPromotion=0` changes nothing in the
disassembly of that loop, because the copy is not there to be optimized away.

What the loop does for the mutable struct is zero a 56-byte stack slot and move the element into it,
32 bytes then 16 then 8, before passing the address of the slot. For the `readonly struct` it
computes the element's own address in two `add` instructions and passes that. The difference is the
0.7 ns per window in the table.

### What to take from it

Marking a struct `readonly` is still worth doing. Two analyzer rules push toward it:
[IDE0251](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0251),
"Member can be made 'readonly'", which is what fires on the struct in this benchmark, and
[IDE0250](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0250),
"Struct can be made 'readonly'", which only fires once every field is `readonly` too. Both are
suggestions, and neither runs on a command-line build without `EnforceCodeStyleInBuild`.

What is not true any more is the reason usually given. The copies show up where a member is too big to inline, not
everywhere a mutable struct meets an `in` parameter, and on a 56-byte struct the cost of one is
under a nanosecond.

If a codebase is being changed on the strength of this rule, measure it there first. The four arms
above are what most of that code will look like.

### Caveats

* The `NoInlining` attribute is doing real work in the last two arms. It stands in for a member big
  enough that the JIT declines to inline it, which is a normal situation but not the one the first
  four arms measure. Both cases are in the table for that reason.
* `Midpoint` is deliberately trivial. Anything heavier and the arithmetic swamps a sub-nanosecond
  copy, which is what an earlier version of this benchmark did wrong.
* Apple M1, macOS 15.7.3, .NET 10.0.100, Server GC, fifteen process launches, thirty iterations.

Full report: [`results/DefensiveCopies.md`](results/DefensiveCopies.md)
Source: [`src/Patterns/DefensiveCopies.cs`](src/Patterns/DefensiveCopies.cs)

## 6. Regex: the expensive line is not the one people guard

`^[a-z]{3,8}-\d{4}(?:\.\d{1,3})?$`, matched five ways, 100 strings per invocation with one in five
deliberately failing. The pattern is anchored, so there is no scanning phase: what is left is the
difference between construction, cache lookup, and the engine itself.

| Method | Mean | Ratio | Allocated |
|---|---|---|---|
| Generated | 2,414 ns | 1.00 | – |
| **NewPerCall** | **136,604 ns** | **56.59** | **366,080 B** |
| StaticMethod | 8,426 ns | 3.49 | – |
| CachedInstance | 8,349 ns | 3.46 | – |
| CachedCompiled | 2,801 ns | 1.16 | – |

### The two rows that are the same

`StaticMethod` and `CachedInstance` differ by less than 1%, and the cached instance came out
nominally slower despite doing strictly less work. One looks the pattern up in a process-wide cache
on every call. The other holds a `static readonly Regex` and skips that entirely.
The lookup is a volatile read of a one-element cache plus a comparison of a four-field key, with no
lock on that path, and it costs nothing measurable against the matching itself.

That is worth knowing in both directions. Hoisting `Regex.IsMatch(input, pattern)` into a cached
field, on its own, buys close to nothing. But it is 3.5x off the pace anyway, and the reason is the
next section.

### The line that actually costs

`new Regex(pattern)` inside the loop is 56x the baseline and allocates 366 KB per invocation. The
constructor parses the pattern every time and never consults the cache: that is what the static
method is for. At one input per invocation construction still dominates, around 50x, though those
rows have a wide spread. That is the shape most real code has: nothing to amortize the construction
over.

### Interpreted against compiled

After construction, the interpreter is what costs: 3x against `CachedCompiled` and 3.5x against the
generated matcher. Both cached arms are one instance, built once, reused. One walks interpreter
opcodes. The other runs IL emitted at construction.

`[GeneratedRegex]` then beats `RegexOptions.Compiled` by 16%. Less than the 3x above it, and the
steady-state number is not the main reason to prefer it: the generator emits C# at compile time, so
there is no reflection emit at startup, it survives trimming, and it works under AOT, where
`RegexOptions.Compiled` silently falls back to the interpreter.

So the ordering of what to fix: stop constructing in a loop, then move off the interpreter. Whether
the cached instance is reached through a static call or a field is the part that does not matter.

This is [SYSLIB1045](https://learn.microsoft.com/dotnet/fundamentals/syslib-diagnostics/syslib1040-1049),
"Use 'GeneratedRegexAttribute' to generate the regular expression implementation at compile-time",
which is enabled by default at Info severity. It fires on the constant-pattern construction as well
as on the static call, which is why both arms carry a suppression.

### Caveats

* This measures the best case for the static cache lookup. `Pattern` is a `const`, so the cache key
  holds the same interned reference and the string comparison exits on reference equality. A pattern
  built at runtime is compared character by character. `CultureInfo.CurrentCulture` is read on every
  call either way.
* The single-input rows have standard deviations of 5 to 17% and are in the report for shape rather
  than for their values. At one input `CachedCompiled` comes out ahead of `Generated`, and that
  ordering should not be relied on.
* `NewPerCall` has an 8.8% standard deviation at 100 inputs. Against a 56x ratio it changes nothing.
* No `IgnoreCase`, so the culture the arms capture differently has no effect on what they match.
* Apple M1, macOS 15.7.3, .NET 10.0.100, Server GC, five process launches, fifteen iterations.

Full report: [`results/RegexConstruction.md`](results/RegexConstruction.md)
Source: [`src/Patterns/RegexConstruction.cs`](src/Patterns/RegexConstruction.cs)

## 7. Counting the dictionary probes, and finding the allocation

Counting tokens by key. The first three arms differ only in how many times they hash the key to
perform one update, and the fourth changes what the key is. The result is not the one the arms were
built to show.

String hash codes are not memoized in .NET, so three dictionary operations really are three hashes
of the same string.

* **ContainsKeyThenIndexer** is `ContainsKey`, then the indexer to read, then the indexer to write.
* **TryGetValueThenIndexer** is the usual fix, two of each.
* **ValueRef** is `CollectionsMarshal.GetValueRefOrAddDefault`, which hands back a reference into the
  entry, so reading and writing are one probe.
* **AlternateLookup** is that same call against a `ReadOnlySpan<char>` alternate lookup, so a string
  is built only when a token is seen for the first time.

| Method | Mean | StdDev | Ratio | Allocated |
|---|---|---|---|---|
| ContainsKeyThenIndexer | 38.44 us | 3.59 | 1.29 | 46.88 KB |
| TryGetValueThenIndexer | 29.82 us | 1.75 | 1.00 | 46.88 KB |
| ValueRef | 28.14 us | 2.49 | 0.95 | 46.88 KB |
| **AlternateLookup** | **16.05 us** | 2.12 | **0.54** | **2.81 KB** |

### Two of the three steps are real

Three probes to two is worth 29%. That step gives a figure for what one probe costs on these keys:
8.6 us per invocation, or 8.6 ns per token.

Two probes to one should then be worth about the same, and it is not. The measured gap is 1.7 us,
and two full runs of the same build put `ValueRef` at 0.82 and 0.95 against the same baseline, which
is a wider disagreement than the effect. The per-run numbers are in
[`results/DictionaryLookupCount-repeats.md`](results/DictionaryLookupCount-repeats.md).

So the step is somewhere between nothing and what the three-to-two step predicts, and this benchmark
cannot say where. It is still the right change to make. It is not something these numbers establish.

The fourth arm is where that changes. Around 60 distinct keys appear across 1000 tokens, so 94% of
updates land on a key the dictionary already holds, and for those no string is needed at all. That is
16x less allocation and roughly half the time, both far outside the spread.

Which is the useful ordering: the probe count is worth tidying, and the allocation is worth fixing.

### What makes the alternate lookup possible

`GetAlternateLookup<ReadOnlySpan<char>>()` requires the dictionary's comparer to implement
`IAlternateEqualityComparer<ReadOnlySpan<char>, string>`. The ordinal comparers do, and so do the
default one and the culture-aware ones. What throws is a hand-written comparer that does not
implement it, which is the case to watch for.

`StringComparer.Ordinal` also gets the dictionary a non-randomized hashing path, though the default
comparer gets the same one, so that is a reason to avoid the culture-aware comparers rather than a
reason to pass anything at all.

The first arm is
[CA1854](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1854),
"Prefer the 'IDictionary.TryGetValue(TKey, out TValue)' method", enabled by default as a suggestion.
Its stated cause is "an IDictionary element access that's guarded by an IDictionary.ContainsKey
check", counting two lookups. The arm here does three, since it reads and writes through the indexer,
so the gap is larger than the rule's own description implies.

### Caveats

* `_counts.Clear()` runs inside every arm and is measured. After the first iteration the capacity is
  fixed at around 60 entries, so it is tens of nanoseconds against 15 to 37 microseconds of work, and
  it is identical across arms.
* Every key is 10 characters with a shared prefix, so each comparison that reaches an entry runs the
  full length. That is the same for all four arms, but it means these numbers do not transfer to keys
  that differ in the first character.
* Under Server GC the `Gen0` column can read low while `Allocated` is accurate. The allocation claim
  above is from `Allocated`.
* Standard deviations run from 5.9% to 13.2%, above what the other sections here publish. Fifteen
  launches did not bring them down, which points at the allocation rather than at run-to-run variance.
  The 29% and 2x differences survive that comfortably. The 5% one does not, and is reported as
  unresolved rather than as a result.
* Apple M1, macOS 15.7.3, .NET 10.0.100, Server GC, fifteen process launches, thirty iterations.

Full report: [`results/DictionaryLookupCount.md`](results/DictionaryLookupCount.md)
Source: [`src/Patterns/DictionaryLookupCount.cs`](src/Patterns/DictionaryLookupCount.cs)

## License

MIT. See [LICENSE](LICENSE).
