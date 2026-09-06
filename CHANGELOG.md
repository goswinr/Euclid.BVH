# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-09-07
### Added
- `Bvh<'T>`: a generic static Bounding Volume Hierarchy over any item type, built from items plus a bounding box function (`Bvh.create`) or directly from `BBox[]` (`Bvh.createFromBoxes`).
- Box based queries on `Bvh<'T>`: `ClosestBox`, `ClosestPair`, `NearestNeighbors`, `ClosePairs` and `ItemsInBox`.
- Point queries on `Bvh<'T>`: `ClosestBox (pt)`, `ClosestItem (pt, sqDistanceTo)` and `ItemsNearPoint (pt, ?tolerance)` for querying with a single 3D point (`Pnt`).
- Exact distance queries on `Bvh<'T>` via squared distance callbacks: `ClosestItem`, `ClosestPair`, `NearestNeighbors` and `ClosePairs` overloads.
- `Bvh2d<'T>`: a 2D BVH with its own data structure, storing a `BRect` per node and running all queries directly on rectangles.
- Rectangle based queries on `Bvh2d<'T>`: `ClosestRect (queryRect)`, `ClosestRect (pt)`, `ClosestPair`, `NearestNeighbors`, `ClosePairs`, `ItemsInRect` and `ItemsNearPoint`.
- Exact distance queries on `Bvh2d<'T>` via squared distance callbacks: `ClosestItem`, `ClosestPair`, `NearestNeighbors` and `ClosePairs` overloads.
- `Bvh2d.Rects` exposing the bounding rectangle of every item.
- `LineBvh`: a static Bounding Volume Hierarchy over `Line3D`, a thin wrapper around `Bvh<Line3D>` with `LineBvh.Tree` exposing the underlying generic tree.
- `LineBvh.create` to build the tree by median splits along the longest axis.
- `LineBvh.ClosestLine` branch-and-bound nearest line query, with optional self exclusion.
- `LineBvh.ClosestPair` to find the globally closest pair of lines.
- `LineBvh.NearestNeighbors` to find the nearest neighbor of every line.
- `LineBvh.ClosePairs` dual tree traversal to find all pairs of lines closer than a maximum distance.
- `LineBvh.LinesInBox` to find all lines near an axis aligned bounding box.
- Point queries on `LineBvh`: `ClosestLine (pt)`, `ClosestPoint (pt)` and `LinesNearPoint (pt, ?tolerance)`.
- `LineBvh2d`: the 2D counterpart of `LineBvh`, built on `Bvh2d<'T>`.
- `BvhPair` result type for pair queries, with `LinePair` as an alias for it.
- Fable support: the library and all tests compile and pass with Fable (JavaScript and TypeScript), tested with Mocha in CI like the Euclid library.
- An interactive SVG nearest-neighbour visualisation for 20–20,000 random lines, stepping through queries, selecting 1–10 neighbours, showing the bounding rectangles tested by the search and comparing per-query BVH performance with brute-force closest-line search.

[0.1.0]: https://github.com/goswinr/Euclid.BVH/releases/tag/0.1.0
