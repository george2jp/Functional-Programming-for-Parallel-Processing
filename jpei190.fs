
// -------------------------
open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open System.IO
open Hopac
open Hopac.Core
open Hopac.Infixes


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
let mutable t = 0

let tid () = Thread.CurrentThread.ManagedThreadId

type Message =
     | FromWest of array<int>
     | FromNorth of array<int>



// let par_split (str1:string) (str2:string) (div1:int) (div2:int) =
//     let n1, n2 = str1.Length, str2.Length
//     let split' n div =
//         let q, r = n/div, n%div
//         let l = List.replicate r (q+1) @ if q = 0 then [] else List.replicate (div-r) q
//         let b = l |> List.scan (fun s n -> s+n) 0
//         let z = Seq.zip b l
//         Seq.toArray z
        
//     let z1, z2 = split' n1 div1, split' n2 div2
//     (z1, z2)

let duration f = 
    let timer = new Stopwatch()
    timer.Start()
    let res = f()
    timer.Stop()
    if t = 1 then
        printfn "... [%d] duration: %i ms" (tid()) timer.ElapsedMilliseconds
    res

// ------------------------

let naive (z1:string) (z2:string) (s1:int[]) (s2:int[]): Tuple<int, array<int>, array<int>> =
    let n1, n2 = z1.Length, z2.Length
    let res_s1 = Array.create s1.Length 0
    res_s1.[0] <- s2.[n2 - 1]

    for i1 in 1..n1-1 do
        let temp1 = Array.create 1 0
        let temp2 = Array.create 1 s1.[i1]
        for i2 in 1..n2-1 do
            if i2 > 1 then
                s2.[i2 - 2] <- temp1.[0]
            temp1.[0] <- temp2.[0]
            temp2.[0] <- if z1.[i1] = z2.[i2] then s2.[i2-1] + 1 else max temp2.[0] s2.[i2]
        s2.[0] <- s1.[i1]
        s2.[n2 - 2] <- temp1.[0]
        s2.[n2-1] <- temp2.[0]
        res_s1.[i1] <- temp2.[0]
 
    (s2.[n2-1], res_s1, s2)

let result = TaskCompletionSource<int> ()

// not really needed, just to collect some threading stats
let threads = new ConcurrentDictionary<int, int> ()

// -------------------------
let diag (z1:string) (z2:string)=
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

    let res = 
        duration <| fun () -> d2
    
    loop 2 d0 d1 d2
//---------------------------

let smalljob (string1:string) (string2:string) (channels:Ch<Message>[,]) (i1:int) (i2:int) = 
    
    
    job {
        let b1 = Array2D.length1 channels
        let b2 = Array2D.length2 channels
        let mutable n =  Array.create 0 0
        let mutable w =  Array.create 0 0
        
        let! m = Ch.take channels.[i1,i2]//inbox.Receive () 
        match m with
         | FromNorth t -> n <- t
         | FromWest t -> w <- t

        let! m' = Ch.take channels.[i1, i2] //inbox.Receive () 
        match m' with
        | FromNorth t -> n <- t
        | FromWest t -> w <- t
                //threads.AddOrUpdate (tid (), 1, fun k v -> v + 1) // for threading stats only
                
        let res, new_w, new_n = naive string1 string2 w n
        if t = 1 then
                   Console.WriteLine(i1.ToString() + " " + i2.ToString() + " " + res.ToString())
        if i1 = b1-1 && i2 = b2-1 then 
                    result.SetResult (res)
        else
                     // Post to South
            if i1 < b1-1 then do! channels.[i1+1, i2] *<+ (FromNorth (new_n))
                     // Post to East 
            if i2 < b2-1 then do! channels.[i1, i2+1] *<+ (FromWest (new_w))
            //do! channels.[i1, i2+1] *<+ (FromWest w)
            //do! channels.[i1, i2+1] *<+ (FromNorth n)
     }
    
            

// -------------------------

let run1 (strings1:string[]) (strings2:string[]) (z1: (int * int)[]) (z2: (int * int)[]) = 
    let b1, b2 = strings1.Length, strings2.Length
    let channels = Array2D.zeroCreate<Ch<Message>> b1 b2
    
   
    let cal = job {
                    for i1 = 0 to b1-1 do
                        for i2 = 0 to b2-1 do
                            channels.[i1,i2] <- Ch()
                            start( smalljob strings1.[i1] strings2.[i2] channels i1 i2)
                         
    
    // none of these agents can yet start
                    for i2 = 1 to b2-1 do 
                        do! channels.[0, i2] *<+ (FromNorth (Array.create ((snd z2.[i2]) + 1) 0)) 
                    for i1 = 1 to b1-1 do  
                        do! channels.[i1, 0] *<+ (FromWest (Array.create ((snd z1.[i1]) + 1) 0))
    
    // start 
  
            // actual start from the North-West corner
                        do! channels.[0, 0] *<+ (FromNorth (Array.create ((snd z2.[0]) + 1) 0)) 
                        do! channels.[0, 0] *<+ (FromWest (Array.create ((snd z1.[0]) + 1) 0)) 
                            
    }
    start cal
    //result.Task.Result
    printfn "%A %A %A" (b1-1) (b2-1) result.Task.Result 
    



