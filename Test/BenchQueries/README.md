# Query benchmark: before and after an optimization

This harness times every query of `BVH` and `BVH2D` that has a result to compare, on .NET and on Fable JavaScript (Node.js), for a committed revision (`baseline`) and for the working tree (`optimized`).
The same F# program runs in both variants and on both runtimes, so only the library differs.

## Reproduce

From the repository root, with .NET 10, the repository's local Fable tool and Node.js installed:

```pwsh
./Test/BenchQueries/prepare.ps1                      # baseline from HEAD, optimized from the working tree
node Test/BenchQueries/compare.mjs dotnet 3
node Test/BenchQueries/compare.mjs js 3
```

- `prepare.ps1 -BaselineRevision <rev>` compares against another revision. The baseline is a `git archive` of that revision under the ignored `dist/` folder; the working tree is never checked out or changed.
- `prepare.ps1 -Variants optimized` rebuilds only the working tree variant, any other name than `baseline` builds the working tree into `dist/<name>-dotnet` and `dist/<name>-js`.
- `compare.mjs <runtime> <repeats> --only=build,closest-pair-box` runs only the named operations, `--baseline=<name> --candidate=<name>` compares two other prepared variants, `--smoke` runs a small and quick input, and `--report-only` summarizes saved CSV files without running anything.
- Do not run benchmarks concurrently with each other or with builds.

## Method

- Input: random line segments of length 1 to 5 in a random direction, uniform or in two clusters, 10,000 and 100,000 of them, in 2D and 3D, default leaf size 4. The generator is seeded and identical on both runtimes.
- The box operations use a tree built from the bounding boxes of the segments (`BVH.createFromBoxes`, `BVH2D.createFromRects`). The line operations use `BVHLine3D` and `BVHLine2D`, whose exact distance between two segments is `XLine3D.getSqDistance` or `XLine2D.getSqDistance`, and between a point and a segment `SqDistanceToPnt` or `SqDistanceToPt`.
- Point and line queries: 4,096 of them, half on an input segment and half anywhere in the domain. Times are per query. The `-all` operations enumerate the whole tree for 4 queries; their time is per query too.
- Each case warms up for at least 350 ms, calibrates a batch to at least 40 ms starting from a single call, then takes 7 samples, or 3 if a batch takes over 300 ms. GC is forced outside the timer before each sample. .NET runs with tiered compilation disabled, Node.js with `--expose-gc`.
- Each repeat starts a fresh process per variant. The order alternates, baseline first in odd repeats. The report takes the median of the repeats' medians and the geometric mean of the speedups across distributions and sizes.
- Every case prints a checksum built from distances, pair counts and pair indices, never from the index of a closest item, because ties may resolve differently. `compare.mjs` fails if the checksums of the variants differ, so both variants are checked to return the same results on all inputs.
- `allocated_bytes` is the .NET allocation per operation; it is not available on JavaScript.

## Results

Measured on 2026-10-25 against `846039c`, on an AMD Ryzen 5 9600X with Windows 11 (10.0.26300), .NET 10.0.13, Fable 5.24.0 and Node.js 24.7.0.
The changes measured are those of the `[Unreleased]` section of the changelog: a bounded `ClosestPair`, a bounding box check before the exact distance in `ClosePairs` and in the searches from a query box, each node distance computed once, the `...ByDistance` traversal without a tuple per heap entry and outside the sequence expression, and node bounds built from the children.

Speedup is baseline time divided by optimized time, so above 1 is faster. Each cell is the geometric mean over the uniform and clustered input at 10,000 and 100,000 items, with the range of the four cases in brackets.

