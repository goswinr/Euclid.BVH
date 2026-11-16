# Polyline self intersection: brute force versus Euclid.BVH

This benchmark compares ways to find a self intersection in a `Polyline2D` and in a `Polyline3D`, on .NET and on Fable JavaScript.

| Method | Broad phase | Narrow phase |
| --- | --- | --- |
| `Polyline2D.tryFindSelfIntersection` (Euclid) | none, every pair of non adjacent segments, O(n²) | `XLineXY.tryIntersect` |
| `Polyline2D.tryFindSelfIntersectionWithBvh` (Euclid.BVH) | a `BVH2D` over the segment rectangles, and its dual tree traversal `ClosePairs`, about O(n log n) | `XLineXY.tryIntersect` |
| `Polyline3D.tryFindSelfIntersection` (Euclid) | none, every pair of non adjacent segments, O(n²) | `XLineXYZ.tryIntersect` |
| `Polyline3D.tryFindSelfIntersectionWithBvh` (Euclid.BVH) | a `BVH` over the segment boxes, and its dual tree traversal `ClosePairs`, about O(n log n) | `XLineXYZ.tryIntersect` |

The methods of one dimension use the same narrow phase with the same tolerances, so only the broad phase differs.
The two `...WithBvh` methods are part of the library, see [PolylineSelfIntersection.fs](../../Src/PolylineSelfIntersection.fs).
Before Euclid 1.0 there was no `Polyline3D.tryFindSelfIntersection`, so this benchmark used a copy of the 2D loops in 3D, and also measured `Polyline2D.tryFindSelfIntersectionWithBRect`, which Euclid 1.0 removed.

## Method

- The input is a closed ring with `PointCount` points, so `PointCount - 1` segments: in 2D a circle of radius 100, in 3D the same circle with a height that goes up and down three times around it.
- The rings have no self intersection. This is the worst case for all methods, because each of them has to test all of its candidate pairs.
- The BVH methods build their tree inside the measured call, so the build time is included.
- The Euclid methods stop at the first intersection they find, while `ClosePairs` always collects all of them. On a polyline that does intersect itself, the Euclid methods can return early and the BVH methods cannot.
- Before anything is timed, the program checks that all methods of a dimension return the same result for a ring and for a figure eight that crosses itself, at every point count.
- Euclid is the version that `Src/Euclid.BVH.fsproj` references, 1.0.0-alpha3 for the numbers below.

## .NET

Run it from the repository root, it takes a bit under two minutes:

```pwsh
dotnet run --project Test/BenchPolylineSelfIntersection/BenchPolylineSelfIntersection.fsproj --configuration Release
```

The arguments after `--` go to BenchmarkDotNet. To run only some of the benchmarks, use a filter, for example `-- --filter *3D*`.

- Before timing, the program calls all methods often on a small ring, so that .NET promotes them to fully optimized code. Without this, a call on 10,000 points takes so long that the benchmark makes fewer than the 30 calls needed for that promotion, and the plain 2D method was measured about 6 times too slow.
- To finish in under two minutes, the benchmarks run in process, with 2 warmup and 6 measured iterations of 120 ms each, and without BenchmarkDotNet's overhead stage. BenchmarkDotNet's default job would take over 20 minutes. For the 2D benchmarks it gave the same numbers within about 10% from 20 points on, but at 5 points this short job reads about 20% higher, probably because no call overhead is subtracted.

Medians, from BenchmarkDotNet 0.15.8 on .NET 10.0.15, Windows 11, AMD Ryzen 5 9600X.
A ratio below 1.00 is faster than the first method of the table.

**Polyline2D**:

| Points | `tryFindSelfIntersection` | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: |
| 5 | 20.9 ns | 97.1 ns | 4.66 |
| 20 | 237 ns | 727 ns | 3.08 |
| 50 | 1.49 µs | 2.00 µs | 1.34 |
| 100 | 6.05 µs | 4.50 µs | 0.74 |
| 200 | 24.8 µs | 9.73 µs | 0.39 |
| 500 | 157 µs | 26.4 µs | 0.17 |
| 1,000 | 614 µs | 59.3 µs | 0.10 |
| 2,000 | 2.43 ms | 135 µs | 0.06 |
| 10,000 | 60.1 ms | 1.24 ms | 0.02 |

