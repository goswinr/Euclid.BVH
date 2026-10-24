# Polyline self intersection: brute force versus Euclid.BVH

This benchmark compares ways to find a self intersection in a `Polyline2D` and in a `Polyline3D`, on .NET and on Fable JavaScript.

| Method | Broad phase | Narrow phase |
| --- | --- | --- |
| `Polyline2D.tryFindSelfIntersection` (Euclid) | none, every pair of non adjacent segments, O(n²) | `XLineXY.tryIntersect` |
| `Polyline2D.tryFindSelfIntersectionWithBRect` (Euclid) | a `BRect` overlap test on every pair of segments, O(n²) | `XLineXY.tryIntersect` |
| `Polyline2D.tryFindSelfIntersectionWithBvh` (Euclid.BVH) | a `BVH2D` over the segment rectangles, and its dual tree traversal `ClosePairs`, about O(n log n) | `XLineXY.tryIntersect` |
| brute force in 3D (`tryFindSelfIntersection3D` in [Shared.fs](Shared.fs)) | none, every pair of non adjacent segments, O(n²) | `XLineXYZ.tryIntersect` |
| `Polyline3D.tryFindSelfIntersectionWithBvh` (Euclid.BVH) | a `BVH` over the segment boxes, and its dual tree traversal `ClosePairs`, about O(n log n) | `XLineXYZ.tryIntersect` |

The methods of one dimension use the same narrow phase, so only the broad phase differs.
Euclid has no self intersection test for a `Polyline3D`, so the 3D brute force here copies the loops of `Polyline2D.tryFindSelfIntersection`.
The two `...WithBvh` methods are part of the library, see [PolylineSelfIntersection.fs](../../Src/PolylineSelfIntersection.fs).

## Method

- The input is a closed ring with `PointCount` points, so `PointCount - 1` segments: in 2D a circle of radius 100, in 3D the same circle with a height that goes up and down three times around it.
- The rings have no self intersection. This is the worst case for all methods, because each of them has to test all of its candidate pairs.
- The BVH methods build their tree inside the measured call, so the build time is included.
- The brute force methods stop at the first intersection they find, while `ClosePairs` always collects all of them. On a polyline that does intersect itself, the brute force methods can return early and the BVH methods cannot.
- Before anything is timed, the program checks that all methods of a dimension return the same result for a ring and for a figure eight that crosses itself, at every point count.
- Euclid is the version that `Src/Euclid.BVH.fsproj` references.

## .NET

Run it from the repository root, it takes a bit under two minutes:

```pwsh
dotnet run --project Test/BenchPolylineSelfIntersection/BenchPolylineSelfIntersection.fsproj --configuration Release
```

The arguments after `--` go to BenchmarkDotNet. To run only some of the benchmarks, use a filter, for example `-- --filter *3D*`.

- Before timing, the program calls all methods often on a small ring, so that .NET promotes them to fully optimized code. Without this, a call on 10,000 points takes so long that the benchmark makes fewer than the 30 calls needed for that promotion, and the plain 2D method was measured about 6 times too slow.
- To finish in under two minutes, the benchmarks run in process, with 2 warmup and 6 measured iterations of 120 ms each, and without BenchmarkDotNet's overhead stage. BenchmarkDotNet's default job would take over 20 minutes. For the 2D benchmarks it gave the same numbers within about 10% from 20 points on, but at 5 points this short job reads about 20% higher, probably because no call overhead is subtracted.

Medians, from BenchmarkDotNet 0.15.8 on .NET 10.0.13, Windows 11, Intel Core i5-1135G7.
A ratio below 1.00 is faster than the first method of the table.

**Polyline2D**:

| Points | `tryFindSelfIntersection` | `...WithBRect` | Ratio | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 5 | 43.4 ns | 74.2 ns | 1.71 | 184 ns | 4.25 |
| 20 | 551 ns | 510 ns | 0.93 | 1.96 µs | 3.55 |
| 50 | 3.98 µs | 2.52 µs | 0.63 | 5.69 µs | 1.43 |
| 100 | 13.5 µs | 8.60 µs | 0.64 | 11.7 µs | 0.86 |
| 200 | 50.5 µs | 33.2 µs | 0.66 | 25.4 µs | 0.50 |
| 500 | 303 µs | 191 µs | 0.63 | 74.7 µs | 0.25 |
| 1,000 | 1.28 ms | 716 µs | 0.56 | 167 µs | 0.13 |
| 2,000 | 4.63 ms | 2.95 ms | 0.64 | 355 µs | 0.08 |
| 10,000 | 115 ms | 73.2 ms | 0.63 | 2.55 ms | 0.02 |

