```

BenchmarkDotNet v0.15.8, macOS Sequoia 15.7.3 (24G419) [Darwin 24.6.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.100
  [Host]    : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  Server=True  
LaunchCount=3  

```
| Method                 | ReadingCount | Mean        | Error     | StdDev      | Median      | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |------------- |------------:|----------:|------------:|------------:|-------:|--------:|-------:|----------:|------------:|
| **Cached**                 | **1**            |    **223.1 ns** |   **5.24 ns** |    **16.85 ns** |    **216.7 ns** |   **1.01** |    **0.10** | **0.0081** |     **512 B** |        **1.00** |
| PerCall                | 1            |    596.0 ns |  16.40 ns |    81.77 ns |    578.4 ns |   2.69 |    0.41 | 0.0095 |     718 B |        1.40 |
| Copied                 | 1            |    546.1 ns |  10.67 ns |    52.08 ns |    538.8 ns |   2.46 |    0.29 | 0.0076 |     718 B |        1.40 |
| PerCallSharedConverter | 1            |    637.6 ns |  16.55 ns |    75.10 ns |    612.2 ns |   2.87 |    0.39 | 0.0114 |     830 B |        1.62 |
| PerCallNewConverter    | 1            | 26,057.5 ns | 974.54 ns | 4,200.00 ns | 23,838.3 ns | 117.42 |   20.55 | 0.2441 |   20671 B |       40.37 |
|                        |              |             |           |             |             |        |         |        |           |             |
| **Cached**                 | **50**           |  **8,649.9 ns** | **117.51 ns** |   **493.67 ns** |  **8,468.1 ns** |   **1.00** |    **0.08** | **0.1373** |    **8984 B** |        **1.00** |
| PerCall                | 50           |  8,984.4 ns | 120.32 ns |   407.37 ns |  8,843.9 ns |   1.04 |    0.07 | 0.1373 |    9186 B |        1.02 |
| Copied                 | 50           |  9,617.7 ns | 393.38 ns | 1,337.24 ns |  9,063.8 ns |   1.12 |    0.17 | 0.1373 |    9186 B |        1.02 |
| PerCallSharedConverter | 50           |  8,421.1 ns |  87.38 ns |   204.24 ns |  8,348.5 ns |   0.98 |    0.06 | 0.1373 |    9106 B |        1.01 |
| PerCallNewConverter    | 50           | 31,209.8 ns | 520.61 ns | 1,559.29 ns | 30,852.9 ns |   3.62 |    0.26 | 0.3662 |   28955 B |        3.22 |