**Polyline3D**:

| Points | `tryFindSelfIntersection` | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: |
| 5 | 18.6 ns | 157 ns | 8.42 |
| 20 | 1.27 µs | 812 ns | 0.64 |
| 50 | 9.52 µs | 2.28 µs | 0.24 |
| 100 | 40.2 µs | 5.10 µs | 0.13 |
| 200 | 163 µs | 11.0 µs | 0.07 |
| 500 | 1.02 ms | 30.9 µs | 0.03 |
| 1,000 | 4.07 ms | 68.6 µs | 0.02 |
| 2,000 | 16.4 ms | 184 µs | 0.01 |
| 10,000 | 410 ms | 1.43 ms | 0.003 |

- In 2D the BVH is 1.3 to 4.7 times slower than Euclid up to about 50 points, because building the tree costs more than it saves. It is faster from about 100 points on: 10 times at 1,000 points and 49 times at 10,000 points.
- In 3D, where each pair costs more to test, the BVH is faster from about 20 points on: 15 times at 200 points, 59 times at 1,000 points and 290 times at 10,000 points.
- In 2D the BVH allocates about 5 times as much as Euclid, from 552 B at 5 points to 851 KB at 10,000 points. In 3D Euclid allocates next to nothing, the BVH from 640 B to 1.1 MB.

These numbers are from a single run.

## Fable JavaScript

BenchmarkDotNet does not run on JavaScript, so the Fable build uses its own small timing loop in [Portable.fs](Portable.fs): it warms up each case for 300 ms, doubles the calls per batch until a batch takes 50 ms, and takes the median of 9 timed batches. Calls over half a second are timed only 3 times.

Compile it with Fable and run it with Node.js from the repository root. Compiling takes under a minute, running a few minutes:

```pwsh
dotnet tool restore
dotnet fable Test/BenchPolylineSelfIntersection/BenchPolylineSelfIntersection.fsproj --outDir Test/BenchPolylineSelfIntersection/dist --configuration Release --noCache
node Test/BenchPolylineSelfIntersection/dist/Program.js
```

The same timing loop also runs on .NET with `-- --portable`. There most rows agreed with BenchmarkDotNet within about 10%, some differed by 20 to 35%, and the 3D BVH at 10,000 points read 2.5 times slower. The crossover points were the same.

Two things in the timing loop matter on Node.js. It builds the ring before the timed calls: the call to time is a delegate, because Fable compiles a curried F# function in a record field into a single function, which rebuilt the ring in every call. And it forces no garbage collection before a batch, because that made the methods that allocate about twice as slow afterwards.

### Two results for each method

On Node.js the speed of a method can depend a lot on what else ran in the same process, so each method is measured twice:

1. **Alone:** each method at each point count in a fresh Node.js process of its own. This is the best case. The fresh processes run in the order of the point counts, all methods for each, so that a changing CPU clock affects all methods alike.
2. **Together:** all methods in one process, after the correctness check. This is closer to an application, which reads from many kinds of arrays.

The cause is in the generated code. Fable compiles every read from an array or `ResizeArray` to a call of fable-library's bounds checked `item(index, array)` function. V8 keeps one type feedback for that function, shared by all its callers. Once a process has read from arrays of several kinds (doubles, objects, typed arrays), that feedback is megamorphic, and every inlined array read in every method becomes a generic lookup.
In a Release build, the hot loops of Euclid.BVH read their arrays directly instead, with the `Arr` and `ResizeArr` helpers in [BVH.fs](../../Src/BVH.fs). So the BVH methods are hardly affected by what else runs, while `Polyline2D.tryFindSelfIntersection` is 5 to 17 times slower together than alone.

Medians, from Node.js 26.12.1 with Fable 5.27.0, on the same machine.
A ratio below 1.00 is faster than the first method of the table.

