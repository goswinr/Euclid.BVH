module NearestNeighboursVisualisation

open System
open System.Collections.Generic
open System.Diagnostics
open Euclid

type Segment = {
    X1: float
    Y1: float
    X2: float
    Y2: float
    }

type Rectangle = {
    MinX: float
    MinY: float
    MaxX: float
    MaxY: float
    }

type Neighbor = {
    Index: int
    Distance: float
    }

type Performance = {
    BvhMilliseconds: float
    BruteForceMilliseconds: float
    Iterations: int
    }

type Scene = {
    Lines: Segment[]
    QueryIndex: int
    Neighbors: Neighbor[]
    Visited: Rectangle[]
    Performance: Performance
    }

type private Node = {
    Rect: BRect
    Indices: int[]
    Left: Node option
    Right: Node option
    }

let private toRectangle (rect: BRect) =
    { MinX = rect.MinX; MinY = rect.MinY; MaxX = rect.MaxX; MaxY = rect.MaxY }

let private sqRectDistance (a: BRect) (b: BRect) =
    let axis aMin aMax bMin bMax =
        if bMin > aMax then bMin - aMax
        elif aMin > bMax then aMin - bMax
        else 0.0
    let dx = axis a.MinX a.MaxX b.MinX b.MaxX
    let dy = axis a.MinY a.MaxY b.MinY b.MaxY
    dx * dx + dy * dy

let private buildTree (rects: BRect[]) =
    let rec build (indices: int[]) =
        let rect = indices |> Array.map (fun i -> rects.[i]) |> Array.reduce (fun a b -> a.Union b)
        if indices.Length <= 2 then
            { Rect = rect; Indices = indices; Left = None; Right = None }
        else
            let center i =
                let itemRect = rects.[i]
                if rect.MaxX - rect.MinX >= rect.MaxY - rect.MinY then
                    itemRect.MinX + itemRect.MaxX
                else
                    itemRect.MinY + itemRect.MaxY
            let sorted = indices |> Array.sortBy center
            let middle = sorted.Length / 2
            {
                Rect = rect
                Indices = Array.empty
                Left = Some (build sorted.[0 .. middle - 1])
                Right = Some (build sorted.[middle ..])
            }
    build [| 0 .. rects.Length - 1 |]

let private nearest (lines: Line2D[]) (rects: BRect[]) (root: Node) queryIndex neighborCount =
    let query = lines.[queryIndex]
    let queryRect = rects.[queryIndex]
    let candidates = ResizeArray<struct (int * float)>()
    let visited = ResizeArray<BRect>()

    let worstDistance () =
        if candidates.Count < neighborCount then Double.MaxValue
        else
            let struct (_, distance) = candidates.[candidates.Count - 1]
            distance

    let addCandidate index =
        if index <> queryIndex then
            candidates.Add (struct (index, XLine2D.getSqDistance (query, lines.[index])))
            let ordered = candidates |> Seq.sortBy (fun struct (_, distance) -> distance) |> Seq.toArray
            candidates.Clear()
            candidates.AddRange ordered
            if candidates.Count > neighborCount then
                candidates.RemoveAt (candidates.Count - 1)

    let rec search node =
        visited.Add node.Rect
        if sqRectDistance queryRect node.Rect <= worstDistance () then
            match node.Left, node.Right with
            | None, None ->
                for index in node.Indices do
                    addCandidate index
            | Some left, Some right ->
                let leftDistance = sqRectDistance queryRect left.Rect
                let rightDistance = sqRectDistance queryRect right.Rect
                if leftDistance <= rightDistance then
                    search left
                    search right
                else
                    search right
                    search left
            | _ -> ()

    search root
    candidates
    |> Seq.map (fun struct (index, sqDistance) -> { Index = index; Distance = sqrt sqDistance })
    |> Seq.toArray,
    visited.ToArray()

let private measureClosestLine (lines: Line2D[]) queryIndex =
    let query = lines.[queryIndex]
    let bvh = LineBvh2d.create lines
    let iterations = max 1 (2_000_000 / lines.Length)
    let stopwatch = Stopwatch()
    let mutable bvhResult = struct (-1, Double.MaxValue)

    stopwatch.Start()
    for _ = 1 to iterations do
        bvhResult <- bvh.ClosestLine (query, queryIndex)
    stopwatch.Stop()
    let bvhMilliseconds = stopwatch.Elapsed.TotalMilliseconds / float iterations

    let mutable bruteResult = struct (-1, Double.MaxValue)
    stopwatch.Restart()
    for _ = 1 to iterations do
        let mutable closestIndex = -1
        let mutable closestSqDistance = Double.MaxValue
        for i = 0 to lines.Length - 1 do
            if i <> queryIndex then
                let sqDistance = XLine2D.getSqDistance (query, lines.[i])
                if sqDistance < closestSqDistance then
                    closestIndex <- i
                    closestSqDistance <- sqDistance
        bruteResult <- struct (closestIndex, sqrt closestSqDistance)
    stopwatch.Stop()

    let struct (_, bvhDistance) = bvhResult
    let struct (_, bruteDistance) = bruteResult
    if abs (bvhDistance - bruteDistance) > 1e-9 then
        failwith $"BVH distance {bvhDistance} does not match brute-force distance {bruteDistance}."

    {
        BvhMilliseconds = bvhMilliseconds
        BruteForceMilliseconds = stopwatch.Elapsed.TotalMilliseconds / float iterations
        Iterations = iterations
    }

let createScene (seed: int) (lineIndex: int) (neighborCount: int) (lineCount: int) : Scene =
    let random = Random seed
    let lineCount = max 20 (min 20_000 lineCount)
    let lines =
        Array.init lineCount (fun _ ->
            let x = random.NextDouble() * 100.0
            let y = random.NextDouble() * 100.0
            let angle = random.NextDouble() * Math.PI * 2.0
            let length = 1.0 + random.NextDouble() * 4.0
            Line2D (x, y, x + cos angle * length, y + sin angle * length))
    let rects = lines |> Array.map BRect.createFromLine
    let queryIndex = max 0 (min (lines.Length - 1) lineIndex)
    let count = max 1 (min 10 neighborCount)
    let neighbors, visited = nearest lines rects (buildTree rects) queryIndex count
    {
        Lines =
            lines
            |> Array.map (fun line -> { X1 = line.From.X; Y1 = line.From.Y; X2 = line.To.X; Y2 = line.To.Y })
        QueryIndex = queryIndex
        Neighbors = neighbors
        Visited = visited |> Array.map toRectangle
        Performance = measureClosestLine lines queryIndex
    }
