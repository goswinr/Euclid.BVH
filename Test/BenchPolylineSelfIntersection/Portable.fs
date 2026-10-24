/// A small timing loop that runs the same way on .NET and on Fable JavaScript (Node.js).
/// BenchmarkDotNet does not run on JavaScript, so this loop measures Fable, and with the
/// --portable argument it measures .NET too, to compare both runtimes with the same loop.
module Portable

open System
open Euclid
open Shared

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop

let now () : float = emitJsExpr () "performance.now()"
let runtime () : string = emitJsExpr () "'Node.js ' + process.version"
#else
let now () = float (Diagnostics.Stopwatch.GetTimestamp()) * 1000.0 / float Diagnostics.Stopwatch.Frequency
let runtime () = Runtime.InteropServices.RuntimeInformation.FrameworkDescription
#endif

/// Returns the median time of one call in nanoseconds.
/// Warms up for 300 ms, then doubles the count of calls per batch until a batch takes at least 50 ms,
/// then times that batch 9 times. A batch that takes over half a second is timed only 3 times, to keep the run short.
/// There is no forced garbage collection before a batch: on Node.js that made the methods that allocate
/// about twice as slow afterwards. Garbage collection happens within the batches, as it would in an application.
/// The action returns whether a self intersection was found, which must never happen on a ring.
/// Checking that also keeps the JIT from optimizing the calls away.
let measure (action: Func<bool>) : float =
    let mutable hits = 0
    let mutable lastCall = 0.0
    let warmupStart = now ()
    while now () - warmupStart < 300.0 do
        let start = now ()
        if action.Invoke () then hits <- hits + 1
        lastCall <- now () - start

    // If a single call takes long enough already, there is nothing to calibrate.
    let mutable batch = 1
    let mutable elapsed = lastCall
    while elapsed < 50.0 do
        let start = now ()
        for _ = 1 to batch do
            if action.Invoke () then hits <- hits + 1
        elapsed <- now () - start
        if elapsed < 50.0 then batch <- batch * 2

    let samples = if elapsed > 500.0 then 3 else 9
    let times = Array.zeroCreate<float> samples
    for sample = 0 to samples - 1 do
        let start = now ()
        for _ = 1 to batch do
            if action.Invoke () then hits <- hits + 1
        times.[sample] <- (now () - start) * 1_000_000.0 / float batch
    if hits <> 0 then failwith "A ring has no self intersection, but one was found."
    Array.sortInPlace times
    times.[samples / 2]

/// A method to measure: its name in the table header, and how to prepare a call of it
/// on a ring with a given count of points. The call returns whether it found a self intersection.
/// The call is a delegate, not an F# function: Fable compiles a field of type int -> unit -> bool
/// to a single function of two arguments, and that would build the ring again in every timed call.
type Method = { Name: string; Prepare: int -> Func<bool> }

let methods : Method[] = [|
    { Name = "tryFindSelfIntersection"; Prepare = fun n -> let pl = ring n in Func<bool>(fun () -> (Polyline2D.tryFindSelfIntersection pl).IsSome) }
    { Name = "...WithBRect"; Prepare = fun n -> let pl = ring n in Func<bool>(fun () -> (Polyline2D.tryFindSelfIntersectionWithBRect pl).IsSome) }
    { Name = "...WithBvh"; Prepare = fun n -> let pl = ring n in Func<bool>(fun () -> (Polyline2D.tryFindSelfIntersectionWithBvh pl).IsSome) }
    { Name = "brute force"; Prepare = fun n -> let pl = ring3D n in Func<bool>(fun () -> (tryFindSelfIntersection3D pl).IsSome) }
    { Name = "...WithBvh"; Prepare = fun n -> let pl = ring3D n in Func<bool>(fun () -> (Polyline3D.tryFindSelfIntersectionWithBvh pl).IsSome) } |]

/// The tables to print: a title, and the indices of their methods.
/// The first method of a table is the one the ratios refer to.
let tables = [| "Polyline2D", [| 0; 1; 2 |]; "Polyline3D", [| 3; 4 |] |]

