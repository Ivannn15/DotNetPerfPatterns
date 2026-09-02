# .NET Performance Patterns

A set of reproducible BenchmarkDotNet benchmarks for performance patterns I keep running
into in production ASP.NET Core services.

Each pattern comes in two versions — the way it is usually written, and a fixed version —
so the cost is measurable rather than assumed. All data is synthetic and generated in
`GlobalSetup`.

## Running

```bash
cd src
dotnet run -c Release
```

Requires .NET 10 SDK.

## Patterns

Work in progress. Each pattern will be added as a separate benchmark.

## Results

Measured on my machine; absolute numbers will differ on yours, the relative differences
should not.
