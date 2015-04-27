module Test.Yaaf.FSharp.Scripting.FsiSessionTests

open NUnit.Framework
open Test.Yaaf.FSharp.Scripting.FsiUnquote
open Yaaf.FSharp.Scripting
open System.Text
open System.IO
let fsiSession = ScriptHost.CreateNew(["MYDEFINE"])
let liveOut = new StringBuilder()
let liveErr = new StringBuilder()
let liveOutStream = new StringWriter(liveOut)
let liveErrStream = new StringWriter(liveErr)
let preventFsiSession = 
  ScriptHost.CreateNew(
    ["MYDEFINE"], 
    preventStdOut = true,
    outWriter = liveOutStream,
    errWriter = liveErrStream)
let forwardFsiSession = 
  ScriptHost.CreateNew(
    ["MYDEFINE"], 
    preventStdOut = true,
    outWriter = ScriptHost.CreateForwardWriter (fun s -> liveOut.Append s |> ignore),
    errWriter = ScriptHost.CreateForwardWriter (fun s -> liveErr.Append s |> ignore))
let fixNewLines (l:string) = l.Replace("\r\n", "\n") 
let withOutput f =
  liveOut.Clear() |> ignore
  liveErr.Clear() |> ignore
  f (),
  liveOut.ToString(), liveErr.ToString()
[<Test>]
let ``let with a given integer type works`` () =
    fsiSession.Let "test" 25
    test <@ fsiSession.EvalExpression<int> "test" = 25 @>
    
[<Test>]
let ``test that we get the correct output`` () =
    let inter = fsiSession.EvalInteractionWithOutput "3 + 4"
    test <@ fixNewLines inter.Output.FsiOutput = "val it : int = 7\n" @>

[<Test>]
let ``let with a given integer option type works`` () =
    fsiSession.Let "test" (Some 25)
    test <@ fsiSession.EvalExpression<int option> "test" = Some 25 @>

[<Test>]
let ``check that defines work works`` () =
    fsiSession.EvalInteraction("""
#if MYDEFINE
let test = 125
#else
let test = 0
#endif""")
    test <@ fsiSession.EvalExpression<int> "test" = 125 @>

[<Test>]
let ``check that fsi object works`` () =
    fsiSession.EvalInteraction ("""
fsi.AddPrinter(fun (n:int) -> n.ToString())
printfn "%d" 4
""")

[<Test>]
let ``check that compile time exceptions are wrapped`` () =
  Assert.Throws<FsiEvaluationException>(fun () ->
    fsiSession.EvalInteraction ("""
asdfasd
"""))
  |> ignore

[<Test>]
let ``check that runtime exceptions are wrapped`` () =
  Assert.Throws<FsiEvaluationException>(fun () ->
    fsiSession.EvalInteraction ("""
((failwith "game over") : unit)
"""))
  |> ignore

[<Test>]
let ``check that we get print output`` () =
    let res = fsiSession.EvalInteractionWithOutput ("""
printf "%s" "test" """)
    test <@ res.Output.ScriptOutput = "test" @>
    
[<Test>]
let ``check that we can work with live output`` () =
  let _, out, err = withOutput (fun () ->
    let res = preventFsiSession.EvalInteractionWithOutput ("""
printfn "%s" "test"
eprintfn "%s" "test" """)
    test <@ fixNewLines res.Error.ScriptOutput = "test\n" @>)
  test <@ fixNewLines out = "test\n" @>
  test <@ fixNewLines err = "test\n" @>

[<Test>]
let ``check that we can work with a forwarder`` () =
  let _, out, err = withOutput (fun () ->
    let res = forwardFsiSession.EvalInteractionWithOutput ("""
printfn "%s" "test"
eprintfn "%s" "test" """)
    test <@ fixNewLines res.Error.ScriptOutput = "test\n" @>)
  test <@ fixNewLines out = "test\n" @>
  test <@ fixNewLines err = "test\n" @>