/// Measures all methods in this process, for each point count one method after the other.
/// Returns the times indexed by method, then by point count.
let measureAll () : float[][] =
    let times = Array.init methods.Length (fun _ -> Array.zeroCreate<float> pointCounts.Length)
    for c = 0 to pointCounts.Length - 1 do
        for m = 0 to methods.Length - 1 do
            times.[m].[c] <- measure (methods.[m].Prepare pointCounts.[c])
    times

let formatTime (ns: float) =
    let value, unit =
        if ns < 1e3 then ns, "ns"
        elif ns < 1e6 then ns / 1e3, "us"
        else ns / 1e6, "ms"
    if value >= 100.0 then sprintf "%.0f %s" value unit
    elif value >= 10.0 then sprintf "%.1f %s" value unit
    else sprintf "%.2f %s" value unit

/// Prints a Markdown table of the given methods, from the times indexed by method, then by point count.
let printTable (title: string) (columns: int[]) (times: float[][]) =
    printfn ""
    printfn "%s" title
    printfn ""
    let header = columns |> Array.mapi (fun k m -> if k = 0 then methods.[m].Name else methods.[m].Name + " | Ratio")
    let align = columns |> Array.mapi (fun k _ -> if k = 0 then "---:" else "---: | ---:")
    printfn "| Points | %s |" (String.concat " | " header)
    printfn "| ---: | %s |" (String.concat " | " align)
    for c = 0 to pointCounts.Length - 1 do
        let baseline = times.[columns.[0]].[c]
        let cells =
            columns |> Array.mapi (fun k m ->
                let t = times.[m].[c]
                if k = 0 then formatTime t else sprintf "%s | %.2f" (formatTime t) (t / baseline))
        printfn "| %d | %s |" pointCounts.[c] (String.concat " | " cells)

#if FABLE_COMPILER
// The parameters only declare the signature, the imported JavaScript function gets them.
[<Import("execFileSync", "node:child_process")>]
let execFileSync (_file: string, _args: string[], _options: obj) : string = jsNative

/// Measures one method at one point count alone, in a fresh Node.js process.
/// V8 keeps the type feedback of a function for all its callers together. Fable compiles every read
/// from an array or ResizeArray to a call of fable-library's item function, so once a process has
/// read from arrays of several kinds, all methods run slower code than they would on their own.
/// The child process runs this program with --only, which calls printOne.
let measureInFreshProcess (methodIndex: int) (pointCount: int) : float =
    let node : string = emitJsExpr () "process.execPath"
    let script : string = emitJsExpr () "process.argv[1]"
    let output = execFileSync (node, [| script; "--only"; string methodIndex; string pointCount |], createObj [ "encoding" ==> "utf8" ])
    float (output.Trim())

/// Prints the time of one method at one point count, for measureInFreshProcess.
let printOne (methodIndex: int) (pointCount: int) =
    printfn "%.3f" (measure (methods.[methodIndex].Prepare pointCount))

/// Measures each method at each point count alone in a fresh process, and then all of them together in this process.
/// The fresh processes run one point count after the other, and all methods for each,
/// so that a CPU clock that changes during the run affects all methods alike.
let run () =
    let alone = Array.init methods.Length (fun _ -> Array.zeroCreate<float> pointCounts.Length)
    for c = 0 to pointCounts.Length - 1 do
        for m = 0 to methods.Length - 1 do
            alone.[m].[c] <- measureInFreshProcess m pointCounts.[c]
    let together = measureAll ()
    for name, columns in tables do
        printTable (sprintf "%s, median time per call on a closed ring, each method alone in a fresh %s process:" name (runtime ())) columns alone
        printTable (sprintf "%s, median time per call on a closed ring, all methods in one %s process:" name (runtime ())) columns together
#else
/// Measures all methods together in this process.
let run () =
    let together = measureAll ()
    for name, columns in tables do
        printTable (sprintf "%s, median time per call on a closed ring, %s:" name (runtime ())) columns together
#endif