//--------------------------------------------------------------------------------------------------------

let agent (string1:string) (string2:string) (agents:MailboxProcessor<Message>[,]) (i1:int) (i2:int) = 
    MailboxProcessor<Message>.Start <| fun inbox ->
        let b1 = Array2D.length1 agents
        let b2 = Array2D.length2 agents
        
        async {
            let mutable n =  Array.create 0 0
            let mutable w =  Array.create 0 0
            
            let! m = inbox.Receive () 
            match m with
            | FromNorth t -> n <- t
            | FromWest t -> w <- t

            let! m' = inbox.Receive () 
            match m' with
            | FromNorth t -> n <- t
            | FromWest t -> w <- t
            

            let (res, new_w, new_n) = naive string1 string2 w n
            if t = 1 then 
                Console.WriteLine(i1.ToString() + " " + i2.ToString() + " " + res.ToString())
            if i1 = b1-1 && i2 = b2-1 then 
                result.SetResult (res)
            else
                if i1 < b1-1 then agents.[i1+1, i2].Post (FromNorth new_n) // Post to South
                if i2 < b2-1 then agents.[i1, i2+1].Post (FromWest (new_w))  // Post to East            
        }

// -------------------------------------------------------------------------------------------------------
let run2 (strings1:string[]) (strings2:string[]) (z1: (int * int)[]) (z2: (int * int)[]) = 
    let b1, b2 = strings1.Length, strings2.Length
    let agents = Array2D.zeroCreate<MailboxProcessor<Message>> b1 b2 // typed nulls

    for i1 = 0 to b1-1 do
        for i2 = 0 to b2-1 do
            agents.[i1, i2] <- agent strings1.[i1] strings2.[i2] agents i1 i2
    
    // none of these agents can yet start
    for i2 = 1 to b2-1 do 
        agents.[0, i2].Post (FromNorth (Array.create ((snd z2.[i2]) + 1) 0))
        
    for i1 = 1 to b1-1 do 
        agents.[i1, 0].Post (FromWest (Array.create ((snd z1.[i1]) + 1) 0))
    
    // start 
    let res = 
        duration <| fun () -> 
            // actual start from the North-West corner
            agents.[0, 0].Post (FromNorth (Array.create ((snd z2.[0]) + 1) 0))
            agents.[0, 0].Post (FromWest (Array.create ((snd z1.[0]) + 1) 0))
            
            result.Task.Result        
    printfn "%d %d %d" (b1 - 1) (b2 - 1) res


    
//--------------------------------------------------------------------------------------------------------
[<EntryPoint>]
let main args =  // Excel example
    try 
    let str1, str2 = File.ReadAllText (args.[0].[4..]), File.ReadAllText (args.[1].[4..])
    let n1, n2 = str1.Length, str2.Length
    
    let funcName = args.[2].[1..3]
    
    if funcName = "SEQ"
     then 
        let seq = diag str1 str2
        printfn "1 1 %A" seq
    elif funcName = "CSP" 
     then
        let b'sArray = args.[2].[5..].Split([|","|], StringSplitOptions.None)
        let b3 = b'sArray.[0] |> int
        let b4 = b'sArray.[1] |> int
        t <- b'sArray.[2] |> int
        let split' n div =
            let q, r = n/div, n%div
            let l = List.replicate r (q+1) @ if q = 0 then [] else List.replicate (div-r) q
            
            let b = l |> List.scan (fun s n -> s+n) 0
            
            let z = Seq.zip b l
            Seq.toArray z
        let z1, z2 = split' n1 b3, split' n2 b4
        str1's <- z1 |> Array.map (fun (b, l) -> "?" + (str1.Substring (b, l)))
        str2's <- z2 |> Array.map (fun (b, l) -> "?" + (str2.Substring (b, l)))
        run1 str1's str2's z1 z2 
        ()
    elif funcName = "ACT"
    then
       let b'sArray = args.[2].[5..].Split([|","|], StringSplitOptions.None)
       let b1 = b'sArray.[0] |> int
       let b2 = b'sArray.[1] |> int
       t <- b'sArray.[2] |> int
       let split' n div =
           let q, r = n/div, n%div
           let l = List.replicate r (q+1) @ if q = 0 then [] else List.replicate (div-r) q
           
           let b = l |> List.scan (fun s n -> s+n) 0
           
           let z = Seq.zip b l
           Seq.toArray z
       let z1, z2 = split' n1 b1, split' n2 b2
       str1's <- z1 |> Array.map (fun (b, l) -> "?" + (str1.Substring (b, l)))
       str2's <- z2 |> Array.map (fun (b, l) -> "?" + (str2.Substring (b, l)))
       run2 str1's str2's z1 z2
       ()
    0


    
    with
        | ex -> 
            printfn "%s" (ex.ToString())
            //printfn "Invalid input Format"
            -1