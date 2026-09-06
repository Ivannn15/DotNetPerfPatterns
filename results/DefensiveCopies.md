```

BenchmarkDotNet v0.15.8, macOS Sequoia 15.7.3 (24G419) [Darwin 24.6.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.100
  [Host]    : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  Server=True  
IterationCount=30  LaunchCount=15  WarmupCount=15  

```
| Method                       | WindowCount | Mean     | Error     | StdDev    | Median   | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------------------- |------------ |---------:|----------:|----------:|---------:|------:|--------:|----------:|------------:|
| ReadonlyStructIn             | 1000        | 2.125 μs | 0.0034 μs | 0.0201 μs | 2.120 μs |  1.00 |    0.01 |         - |          NA |
| MutableIn                    | 1000        | 2.121 μs | 0.0127 μs | 0.0756 μs | 2.096 μs |  1.00 |    0.04 |         - |          NA |
| ReadonlyMembersIn            | 1000        | 2.110 μs | 0.0052 μs | 0.0313 μs | 2.102 μs |  0.99 |    0.02 |         - |          NA |
| MutableByValue               | 1000        | 2.108 μs | 0.0021 μs | 0.0124 μs | 2.105 μs |  0.99 |    0.01 |         - |          NA |
| ReadonlyStructSeparateMember | 1000        | 1.262 μs | 0.0023 μs | 0.0140 μs | 1.259 μs |  0.59 |    0.01 |         - |          NA |
| MutableSeparateMember        | 1000        | 1.967 μs | 0.0094 μs | 0.0588 μs | 1.958 μs |  0.93 |    0.03 |         - |          NA |
