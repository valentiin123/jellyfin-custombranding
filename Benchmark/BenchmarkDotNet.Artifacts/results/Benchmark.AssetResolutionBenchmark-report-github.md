```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3


```
| Method             | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------------------- |---------:|---------:|---------:|-------:|----------:|
| Original_NonAsset  | 43.63 ns | 0.553 ns | 0.431 ns | 0.0013 |      32 B |
| Optimized_NonAsset | 14.45 ns | 0.084 ns | 0.066 ns |      - |         - |
| Original_Asset     | 66.72 ns | 0.471 ns | 0.441 ns | 0.0026 |      64 B |
| Optimized_Asset    | 67.23 ns | 0.376 ns | 0.333 ns | 0.0026 |      64 B |
