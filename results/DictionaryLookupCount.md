```

BenchmarkDotNet v0.15.8, macOS Sequoia 15.7.3 (24G419) [Darwin 24.6.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.100
  [Host]    : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  Server=True  
IterationCount=30  LaunchCount=15  WarmupCount=15  

```
| Method                 | TokenCount | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |----------- |---------:|---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| ContainsKeyThenIndexer | 1000       | 38.44 μs | 0.590 μs | 3.587 μs | 37.33 μs |  1.29 |    0.14 | 0.7324 |  46.88 KB |        1.00 |
| TryGetValueThenIndexer | 1000       | 29.82 μs | 0.290 μs | 1.748 μs | 29.52 μs |  1.00 |    0.08 | 0.7324 |  46.88 KB |        1.00 |
| ValueRef               | 1000       | 28.14 μs | 0.405 μs | 2.490 μs | 27.69 μs |  0.95 |    0.10 | 0.7324 |  46.88 KB |        1.00 |
| AlternateLookup        | 1000       | 16.05 μs | 0.350 μs | 2.115 μs | 15.11 μs |  0.54 |    0.08 | 0.0458 |   2.81 KB |        0.06 |
