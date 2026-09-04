```

BenchmarkDotNet v0.15.8, macOS Sequoia 15.7.3 (24G419) [Darwin 24.6.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.100
  [Host]    : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  Server=True  
IterationCount=20  LaunchCount=9  WarmupCount=10  

```
| Method      | PayloadLength | Mean       | Error     | StdDev     | Median     | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |-------------- |-----------:|----------:|-----------:|-----------:|------:|--------:|----------:|------------:|
| **Cached**      | **128**           |   **7.046 ns** | **0.0872 ns** |  **0.3279 ns** |   **6.908 ns** |  **1.00** |    **0.06** |         **-** |          **NA** |
| CachedArray | 128           |  15.156 ns | 0.0353 ns |  0.1302 ns |  15.134 ns |  2.16 |    0.09 |         - |          NA |
| InlineArray | 128           |  15.029 ns | 0.0236 ns |  0.0886 ns |  15.011 ns |  2.14 |    0.09 |         - |          NA |
|             |               |            |           |            |            |       |         |           |             |
| **Cached**      | **512**           |  **24.346 ns** | **0.0905 ns** |  **0.3325 ns** |  **24.228 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| CachedArray | 512           |  32.391 ns | 0.1023 ns |  0.3772 ns |  32.297 ns |  1.33 |    0.02 |         - |          NA |
| InlineArray | 512           |  32.333 ns | 0.0572 ns |  0.2122 ns |  32.286 ns |  1.33 |    0.02 |         - |          NA |
|             |               |            |           |            |            |       |         |           |             |
| **Cached**      | **1024**          |  **50.797 ns** | **0.1159 ns** |  **0.4288 ns** |  **50.724 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| CachedArray | 1024          |  57.960 ns | 0.0823 ns |  0.3113 ns |  57.880 ns |  1.14 |    0.01 |         - |          NA |
| InlineArray | 1024          |  58.232 ns | 0.2318 ns |  0.8543 ns |  58.096 ns |  1.15 |    0.02 |         - |          NA |
|             |               |            |           |            |            |       |         |           |             |
| **Cached**      | **4096**          | **197.212 ns** | **2.7023 ns** | **10.1619 ns** | **194.392 ns** |  **1.00** |    **0.07** |         **-** |          **NA** |
| CachedArray | 4096          | 202.180 ns | 0.6177 ns |  2.3075 ns | 201.476 ns |  1.03 |    0.04 |         - |          NA |
| InlineArray | 4096          | 201.762 ns | 0.6360 ns |  2.3524 ns | 201.034 ns |  1.03 |    0.04 |         - |          NA |