**Polyline3D**:

| Points | brute force | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: |
| 5 | 66.1 ns | 249 ns | 3.78 |
| 20 | 2.21 µs | 3.64 µs | 1.65 |
| 50 | 13.8 µs | 10.8 µs | 0.78 |
| 100 | 55.9 µs | 22.8 µs | 0.41 |
| 200 | 227 µs | 47.4 µs | 0.21 |
| 500 | 1.39 ms | 148 µs | 0.11 |
| 1,000 | 5.61 ms | 315 µs | 0.06 |
| 2,000 | 22.6 ms | 835 µs | 0.04 |
| 10,000 | 618 ms | 3.37 ms | 0.01 |

- In 2D the BVH is 1.4 to 4 times slower than the plain version up to about 50 points, because building the tree costs more than it saves. It is faster than the plain version from about 100 points on, and faster than both Euclid functions from about 200 points on.
- At 1,000 points the 2D BVH is about 8 times faster than the plain version and 4 times faster than the BRect version. At 10,000 points it was 30 to 45 times faster than the plain version, over several runs.
- In 3D, where each pair costs more to test, the BVH is faster than the brute force from about 50 points on: 5 times at 200 points, 18 times at 1,000 points and 180 times at 10,000 points.
- The BVH allocates about as much as the BRect version, about 5 times as much as the plain version: from 552 B at 5 points to 850 KB at 10,000 points in 2D.

Repeated runs on this laptop agreed within about 15% for most rows, and up to 25% for some. Single rows occasionally differed more. The crossover points were the same in every run.

## Fable JavaScript

BenchmarkDotNet does not run on JavaScript, so the Fable build uses its own small timing loop in [Portable.fs](Portable.fs): it warms up each case for 300 ms, doubles the calls per batch until a batch takes 50 ms, and takes the median of 9 timed batches. Calls over half a second are timed only 3 times.

Compile it with Fable and run it with Node.js from the repository root. Compiling takes under a minute, running about two and a half minutes:

```pwsh
dotnet tool restore
dotnet fable Test/BenchPolylineSelfIntersection/BenchPolylineSelfIntersection.fsproj --outDir Test/BenchPolylineSelfIntersection/dist --configuration Release --noCache
node Test/BenchPolylineSelfIntersection/dist/Program.js
```

The same timing loop also runs on .NET with `-- --portable`. There most rows agreed with BenchmarkDotNet within about 10%, some differed by 20 to 35%, and the 3D BVH at 10,000 points read 2.5 times slower. The crossover points were the same.

Two things in the timing loop matter on Node.js. It builds the ring before the timed calls: the call to time is a delegate, because Fable compiles a curried F# function in a record field into a single function, which rebuilt the ring in every call. And it forces no garbage collection before a batch, because that made the methods that allocate about twice as slow afterwards.

### Two results for each method

On Node.js the speed of a method depends a lot on what else ran in the same process, so each method is measured twice:

1. **Alone:** each method at each point count in a fresh Node.js process of its own. This is the best case. The fresh processes run in the order of the point counts, all methods for each, so that a changing CPU clock affects all methods alike.
2. **Together:** all methods in one process, after the correctness check. This is closer to an application, which reads from many kinds of arrays.

The cause is in the generated code. Fable compiles every read from an array or `ResizeArray` to a call of fable-library's bounds checked `item(index, array)` function. V8 keeps one type feedback for that function, shared by all its callers. Once a process has read from arrays of several kinds (doubles, objects, typed arrays), that feedback is megamorphic, and every inlined array read in every method becomes a generic lookup. Calling only `item` on a few kinds of arrays before the measurement was enough to make `tryFindSelfIntersection` 17 times slower at 500 points.

Medians, from Node.js 26.7.0 with Fable 5.24.0, on the same machine.
A ratio below 1.00 is faster than the first method of the table.

**Polyline2D, alone**, each method in a fresh Node.js process:

