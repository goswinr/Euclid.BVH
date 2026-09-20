# Portable optimization experiments

This harness compares this repository before and after an optimization, not C-dev3's .NET-only implementation. The same F# workload runs on .NET and Fable/Node. See [OPTIMIZATIONS.md](../OPTIMIZATIONS.md) for the recorded findings and the decision about what to retain.

## Reproduce

From the repository root, with .NET 10, the repository's local Fable tool and Node installed:

```pwsh
./Test/Bench-C-dev3/Portable/prepare.ps1
node Test/Bench-C-dev3/Portable/compare.mjs dotnet 2
node Test/Bench-C-dev3/Portable/compare.mjs js 2
node Test/Bench-C-dev3/Portable/compare.mjs js 3 optimized --build-only
```

Preparation archives release `0.2.0` into an ignored directory, copies the current harness into it, and compiles independent baseline and current artifacts. It does not check out or alter the working tree. It refuses to overwrite an existing source snapshot. Do not run benchmarks concurrently with each other or with builds. Recorded CSV files in `../measurements` are replaced by rerunning the same variant/repeat.

Summarize saved measurements without rerunning:

```pwsh
node Test/Bench-C-dev3/Portable/compare.mjs dotnet 2 optimized --report-only
node Test/Bench-C-dev3/Portable/compare.mjs js 2 optimized --report-only
node Test/Bench-C-dev3/Portable/compare.mjs dotnet 2 predicates --report-only
node Test/Bench-C-dev3/Portable/compare.mjs js 2 predicates --report-only
node Test/Bench-C-dev3/Portable/compare.mjs js 3 optimized --build-only --report-only
```

The `predicates` experiment adds zero-tolerance containment/overlap traversals to the retained scalar builder. Its source is preserved as `zero-tolerance.patch`, applicable to the retained implementation with `git apply`. To repeat that experiment, apply it in a disposable checkout, build into `dist/predicates-dotnet` and `dist/predicates-js` using the same publish/Fable commands from `prepare.ps1`, then pass `predicates` instead of `optimized` to `compare.mjs`. The patch is experimental, not part of the library build.

## Method

- 2D rectangles and 3D boxes; 10,000 and 100,000 items; uniform and clustered input; default leaf size 4.
- Identical seeded generator across runtimes, with double-precision coordinates. Half of 4,096 point probes are inside an input item; the other half span the full domain. Range probes extend one unit around each point.
- Timed operations: build from precomputed bounds, point queries, and rectangle/box queries. Query tolerances are zero and 0.5; every query materializes a result list.
- Each case warms for at least 350 ms, calibrates a batch to at least 40 ms, and takes seven samples. GC is forced outside the timer before each sample. .NET tiered compilation is disabled in the project so measured batches do not mix JIT tiers. Node runs with `--expose-gc`.
- The full sweep uses two fresh-process repeats, baseline/current then current/baseline. Reports take the median of the run medians (the average for two repeats), then compute geometric means of baseline/current ratios across sizes and distributions. These are descriptive measurements, not confidence intervals.
- The build-only confirmation uses three repeats (baseline/current, current/baseline, baseline/current), skipping query verification/timing while retaining the same input generation and initial tree construction. Its eight cases run much faster than the full sweep. Results live under `measurements/optimized-build`; full-sweep and rejected-candidate results are preserved separately.
- The first 32 queries per dataset/tolerance are checked against independent linear scans. Checksums are consumed during timing and checked between variants. The library's full tests provide additional correctness coverage.
- Raw CSV times are nanoseconds per tree build or per query. `allocated_bytes` is .NET allocation per operation, not retained heap size; `-1` means unavailable on JavaScript. No JavaScript allocation reduction is claimed from these timing measurements.
- Generated code, binaries and source snapshots live under ignored `dist/`. Only source, the experiment patch and CSV evidence should be committed.
