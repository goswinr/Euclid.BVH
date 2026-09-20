# Euclid.BVH versus C-dev3/BVH2D

This benchmark compares this repository's `Euclid.BVH2D` with [C-dev3/BVH2D](https://github.com/C-dev3/BVH2D) on their directly shared operation: point-containment queries over axis-aligned rectangles.

The benchmark pins the released `BVH2D` NuGet package at `1.0.0`. Its library source is identical to repository master commit `37f9d35`; that commit only changes package file layout and README content relative to tag `v1.0.0`.

## Method

- Both implementations receive geometrically identical deterministic rectangles and points.
- Construction starts from precomputed rectangle bounds; input conversion is outside the timed region.
- Point queries use the public APIs that materialize a result list: `ItemsNearPoint(point, 0.0)` and `QueryPoint(point)`.
- Every returned index set is compared before timings are reported.
- The benchmark warms JIT and shared pool state, forces a full collection before each sample, and reports medians.
- Euclid uses double-precision coordinates and its default leaf size of 4. C-dev3 uses single-precision coordinates and fixed one-item leaves.
- Allocation figures come from `GC.GetAllocatedBytesForCurrentThread` and cover the measured operation, not retained heap size.

Run the full comparison from the repository root:

```pwsh
dotnet run --project Test/Bench-C-dev3/Bench-C-dev3.fsproj --configuration Release
```

For a faster smoke run:

```pwsh
dotnet run --project Test/Bench-C-dev3/Bench-C-dev3.fsproj --configuration Release -- --quick
```

Machine-specific results belong in `RESULTS.md`; rerun the benchmark before drawing conclusions on another runtime or processor.

## Follow-up optimization experiments

See [OPTIMIZATIONS.md](OPTIMIZATIONS.md) for before/after measurements of changes inspired by this comparison, on both .NET and Fable/Node. The [portable harness](Portable/README.md) uses longer warmup, calibrated batches, alternating process order and disabled .NET tiered compilation. The original comparison above does not disable tiering; its small-input timing conclusions need that caveat.
