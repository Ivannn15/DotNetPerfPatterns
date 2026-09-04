```

BenchmarkDotNet v0.15.8, macOS Sequoia 15.7.3 (24G419) [Darwin 24.6.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.100
  [Host]    : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  Server=True  
LaunchCount=5  

```
| Method      | PayloadLength | Mean       | Error     | StdDev     | Median     | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |-------------- |-----------:|----------:|-----------:|-----------:|------:|--------:|----------:|------------:|
| **Cached**      | **128**           |   **7.494 ns** | **0.2183 ns** |  **0.9867 ns** |   **7.281 ns** |  **1.02** |    **0.18** |         **-** |          **NA** |
| CachedArray | 128           |  16.699 ns | 0.3740 ns |  1.6160 ns |  16.097 ns |  2.26 |    0.34 |         - |          NA |
| InlineArray | 128           |  16.299 ns | 0.2383 ns |  0.8782 ns |  16.075 ns |  2.21 |    0.28 |         - |          NA |
|             |               |            |           |            |            |       |         |           |             |
| **Cached**      | **512**           |  **24.796 ns** | **0.2373 ns** |  **0.6495 ns** |  **24.586 ns** |  **1.00** |    **0.04** |         **-** |          **NA** |
| CachedArray | 512           |  32.488 ns | 0.1736 ns |  0.4193 ns |  32.362 ns |  1.31 |    0.04 |         - |          NA |
| InlineArray | 512           |  32.894 ns | 0.2364 ns |  0.5799 ns |  32.796 ns |  1.33 |    0.04 |         - |          NA |
|             |               |            |           |            |            |       |         |           |             |
| **Cached**      | **1024**          |  **49.728 ns** | **0.0939 ns** |  **0.2250 ns** |  **49.708 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| CachedArray | 1024          |  57.962 ns | 0.2536 ns |  0.6027 ns |  57.844 ns |  1.17 |    0.01 |         - |          NA |
| InlineArray | 1024          |  58.759 ns | 0.3806 ns |  0.9046 ns |  58.529 ns |  1.18 |    0.02 |         - |          NA |
|             |               |            |           |            |            |       |         |           |             |
| **Cached**      | **4096**          | **194.063 ns** | **0.5557 ns** |  **1.3313 ns** | **193.763 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| CachedArray | 4096          | 215.817 ns | 2.5821 ns | 11.0728 ns | 213.912 ns |  1.11 |    0.06 |         - |          NA |
| InlineArray | 4096          | 215.639 ns | 2.4243 ns |  9.6091 ns | 213.615 ns |  1.11 |    0.05 |         - |          NA |
