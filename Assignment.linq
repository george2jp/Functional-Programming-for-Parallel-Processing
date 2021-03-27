<Query Kind="FSharpProgram" />

// -------------------------

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open System.Diagnostics

// -------------------------------------

let mutable PARALLEL = true
let mutable AGENTS1 = true

// -------------------------------------

let mutable str1's = Array.zeroCreate<string> 0
let mutable str2's = Array.zeroCreate<string> 0
let mutable sent1's = Array.zeroCreate<int[]> 0
let mutable sent2's = Array.zeroCreate<int[]> 0
let mutable diag0's = Array.zeroCreate<int[]> 0
let mutable diag1's = Array.zeroCreate<int[]> 0
let mutable diag2's = Array.zeroCreate<int[]> 0

// -------------------------------------

let tid () = Thread.CurrentThread.ManagedThreadId

let duration f = 
    let timer = new Stopwatch()
    timer.Start()
    let res = f()
    timer.Stop()
    printfn "... [%d] duration: %i ms" (tid()) timer.ElapsedMilliseconds
    res

// ------------------------

let naive (z1:string) (z2:string) (s1:int[]) (s2:int[]): int =
    let n1, n2 = z1.Length, z2.Length
    let a = Array2D.create n1 n2 0
        
    for i1 in 0..n1-1 do
        a.[i1,0] <- s1.[i1]
        
    for i2 in 0..n2-1 do
        a.[0,i2] <- s2.[i2]
        
    for i1 in 1..n1-1 do
        for i2 in 1..n2-1 do
            a.[i1,i2] <- if z1.[i1] = z2.[i2] then a.[i1-1,i2-1] + 1 else max a.[i1,i2-1] a.[i1-1,i2]
    

    for i1 in 0..n1-1 do
        s1.[i1] <- a.[i1,n2-1] 
        
    for i2 in 0..n2-1 do
        s2.[i2] <- a.[n1-1,i2] 
    
    //a.Dump ()
    a.[n1-1,n2-1]

// -------------------------

let diag (z1:string) (z2:string) : int =
    let z1, z2 = "?" + z1, "?" + z2
    let n1, n2 = z1.Length, z2.Length
    
    let d0 = Array.create n1 0
    let d1 = Array.create n1 0
    let d2 = Array.create n1 0
    
    //for k = 2 to n1+n2-2 do
    let rec loop k (d0: int[]) (d1: int[]) (d2: int[]) =
        let lo = if k < n2 then 1 else k-n2+1 
        let hi = if k < n1 then k-1 else n1-1
                    
        for i = lo to hi do
            if z1.[i] = z2.[k-i] 
            then d2.[i] <- d0.[i-1] + 1 
            else d2.[i] <- max d1.[i-1] d1.[i]
        
        if k < n1+n2-2 then loop (k+1) d1 d2 d0
        else d2.[n1-1]
    
    loop 2 d0 d1 d2


//--------------------------
type Message =
     | FromWest of int
     | FromNorth of int

let result = TaskCompletionSource<int*int> ()

// not really needed, just to collect some threading stats
let threads = new ConcurrentDictionary<int, int> ()

// -------------------------

let agent (string1:string) (string2:string) (sentinel1:int[]) (sentinel2:int[]) (agents:MailboxProcessor<Message>[,]) (i1:int) (i2:int) = 
    MailboxProcessor<Message>.Start <| fun inbox ->
        let b1 = Array2D.length1 agents
        let b2 = Array2D.length2 agents
        
        async {
            let mutable n = 0
            let mutable w = 0
            
            let! m = inbox.Receive () 
            match m with
            | FromNorth t -> n <- t
            | FromWest t -> w <- t

            let! m' = inbox.Receive () 
            match m' with
            | FromNorth t -> n <- t
            | FromWest t -> w <- t
            
            threads.AddOrUpdate (tid (), 1, fun k v -> v + 1) |> ignore // for threading stats only
            // Thread.SpinWait 100000 // simulate intensive computation
            let log = sprintf "naive[%d,%d] %s %s %A %A" i1 i2 string1 string2 sentinel1 sentinel2
            let r = naive string1 string2 sentinel1 sentinel2
            printfn "%s -> r=%d %A %A" log r sentinel1 sentinel2
            
            if i1 = b1-1 && i2 = b2-1 then 
                result.SetResult (n+1, w+1)
            else
                if i1 < b1-1 then agents.[i1+1, i2].Post (FromNorth (n+1)) // Post to South
                if i2 < b2-1 then agents.[i1, i2+1].Post (FromWest (w+1))  // Post to East            
        }

// -------------------------