| Points | `tryFindSelfIntersection` | `...WithBRect` | Ratio | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 5 | 48.4 ns | 165 ns | 3.41 | 693 ns | 14.31 |
| 20 | 551 ns | 1.42 µs | 2.57 | 14.7 µs | 26.67 |
| 50 | 3.13 µs | 4.52 µs | 1.45 | 31.5 µs | 10.07 |
| 100 | 12.6 µs | 15.9 µs | 1.26 | 74.9 µs | 5.96 |
| 200 | 48.4 µs | 54.7 µs | 1.13 | 162 µs | 3.35 |
| 500 | 274 µs | 303 µs | 1.11 | 482 µs | 1.76 |
| 1,000 | 1.14 ms | 1.22 ms | 1.07 | 1.35 ms | 1.18 |
| 2,000 | 4.46 ms | 4.93 ms | 1.10 | 3.44 ms | 0.77 |
| 10,000 | 111 ms | 117 ms | 1.06 | 22.0 ms | 0.20 |

**Polyline2D, together**, all methods in one Node.js process:

| Points | `tryFindSelfIntersection` | `...WithBRect` | Ratio | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 5 | 381 ns | 572 ns | 1.50 | 1.78 µs | 4.67 |
| 20 | 6.83 µs | 4.26 µs | 0.62 | 26.2 µs | 3.83 |
| 50 | 42.3 µs | 16.8 µs | 0.40 | 84.1 µs | 1.99 |
| 100 | 173 µs | 56.4 µs | 0.33 | 182 µs | 1.05 |
| 200 | 671 µs | 200 µs | 0.30 | 429 µs | 0.64 |
| 500 | 4.14 ms | 1.15 ms | 0.28 | 1.36 ms | 0.33 |
| 1,000 | 16.7 ms | 4.50 ms | 0.27 | 3.35 ms | 0.20 |
| 2,000 | 68.3 ms | 18.2 ms | 0.27 | 8.14 ms | 0.12 |
| 10,000 | 1,692 ms | 465 ms | 0.27 | 60.6 ms | 0.04 |

**Polyline3D, alone**, each method in a fresh Node.js process:

| Points | brute force | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: |
| 5 | 123 ns | 984 ns | 7.97 |
| 20 | 6.24 µs | 15.9 µs | 2.55 |
| 50 | 43.5 µs | 43.7 µs | 1.00 |
| 100 | 186 µs | 99.0 µs | 0.53 |
| 200 | 696 µs | 218 µs | 0.31 |
| 500 | 4.60 ms | 718 µs | 0.16 |
| 1,000 | 18.3 ms | 1.54 ms | 0.08 |
| 2,000 | 72.3 ms | 4.20 ms | 0.06 |
| 10,000 | 1,832 ms | 23.5 ms | 0.01 |

**Polyline3D, together**, all methods in one Node.js process:

| Points | brute force | `...WithBvh` | Ratio |
| ---: | ---: | ---: | ---: |
| 5 | 778 ns | 2.42 µs | 3.12 |
| 20 | 11.9 µs | 30.6 µs | 2.58 |
| 50 | 75.7 µs | 96.4 µs | 1.27 |
| 100 | 299 µs | 214 µs | 0.72 |
| 200 | 1.18 ms | 509 µs | 0.43 |
| 500 | 7.45 ms | 1.57 ms | 0.21 |
| 1,000 | 29.4 ms | 3.84 ms | 0.13 |
| 2,000 | 121 ms | 9.28 ms | 0.08 |
| 10,000 | 3,010 ms | 63.0 ms | 0.02 |

- **Alone**, the plain 2D version is the fastest up to 1,000 points, and about as fast as on .NET. The BRect version is slower than the plain version at every size, as Euclid's documentation says: 3.4 times at 5 points, about the same from 500 points on. The 2D BVH is faster than the plain version from about 2,000 points on, 5 times at 10,000 points.
- **Together**, the plain 2D version is 8 to 15 times slower than alone, the BRect version 3 to 4 times and the BVH 2 to 3 times. That fits how often they read arrays: the plain version reads four array elements for every pair of segments, the BRect version one for most pairs. It turns the ranking around: the BRect version is faster than the plain version from 20 points on, the BVH from about 200 points on, and the BVH is faster than the BRect version from about 1,000 points on. At 10,000 points the BVH is 28 times faster than the plain version and 8 times faster than the BRect version.
- In 3D the BVH is faster than the brute force from about 100 points on, both alone and together.
- Alone, the 2D BVH is 4 to 10 times slower on Node.js than on .NET.

Single rows varied by up to 2 times between runs on this laptop, more than on .NET, while the crossover points stayed about the same.
