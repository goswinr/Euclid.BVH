# Measured optimization experiments

Measured on 2026-09-20. Baseline: release `0.2.0`, commit `efa9ec7768ab5d6653b5400167fc150b446a0fc8`.

The two experiments suggested by the C-dev3 comparison were scalar bounds accumulation during construction and direct containment/overlap predicates for zero-tolerance queries. These measurements compare Euclid with itself; they are not new Euclid-versus-C-dev3 timings.

## Decision

Retain scalar bounds accumulation in both builders. Do not retain the zero-tolerance query specialization: it improved some JavaScript queries but regressed .NET queries and some positive-tolerance JavaScript queries. Public APIs, the median/center-spread splitting strategy, and query implementations remain unchanged.

Both changes use ordinary F# and are Fable compatible. The retained change constructs a final `BRect`/`BBox` per node instead of repeatedly constructing union values inside the bounds loops. Inspecting the generated JavaScript confirmed that the constructors moved outside those loops. .NET already represents those bounds as structs, so a JavaScript benefit need not translate into a .NET benefit.

## Retained change: scalar bounds accumulation

The .NET full sweep was essentially flat: **0.988x** build speed in 2D and **1.032x** in 3D, averaged geometrically across sizes/distributions. Allocation was identical before/after: 810,320 / 7,545,872 bytes per 2D build and 1,093,696 / 10,194,432 bytes per 3D build at 10k / 100k items, respectively. No .NET allocation or substantial speed improvement is claimed.

The JavaScript full sweep became noisy, even in unchanged query code. One 2D clustered 100k baseline block slowed markedly, inflating the aggregate build speedup to 1.166x in 2D (3D was 1.081x). **Do not use that 1.166x figure as the expected gain.** The raw results remain in [measurements/optimized](measurements/optimized), including all outliers; no samples were silently discarded.

A separate, shorter, three-repeat build-only confirmation follows. It retains the same inputs, warmup, sample calibration and GC policy, but skips the long query sweep. Its median-of-three run medians is less sensitive to a single slow run.

| Fable/Node | Distribution | Items | Baseline ms/build | Scalar ms/build | Speedup |
| --- | --- | ---: | ---: | ---: | ---: |
| 2D | Uniform | 10,000 | 8.506 | 8.068 | 1.054x |
| 2D | Uniform | 100,000 | 131.044 | 119.116 | 1.100x |
| 2D | Clustered | 10,000 | 8.908 | 8.245 | 1.080x |
| 2D | Clustered | 100,000 | 133.319 | 118.835 | 1.122x |
| 3D | Uniform | 10,000 | 9.781 | 8.901 | 1.099x |
| 3D | Uniform | 100,000 | 153.273 | 136.877 | 1.120x |
| 3D | Clustered | 10,000 | 9.928 | 9.206 | 1.078x |
| 3D | Clustered | 100,000 | 153.983 | 139.451 | 1.104x |

Geometric mean speedup: **1.089x in 2D** and **1.100x in 3D**. All eight build-only cases improved, with a range of 1.054-1.122x. This supports retaining the scalar builder as a modest Fable improvement, not a transformative speedup. Absolute timings should not be compared between the full-sweep and build-only modes, which have different preceding workloads and runtime/GC histories. Raw confirmation measurements: [measurements/optimized-build](measurements/optimized-build).

## Rejected experiment: zero-tolerance query specialization

This candidate combined the scalar builder with separate `tolerance = 0.0` traversals using `ContainsPt`/`ContainsPnt` and `IsOverlapping`. Positive tolerances retained squared Euclidean distance, so a diagonal near-miss did not become a hit merely because it fit inside expanded axis bounds.

Speedup is baseline time divided by candidate time: **above 1 is faster**, below 1 is slower. Values below are geometric means over uniform/clustered distributions and 10k/100k items.

| Runtime | Dimensions | Point, zero tolerance | Range, zero tolerance | Point, positive tolerance | Range, positive tolerance |
| --- | --- | ---: | ---: | ---: | ---: |
| .NET | 2D | 0.956x | 0.949x | 0.999x | 0.976x |
| .NET | 3D | 0.958x | 0.911x | 1.017x | 0.977x |
| Fable/Node | 2D | 1.055x | 1.028x | 0.971x | 0.927x |
| Fable/Node | 3D | 1.050x | 1.059x | 0.951x | 0.968x |

Most .NET zero-tolerance cases took about 4-13% longer. JavaScript zero-tolerance queries improved by about 3-6% in aggregate, but positive-tolerance queries took about 3-8% longer in aggregate. There were larger individual outliers, so this is not a claim that every workload behaves that way. The positive-tolerance slowdown was observed despite keeping its distance arithmetic unchanged; the cause was not profiled.

The candidate is saved as [zero-tolerance.patch](Portable/zero-tolerance.patch), not compiled into the library. Its raw measurements are in [measurements/predicates](measurements/predicates). No SIMD, pooling, span APIs or runtime-specific library code were introduced.

## Method and limits

- AMD Ryzen 5 9600X, 6 cores / 12 logical processors; Windows 10.0.26200.
- .NET 10.0.12, Release, tiered compilation disabled; Fable 5.17.0; Node v24.7.0.
- 2D and 3D, 10k and 100k items, uniform and clustered distributions, default leaf size 4, 4,096 queries per batch. Build starts from precomputed bounds; queries return fresh result lists.
- Per case: at least 350 ms warmup, calibrated batches lasting at least 40 ms, seven timed samples, GC outside the timer. Full sweeps used two fresh-process runs per variant with reversed order; average of the two run medians, then geometric mean of speedup ratios. The JavaScript build-only confirmation used three repeats and the median of their medians. Benchmarks ran sequentially, without concurrent builds/tests; unrelated machine activity was not controlled.
- Sampled queries were checked against independent linear scans; baseline and candidate checksums agreed. The retained version also passed all 76 tests on .NET and on Fable JavaScript in both Release and Debug, plus the TypeScript compile check.
- Both library targets (`net6.0`, `net472`) built successfully, with no warnings. Runtime .NET measurements used .NET 10, not .NET Framework or .NET 6.
- These are exploratory measurements on one machine, not confidence intervals or a claim about every runtime, leaf size, coordinate scale or geometry distribution. Browser JavaScript engines and .NET tiered/PGO execution were not measured.
- No JavaScript byte-allocation metric was collected. Constructor removal is visible in generated code, but a specific allocated-byte or GC-time reduction is not claimed.

The [portable benchmark instructions](Portable/README.md) reproduce both runtimes and summarize saved CSV files. The original [C-dev3 comparison](RESULTS.md) is retained with a warning about its short warmup and JIT-tiering bias; its apparent small-tree build advantage should not be treated as an algorithmic result.