let run (strings1:string[]) (strings2:string[]) (sentinels1:int[][]) (sentinels2:int[][]) = 
    let b1, b2 = strings1.Length, strings2.Length
    let agents = Array2D.zeroCreate<MailboxProcessor<Message>> b1 b2 // typed nulls

    for i1 = 0 to b1-1 do
        for i2 = 0 to b2-1 do
            agents.[i1, i2] <- agent strings1.[i1] strings2.[i2] sentinels1.[i1] sentinels2.[i2] agents i1 i2
    
    // none of these agents can yet start
    for i2 = 1 to b2-1 do 
        agents.[0, i2].Post (FromNorth 0)
        
    for i1 = 1 to b1-1 do 
        agents.[i1, 0].Post (FromWest 0)
    
    // start 
    let n, w = 
        duration <| fun () -> 
            // actual start from the North-West corner
            agents.[0, 0].Post (FromNorth 0)
            agents.[0, 0].Post (FromWest 0)
            result.Task.Result
            
    printfn "... [%d] n:%d, w:%d" (tid()) n w

    // optional stats
    threads |> Seq.iter (fun kv -> printfn "... [%d] %d times" kv.Key kv.Value)
    let tcount = threads |> Seq.length
    let acount = threads |> Seq.sumBy (fun kv -> kv.Value)
    printfn "... [%d] total threads:%d, agents:%d" (tid()) tcount acount

// -------------------------

let main () =  // Excel example
    let b1, b2 = 2, 2
    let str1, str2 = "abcde", "abcdefg"
    let n1, n2 = str1.Length, str2.Length

    let split' n div =
        let q, r = n/div, n%div
        let l = List.replicate r (q+1) @ if q = 0 then [] else List.replicate (div-r) q
        //printfn "%A" q
        let b = l |> List.scan (fun s n -> s+n) 0
        //printfn "%A" b
        let z = Seq.zip b l
        Seq.toArray z
        
        
    let z1, z2 = split' n1 b1, split' n2 b2
    // s1:abcde; s2:abcdefg
    // n1:5; n2:7; div1:2; div2:3; z1:[|(0, 3); (3, 2)|]; z2:[|(0, 3); (3, 2); (5, 2)|]
    // zi:[| (beg', len) ... |]

    //printfn "z1: %A" z1
    //printfn "z2: %A" z2

    let str1', str2' = "?" + str1, "?" + str2

    str1's <- z1 |> Array.map (fun (b, l) -> str1'.Substring (b, l+1))
    str2's <- z2 |> Array.map (fun (b, l) -> str2'.Substring (b, l+1))

    //printfn "str1's: %A" str1's
    //printfn "str2's: %A" str2's

    sent1's <- z1 |> Array.map (fun (_, l) -> Array.zeroCreate<int> (l+1))
    sent2's <- z2 |> Array.map (fun (_, l) -> Array.zeroCreate<int> (l+1))

    //printfn "sent1's: %A" sent1's
    //printfn "sent2's: %A" sent2's

    //diag0's <- z1 |> Array.map (fun (_, l) -> Array.zeroCreate<int> (l+1))
    //diag1's <- z1 |> Array.map (fun (_, l) -> Array.zeroCreate<int> (l+1))
    //diag2's <- z1 |> Array.map (fun (_, l) -> Array.zeroCreate<int> (l+1))

    //printfn "diag0's: %A" diag0's
    //printfn "diag1's: %A" diag1's
    //printfn "diag2's: %A" diag2's

    run str1's str2's sent1's sent2's
    ()
   
let main2 () =  // Excel example, variant
    let b1, b2 = 2, 2
    
    let strings1 = [|  "_xazbyx"; "xazby" |]
    let strings2 = [|  "_axcyba"; "bxcyb" |]
    
    let sentinels1 = [| [| 0; 0; 0; 0; 0; 0; 0; |]; [| 0; 0; 0; 0; 0; |] |]
    let sentinels2 = [| [| 0; 0; 0; 0; 0; 0; 0; |]; [| 0; 0; 0; 0; 0; |] |]

    run strings1 strings2 sentinels1 sentinels2
    ()
   
let main1 () =  // Excel example, variant
    let b1, b2 = 1, 1
    
    let strings1 = [|  "_xazby" |]
    let strings2 = [|  "_axcyb" |]
    
    let sentinels1 = [| [| 0; 0; 0; 0; 0; 0; |] |]
    let sentinels2 = [| [| 0; 0; 0; 0; 0; 0; |] |]

    run strings1 strings2 sentinels1 sentinels2
    ()
   
let main3 () =  // Excel example, variant
    let b1, b2 = 1, 1
    
    let strings1 = [|  "_x"; "xa"; "az"; "zb"; "by" |]
    let strings2 = [|  "_a"; "ax"; "xc"; "cy"; "yb" |]
    
    let sentinels1 = [| [| 0; 0; |]; [| 0; 0; |]; [| 0; 0; |]; [| 0; 0; |]; [| 0; 0; |]; |]
    let sentinels2 = [| [| 0; 0; |]; [| 0; 0; |]; [| 0; 0; |]; [| 0; 0; |]; [| 0; 0; |]; |]

    run strings1 strings2 sentinels1 sentinels2
    ()
   
main ()
//main2 ()
//main1 ()
//main3 ()
// -------------------------