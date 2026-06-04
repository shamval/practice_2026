```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8457/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 5800U with Radeon Graphics 1.90GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 8.0.27 (8.0.27, 8.0.2726.22922), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 8.0.27 (8.0.27, 8.0.2726.22922), X64 RyuJIT x86-64-v3


```
| Method        | Mean     | Error    | StdDev   | Ratio | RatioSD |
|-------------- |---------:|---------:|---------:|------:|--------:|
| BruteForce    | 589.7 μs |  7.92 μs |  7.41 μs |  1.00 |    0.02 |
| PartialSelect | 901.5 μs | 17.93 μs | 49.69 μs |  1.53 |    0.09 |
| GridExpansion | 182.0 μs |  5.49 μs | 14.93 μs |  0.31 |    0.03 |