| Operation | .NET 2D | .NET 3D | JS 2D | JS 3D |
| --- | ---: | ---: | ---: | ---: |
| `build` | 1.03 (1.03–1.04) | 1.05 (1.03–1.09) | 1.04 (1.03–1.08) | 1.06 (1.04–1.08) |
| `closest-box-pt` | 1.22 (1.17–1.25) | 1.22 (1.20–1.27) | 1.15 (1.09–1.21) | 1.12 (1.07–1.18) |
| `closest-line-pt` | 1.13 (1.12–1.14) | 1.28 (1.26–1.31) | 1.18 (1.11–1.28) | 1.18 (1.14–1.21) |
| `closest-line-line` | 1.18 (1.15–1.21) | 1.23 (1.21–1.25) | 1.07 (1.01–1.14) | 1.14 (1.07–1.21) |
| `nearest-neighbors-box` | 1.19 (1.17–1.22) | 1.27 (1.24–1.29) | 1.12 (1.04–1.25) | 1.10 (1.04–1.16) |
| `nearest-neighbors-line` | 1.19 (1.15–1.21) | 1.25 (1.19–1.31) | 1.04 (0.96–1.12) | 1.11 (1.06–1.20) |
| `closest-pair-box` | 52,797 (2,143–344,393) | 47,783 (10,494–221,964) | 46,283 (1,710–321,021) | 32,426 (5,298–172,500) |
| `closest-pair-line` | 2.15 (1.94–2.37) | 3.40 (3.04–3.86) | 2.06 (1.80–2.39) | 3.27 (2.74–3.86) |
| `close-pairs-box-0` | 1.20 (1.16–1.22) | 1.15 (1.12–1.17) | 1.08 (1.02–1.19) | 1.03 (1.02–1.05) |
| `close-pairs-box-0.5` | 1.18 (1.14–1.20) | 1.14 (1.11–1.16) | 1.03 (1.01–1.06) | 1.08 (1.03–1.13) |
| `close-pairs-line-0` | 2.17 (1.96–2.41) | 2.01 (1.95–2.12) | 1.21 (1.08–1.58) | 1.27 (1.24–1.29) |
| `close-pairs-line-0.5` | 1.94 (1.64–2.27) | 1.90 (1.81–2.00) | 1.18 (1.07–1.51) | 1.22 (1.16–1.26) |
| `boxes-by-distance-10` | 1.07 (1.04–1.10) | 1.04 (1.02–1.06) | 1.29 (1.24–1.34) | 1.27 (1.24–1.32) |
| `lines-by-distance-10` | 1.02 (1.00–1.04) | 1.01 (0.99–1.02) | 1.30 (1.25–1.35) | 1.30 (1.25–1.34) |
| `lines-by-line-distance-10` | 1.02 (1.01–1.03) | 1.02 (1.01–1.03) | 1.29 (1.25–1.33) | 1.30 (1.26–1.34) |
| `boxes-by-distance-all` | 1.07 (1.05–1.08) | 1.05 (1.04–1.06) | 1.32 (1.18–1.60) | 1.24 (1.19–1.34) |
| `lines-by-distance-all` | 1.00 (1.00–1.01) | 1.02 (1.02–1.02) | 1.33 (1.20–1.59) | 1.26 (1.20–1.31) |
| `lines-by-line-distance-all` | 1.01 (1.01–1.01) | 1.02 (1.00–1.04) | 1.30 (1.15–1.53) | 1.24 (1.17–1.32) |

- `closest-pair-box` is not a typical speedup. In this input many segment boxes overlap, and the new box overload returns at the first pair of touching boxes, while the old one searched from every item. Without touching boxes, expect a gain like that of `closest-pair-line`, which comes from bounding each search by the closest pair found so far.
- The four rows `closest-line-pt`, `closest-line-line`, `nearest-neighbors-line` and `closest-pair-line` come from a second run of only these operations, see the last experiment below. All other rows come from the full sweep in [measurements/optimized](measurements/optimized); its code differs from the final one only in `ClosestItem` from a point, which only `closest-line-pt` uses.
- JavaScript times at 100,000 items vary by up to about 30% between sessions, also for the unchanged baseline. Compare the variants of one run, not the absolute times of different runs.
- On .NET the `...ByDistance` sequences allocate about 70% less, for example 3.2 MB instead of 11.1 MB for a full enumeration of 100,000 items, and every search inside `NearestNeighbors` and `ClosestPair` allocates one tuple less. The other allocations are unchanged.

## Experiments that were not kept

Each was measured against the other changes as they were at that point, with 3 repeats on both runtimes, by building the working tree with one change reverted as an extra variant (`prepare.ps1 -Variants <name>`, `compare.mjs --baseline=optimized --candidate=<name>`). The raw files are in the `measurements/optimized-vs-*` folders.

- Lazy exact distances in `ItemsByDistance`: an item was first keyed by its bounding box distance and its exact distance evaluated only when it reached the top of the heap. It was slower everywhere: evaluating the items of an expanded leaf right away was 1.04 to 1.20 times faster for the first 10 items, and 1.2 to 1.6 times faster for a full enumeration, even with the line to line distance.
- In `ClosePairs`, splitting the bigger of two internal nodes instead of always the first one: within about 3% either way, slightly worse on JavaScript in 2D, so the simpler rule stayed.
- Checking the bounding box of an item before its exact distance in `ClosestItem` from a point: without the check, `closest-line-pt` was 1.15 to 1.25 times faster on JavaScript and 1.18 times faster on .NET in 3D. The distance from a point to a segment is cheap, and on JavaScript each box is a separate object, so the extra memory access cost more than the check saved. For the searches from a query box, `ClosestPair` and `NearestNeighbors`, which measure between two geometries, the check stayed: without it they were 5 to 11% slower on .NET and about even on JavaScript.
