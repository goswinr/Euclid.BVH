module SvgVisualisation

open System
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

type Scene = {
    Lines: Segment[]
    Levels: Rectangle[][]
    }

let createScene (seed: int) : Scene =
    let random = Random seed
    let lines =
        Array.init 100 (fun _ ->
            let x = random.NextDouble() * 100.0
            let y = random.NextDouble() * 100.0
            let angle = random.NextDouble() * Math.PI * 2.0
            let length = 1.0 + random.NextDouble() * 4.0
            Line2D (x, y, x + cos angle * length, y + sin angle * length))
    let tree = LineBvh2d.create (lines, 2)
    {
        Lines =
            lines
            |> Array.map (fun line -> { X1 = line.From.X; Y1 = line.From.Y; X2 = line.To.X; Y2 = line.To.Y })
        Levels =
            tree.Tree.NodeRectanglesByDepth
            |> Array.map (Array.map (fun rect -> { MinX = rect.MinX; MinY = rect.MinY; MaxX = rect.MaxX; MaxY = rect.MaxY }))
    }
