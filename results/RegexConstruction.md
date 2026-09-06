```

BenchmarkDotNet v0.15.8, macOS Sequoia 15.7.3 (24G419) [Darwin 24.6.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.100
  [Host]    : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  Server=True  
IterationCount=15  LaunchCount=5  WarmupCount=6  

```
| Method         | InputCount | Mean          | Error        | StdDev        | Median        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------- |----------- |--------------:|-------------:|--------------:|--------------:|------:|--------:|-------:|----------:|------------:|
| **Generated**      | **1**          |      **28.13 ns** |     **1.488 ns** |      **3.449 ns** |      **26.75 ns** |  **1.01** |    **0.16** |      **-** |         **-** |          **NA** |
| NewPerCall     | 1          |   1,357.87 ns |    51.571 ns |    121.558 ns |   1,321.91 ns | 48.86 |    6.53 | 0.0534 |    3672 B |          NA |
| StaticMethod   | 1          |      80.96 ns |     1.849 ns |      4.358 ns |      79.44 ns |  2.91 |    0.33 |      - |         - |          NA |
| CachedInstance | 1          |      84.84 ns |     5.893 ns |     14.006 ns |      76.96 ns |  3.05 |    0.59 |      - |         - |          NA |
| CachedCompiled | 1          |      25.78 ns |     0.821 ns |      1.918 ns |      25.28 ns |  0.93 |    0.12 |      - |         - |          NA |
|                |            |               |              |               |               |       |         |        |           |             |
| **Generated**      | **100**        |   **2,414.13 ns** |    **13.632 ns** |     **32.131 ns** |   **2,409.92 ns** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| NewPerCall     | 100        | 136,603.62 ns | 4,953.102 ns | 12,056.536 ns | 129,787.91 ns | 56.59 |    5.02 | 5.8594 |  366080 B |          NA |
| StaticMethod   | 100        |   8,426.06 ns |    58.580 ns |    139.221 ns |   8,397.82 ns |  3.49 |    0.07 |      - |         - |          NA |
| CachedInstance | 100        |   8,349.30 ns |    85.922 ns |    199.136 ns |   8,302.07 ns |  3.46 |    0.09 |      - |         - |          NA |
| CachedCompiled | 100        |   2,800.70 ns |    22.384 ns |     52.322 ns |   2,783.04 ns |  1.16 |    0.03 |      - |         - |          NA |
