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
let fixNewLines (l:string) = l.Replace("\r\n", "\n")

[<Test>]
let ``Check if we don't call the forwarder`` () =
  let called = ref false
  ( use __ = ScriptHost.CreateForwardWriter ((fun _ -> called := true), removeNewLines = true)
    ())
  test <@ (not !called) @>

[<Test>]
let ``Check if we can detect a newline charater at the end with removeNewLines`` () =
  let called = ref 0
  ( use forwarder = ScriptHost.CreateForwardWriter ((fun _ -> called := !called + 1), removeNewLines = true)
    forwarder.WriteLine "test")
  test <@ !called = 2 @>

  called := 0
  ( use forwarder = ScriptHost.CreateForwardWriter ((fun _ -> called := !called + 1), removeNewLines = true)
    forwarder.Write "test")
  test <@ !called = 1 @>

[<Test>]
let ``Check if get the last input`` () =
  let sb = new StringBuilder()
  ( use forwarder = ScriptHost.CreateForwardWriter (sb.AppendLine >> ignore, removeNewLines = true)
    forwarder.Write "test"
    ())
  test <@ fixNewLines (sb.ToString()) = "test\n" @>

[<Test>]
let ``Check if get the multiple inputs`` () =
  let sb = new StringBuilder()
  ( use forwarder = ScriptHost.CreateForwardWriter (sb.AppendLine >> ignore, removeNewLines = true)
    forwarder.Write "test"
    forwarder.Write "test1"
    forwarder.WriteLine ()
    forwarder.Write "test2"
    ())
  test <@ fixNewLines (sb.ToString()) = "testtest1\ntest2\n" @>

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
    outWriter = ScriptHost.CreateForwardWriter (liveOut.Append >> ignore),
    errWriter = ScriptHost.CreateForwardWriter (liveErr.Append >> ignore))
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
    test <@ fixNewLines (inter.Output.FsiOutput.Trim()) = "val it : int = 7" @>

[<Test>]
let ``let with a given integer option type works`` () =
    fsiSession.Let "test" (Some 25)
    test <@ fsiSession.EvalExpression<int option> "test" = Some 25 @>

[<Test>]
let ``check that default config produces no error output`` () =
  let _, _, err =
    withOutput (fun () ->
      ScriptHost.CreateNew(
        ["MYDEFINE"],
        preventStdOut = true,
        outWriter = ScriptHost.CreateForwardWriter (liveOut.Append >> ignore),
        errWriter = ScriptHost.CreateForwardWriter (liveErr.Append >> ignore)))
  test <@ System.String.IsNullOrWhiteSpace(err) @>

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

[<Test>]
let ``test Handle method`` () =
  match fsiSession.Handle<int> fsiSession.EvalExpression "5 + 4" with
  | InvalidExpressionType _
  | InvalidCode _ -> Assert.Fail "expected 9"
  | Result r -> test <@ r = 9 @>

  match fsiSession.Handle<string> fsiSession.EvalExpression "5 + 4" with
  | InvalidExpressionType e -> test <@ e.Value.IsSome @>
  | InvalidCode _
  | Result _ -> Assert.Fail "expected InvalidExpressionType failure"

  match fsiSession.Handle<string> fsiSession.EvalExpression """failwith "test" : int """ with
  | InvalidCode _ -> ()
  | InvalidExpressionType _
  | Result _ -> Assert.Fail "expected InvalidCode failure"

[<Test>]
let ``check that we can access System.IO`` () =
    File.WriteAllText("Test.txt", "content")
    try
        let res = fsiSession.EvalInteractionWithOutput ("""
printf "%s" <| System.IO.File.ReadAllText("Test.txt") """)
        test <@ res.Output.ScriptOutput = "content" @>
    finally
        File.Delete("Test.txt")

[<Test>]
let ``check that we can access System.Linq`` () =
    let res = fsiSession.EvalInteractionWithOutput ("""
System.Linq.Enumerable.Average([1; 3]) |> int |> printf "%d" """)
    test <@ res.Output.ScriptOutput = "2" @>

[<Test>]
let ``check that we can access System.Numerics`` () =
    let res = fsiSession.EvalInteractionWithOutput ("""
typeof<System.Numerics.BigInteger>.Name |> printf "%s" """)
    test <@ res.Output.ScriptOutput = "BigInteger" @>

[<Test>]
let ``Test that we can use objects after dispose`` () =
  let incFunc, getFunc =
    ( use fsiSession = ScriptHost.CreateNew(["MYDEFINE"])
      fsiSession.EvalInteraction("""let mutable t = 0""")
      fsiSession.EvalExpression<unit -> unit>("fun () -> t <- t + 1"),
      fsiSession.EvalExpression<unit -> int>("fun () -> t"))

  test <@ getFunc() = 0 @>
  test <@ getFunc() = 0 @>
  incFunc()
  test <@ getFunc() = 1 @>
  incFunc()
  incFunc()
  test <@ getFunc() = 3 @>
  test <@ getFunc() = 3 @>

[<Test>]
let ``Test that we can call with full debug`` () =
  ( use fsiSession = ScriptHost.Create({ FsiOptions.Default with Debug = Some DebugMode.Full })
    let res = fsiSession.EvalInteractionWithOutput ("""
System.Linq.Enumerable.Average([1; 3]) |> int |> printf "%d" """)
    test <@ res.Output.ScriptOutput = "2" @>)

[<Test>]
let ``Test that we can call with no debug`` () =
  ( use fsiSession = ScriptHost.Create({ FsiOptions.Default with Debug = Some DebugMode.NoDebug })
    let res = fsiSession.EvalInteractionWithOutput ("""
System.Linq.Enumerable.Average([1; 3]) |> int |> printf "%d" """)
    test <@ res.Output.ScriptOutput = "2" @>)

[<Test>]
let ``Test that we can call with pdbonly debug`` () =
  ( use fsiSession = ScriptHost.Create({ FsiOptions.Default with Debug = Some DebugMode.PdbOnly })
    let res = fsiSession.EvalInteractionWithOutput ("""
System.Linq.Enumerable.Average([1; 3]) |> int |> printf "%d" """)
    test <@ res.Output.ScriptOutput = "2" @>)

    
[<Test>]
let ``Test that report local works`` () =
  ( use fsiSession = ScriptHost.CreateNew()
    match fsiSession.Handle<string> fsiSession.EvalExpression "asdasdfasdffas" with
    | InvalidCode  e -> test <@ e.Result.Error.FsiOutput.Contains "asdasdfasdffas" @>
    | InvalidExpressionType _
    | Result _ -> Assert.Fail "expected InvalidCode failure")

[<Test>]
let ``Test that report global works`` () =
  ( use fsiSession = ScriptHost.CreateNew(reportGlobal = true)
    match fsiSession.Handle<string> fsiSession.EvalExpression "asdasdfasdffas" with
    | InvalidCode  e -> test <@ e.Result.Error.FsiOutput.Contains "asdasdfasdffas" @>
    | InvalidExpressionType _
    | Result _ -> Assert.Fail "expected InvalidCode failure")