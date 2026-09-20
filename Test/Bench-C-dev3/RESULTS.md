# Results: Euclid.BVH versus C-dev3/BVH2D

**Historical run, with a JIT-tiering caveat:** these original timings left .NET tiered compilation enabled and used short warmup. In particular, the apparent 2.6x small-tree build advantage below was not reproduced with tiering disabled; do not treat it as an algorithmic advantage. The follow-up [optimization measurements](OPTIMIZATIONS.md) use independent Release artifacts, disabled tiering, calibrated batches and reversed run order. The tables below are retained as the original observation, not as corrected steady-state results.

Measured on 2026-09-20 with:

- AMD Ryzen 5 9600X 6-Core Processor, 12 logical processors
- Windows 10.0.26200
- .NET 10.0.12, Release configuration
- Euclid.BVH 0.2.0 at commit `efa9ec7`
- C-dev3/BVH2D NuGet 1.0.0, whose library source matches repository master `37f9d35`

Each value is the median of warmed in-process samples. All 20,000 point-query result sets per dataset were checked for equality before measurement.

## Build

| Implementation | Rectangles | Median ms | Allocated MiB/build |
| --- | ---: | ---: | ---: |
| Euclid.BVH2D | 1,000 | 0.461 | 0.066 |
| C-dev3/BVH2D | 1,000 | 0.180 | 0.092 |
| Euclid.BVH2D | 10,000 | 1.815 | 0.773 |
| C-dev3/BVH2D | 10,000 | 1.839 | 0.954 |
| Euclid.BVH2D | 100,000 | 20.478 | 7.196 |
| C-dev3/BVH2D | 100,000 | 21.355 | 9.537 |

C-dev3 builds the 1,000-rectangle case about 2.6 times faster. At 10,000 rectangles the timings are effectively tied, and at 100,000 rectangles Euclid is about 4% faster. Euclid allocates 19–28% less during construction across these sizes.

## Point containment queries

Both implementations materialize a fresh result list for each query.

| Implementation | Rectangles | Median ns/query | Allocated B/query |
| --- | ---: | ---: | ---: |
| Euclid.BVH2D | 1,000 | 141.3 | 56.4 |
| C-dev3/BVH2D | 1,000 | 100.3 | 400.0 |
| Euclid.BVH2D | 10,000 | 187.5 | 56.5 |
| C-dev3/BVH2D | 10,000 | 147.0 | 400.0 |
| Euclid.BVH2D | 100,000 | 272.4 | 56.4 |
| C-dev3/BVH2D | 100,000 | 228.2 | 400.0 |

C-dev3 answers this workload about 1.2–1.4 times faster. Euclid allocates about 7.1 times less per query (roughly 86% less).

## Scope and interpretation

- This is a focused comparison of the one directly shared query: finding rectangles containing a point.
- Euclid uses double-precision coordinates and its default leaf size of 4; C-dev3 uses single precision and fixed one-item leaves.
- The generated domain grows with the square root of the rectangle count, keeping expected overlap density roughly constant. Half the probes are generated inside an input rectangle.
- C-dev3 exposes an iterator and span overload as well, but `QueryPoint(point)` is used here because it has the same materialized-list result shape as Euclid's `ItemsNearPoint(point, 0.0)`.
- Euclid additionally supports nearest-item, nearest-neighbor, closest-pair, distance-ordered, rectangle-range, and close-pair queries; C-dev3/BVH2D does not expose equivalent operations, so they are outside this comparison.
- Timing results are machine-specific. Rerun the benchmark before using the absolute numbers for capacity planning.