**Polyline2D, alone**, each method in a fresh Node.js process:

| Points | `tryFindSelfIntersection` | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: |
| 5 | 17.9 ns | 180 ns | 10.10 |
| 20 | 168 ns | 2.09 µs | 12.49 |
| 50 | 1.04 µs | 4.62 µs | 4.43 |
| 100 | 4.10 µs | 9.84 µs | 2.40 |
| 200 | 16.5 µs | 21.3 µs | 1.29 |
| 500 | 103 µs | 57.6 µs | 0.56 |
| 1,000 | 408 µs | 131 µs | 0.32 |
| 2,000 | 1.59 ms | 276 µs | 0.17 |
| 10,000 | 38.5 ms | 1.97 ms | 0.05 |

**Polyline2D, together**, all methods in one Node.js process:

| Points | `tryFindSelfIntersection` | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: |
| 5 | 92.9 ns | 281 ns | 3.02 |
| 20 | 2.58 µs | 3.20 µs | 1.24 |
| 50 | 16.3 µs | 7.75 µs | 0.48 |
| 100 | 64.9 µs | 16.0 µs | 0.25 |
| 200 | 260 µs | 33.3 µs | 0.13 |
| 500 | 1.60 ms | 93.6 µs | 0.06 |
| 1,000 | 6.43 ms | 200 µs | 0.03 |
| 2,000 | 25.7 ms | 436 µs | 0.02 |
| 10,000 | 641 ms | 2.70 ms | 0.004 |

**Polyline3D, alone**, each method in a fresh Node.js process:

| Points | `tryFindSelfIntersection` | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: |
| 5 | 33.5 ns | 337 ns | 10.06 |
| 20 | 2.84 µs | 2.42 µs | 0.85 |
| 50 | 20.2 µs | 5.44 µs | 0.27 |
| 100 | 83.0 µs | 11.8 µs | 0.14 |
| 200 | 340 µs | 25.0 µs | 0.07 |
| 500 | 2.16 ms | 66.5 µs | 0.03 |
| 1,000 | 8.65 ms | 154 µs | 0.02 |
| 2,000 | 34.6 ms | 324 µs | 0.01 |
| 10,000 | 863 ms | 2.30 ms | 0.003 |

**Polyline3D, together**, all methods in one Node.js process:

| Points | `tryFindSelfIntersection` | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: |
| 5 | 51.1 ns | 335 ns | 6.56 |
| 20 | 3.24 µs | 2.54 µs | 0.78 |
| 50 | 22.6 µs | 5.86 µs | 0.26 |
| 100 | 92.7 µs | 12.0 µs | 0.13 |
| 200 | 377 µs | 25.5 µs | 0.07 |
| 500 | 2.77 ms | 70.3 µs | 0.03 |
| 1,000 | 9.61 ms | 156 µs | 0.02 |
| 2,000 | 38.4 ms | 339 µs | 0.01 |
| 10,000 | 972 ms | 2.32 ms | 0.002 |

- **Alone**, Euclid's 2D function is the fastest up to about 200 points, and even faster than on .NET. The 2D BVH is faster from about 500 points on, 3 times at 1,000 points and 20 times at 10,000 points.
- **Together**, Euclid's 2D function is 5 to 17 times slower than alone, the 2D BVH only 1.4 to 1.7 times. So there the 2D BVH is faster from about 50 points on, 32 times at 1,000 points.
- In 3D the BVH is faster from about 20 points on, both alone and together: 56 to 62 times at 1,000 points. Euclid's 3D function is only 1.1 to 1.5 times slower together than alone, the 3D BVH about the same.
- Alone, the 2D BVH is 1.6 to 2.9 times slower on Node.js than on .NET.

These numbers are from a single run. Earlier runs on another machine varied by up to 2 times between runs for single rows, more than on .NET, while the crossover points stayed about the same.
The numbers before Euclid 1.0, measured on an Intel Core i5-1135G7 laptop, are in the git history of this file.
