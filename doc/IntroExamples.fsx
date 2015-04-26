(*** hide ***)
#nowarn "211"
#I "../build/net45"
#r "Yaaf.FSharp.Scripting.dll"
(**
# Working with IFsiSessions

## Creating sessions

`ScriptHost.CreateNew()` and `ScriptHost.Create()` provide several optional parameters 
to configure the scripting environment:

 * `fsiObj: obj`: 
    The optional "fsi" object to use (defaults to using the one defined in `FSharp.Compiler.Service.dll`)
    You should have the same members as https://github.com/Microsoft/visualfsharp/blob/fsharp4/src/fsharp/fsiaux.fsi#L19 .
    All your members are called via reflection.
 * `reportGlobal: bool`:
    When true all error messages and outputs contain the complete output (instead of only the output of the last command).
    This only affects error messages.
    Defaults to `false`
 * `outWriter: TextWriter`:
    A custom TextWriter object where we additionally write the stdout of the executing code (for long running scripts).
 * `fsiOutWriter: TextWriter`:
    A custom TextWriter object where we additionally write the fsi stdout (for long running scripts).
 * `errWriter: TextWriter`:
    A custom TextWriter object where we additionally write the stderr of the executing code (for long running scripts).
 * `fsiErrWriter: TextWriter`:
    A custom TextWriter object where we additionally write the fsi stderr (for long running scripts). 
 * `preventStdOut: bool`:
    A value indicating whether we should completly block the executing script from writing to the current stdout and stderr.
    Defaults to `false`. 
   
You should use `preventStdOut = true` if you want to control the script output yourself.
You can work with either the live output or with the return values.
Return values will be constant no matter the configuration options!

### Example usage ("Live Output"):

*)
// (*** define-output: liveRedirect ***)
open Yaaf.FSharp.Scripting

// Setup for all future interactions
let liveSession =
    try ScriptHost.CreateNew
          (preventStdOut = true,
           outWriter = ScriptHost.CreateForwardWriter (printf "Script stdout: %s"),
           errWriter = ScriptHost.CreateForwardWriter (printf "Script stderr: %s"))
    with :? FsiEvaluationException as e ->
        printf "FsiEvaluationSession could not be created."
        printf "%s" e.Result.Error.Merged
        reraise ()
liveSession.EvalInteraction """printf "Some test" """
// (** The standard output is: *)
// (*** include-output:test ***)
(**

### Example usage ("Return values"):

*)

// (*** define-output: direct ***)
// Use the "WithOutput" members and work with the return type
let directSession =
    try ScriptHost.CreateNew(preventStdOut = true)
    with :? FsiEvaluationException as e ->
        printf "FsiEvaluationSession could not be created."
        printf "%s" e.Result.Error.Merged
        reraise ()
let v = directSession.EvalInteractionWithOutput """printf "direct result" """
printfn "And We have: %s" v.Output.ScriptOutput
//(** The standard output is: *)
//(*** include-output:test ***)
//(** The value v is: *)
//(*** include-value: v ***)
(** 
Note that you can use both systems at the same time as well (the return values are always available).

*)

