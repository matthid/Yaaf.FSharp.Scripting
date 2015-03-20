module internal Yaaf.FSharp.Scripting.Helper
open System
open System.Collections.Generic
open Microsoft.FSharp.Compiler.Interactive.Shell
open System.IO
open System.Text
open Microsoft.FSharp.Compiler.SourceCodeServices

/// Represents a simple (fake) event loop for the 'fsi' object
type SimpleEventLoop () = 
  member x.Run () = ()
  member x.Invoke<'T>(f:unit -> 'T) = f()
  member x.ScheduleRestart() = ()

/// Implements a simple 'fsi' object to be passed to the FSI evaluator
[<Sealed>]
type InteractiveSettings()  = 
  let mutable evLoop = (new SimpleEventLoop())
  let mutable showIDictionary = true
  let mutable showDeclarationValues = true
  let mutable args = Environment.GetCommandLineArgs()
  let mutable fpfmt = "g10"
  let mutable fp = (System.Globalization.CultureInfo.InvariantCulture :> System.IFormatProvider)
  let mutable printWidth = 78
  let mutable printDepth = 100
  let mutable printLength = 100
  let mutable printSize = 10000
  let mutable showIEnumerable = true
  let mutable showProperties = true
  let mutable addedPrinters = []

  member self.FloatingPointFormat with get() = fpfmt and set v = fpfmt <- v
  member self.FormatProvider with get() = fp and set v = fp <- v
  member self.PrintWidth  with get() = printWidth and set v = printWidth <- v
  member self.PrintDepth  with get() = printDepth and set v = printDepth <- v
  member self.PrintLength  with get() = printLength and set v = printLength <- v
  member self.PrintSize  with get() = printSize and set v = printSize <- v
  member self.ShowDeclarationValues with get() = showDeclarationValues and set v = showDeclarationValues <- v
  member self.ShowProperties  with get() = showProperties and set v = showProperties <- v
  member self.ShowIEnumerable with get() = showIEnumerable and set v = showIEnumerable <- v
  member self.ShowIDictionary with get() = showIDictionary and set v = showIDictionary <- v
  member self.AddedPrinters with get() = addedPrinters and set v = addedPrinters <- v
  member self.CommandLineArgs with get() = args  and set v  = args <- v
  member self.AddPrinter(printer : 'T -> string) =
    addedPrinters <- Choice1Of2 (typeof<'T>, (fun (x:obj) -> printer (unbox x))) :: addedPrinters

  member self.EventLoop
    with get () = evLoop
    and set (x:SimpleEventLoop)  = ()

  member self.AddPrintTransformer(printer : 'T -> obj) =
    addedPrinters <- Choice2Of2 (typeof<'T>, (fun (x:obj) -> printer (unbox x))) :: addedPrinters


let isMono = try Type.GetType("Mono.Runtime") <> null with _ -> false 
let getSession (defines) =
    // Intialize output and input streams
    let sbOut = new StringBuilder()
    let sbErr = new StringBuilder()
    let sbInput = new StringBuilder()
    let inStream = new StringReader("")
    let outStream = new StringWriter(sbOut)
    let errStream = new StringWriter(sbErr)

    // Build command line arguments & start FSI session

    let args =
        let includes =
          if isMono then
            // Workaround that FSC doesn't find a FSharp.Core.dll
            let runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
            let monoDir = Path.GetDirectoryName runtimeDir
            // prefer current runtime (which FSC would find anyway, but fallback to 4.0 if nothing is found in 4.5 or higher)
            // See also https://github.com/fsharp/fsharp/pull/389, https://github.com/fsharp/fsharp/pull/388
            [ runtimeDir; Path.Combine (monoDir, "4.0") ]
          else []
        [ yield "C:\\fsi.exe"
          yield "--noninteractive"
          for i in includes do
            yield sprintf "-I:%s" i
          for define in defines do
            yield sprintf "--define:%s" define ]

    let fsiSession =
      try
        let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration(new InteractiveSettings(), false)
        FsiEvaluationSession.Create(fsiConfig, args |> List.toArray, inStream, outStream, errStream)
      with e ->
        raise <| new Exception(
          sprintf "Error in creating a fsi session: %s\nConcrete exn: %A\nOutput: %s\nInput: %s" (sbErr.ToString()) e (sbOut.ToString()) (sbInput.ToString()),
          e)
  
    let save_ f text =
      try
        f text
      with e ->
        raise <| new Exception(
          sprintf "Evaluation of (\n%s\n) failed: %s\nConcrete exn: %A\nOutput: %s\nInput: %s" text (sbErr.ToString()) e (sbOut.ToString()) (sbInput.ToString()),
          e)
    
    let save f = 
        save_ (fun text ->
            sbInput.AppendLine(text) |> ignore
            f text)
    let saveScript f = 
        save_ (fun path ->
            sbInput.AppendLine(File.ReadAllText path) |> ignore
            f path)

    let evalInteraction = save fsiSession.EvalInteraction 
    let evalExpression = save fsiSession.EvalExpression
    let evalScript = saveScript fsiSession.EvalScript
    
    // We need to introduce ourself so we can hook later
    let session =
        { new IFsiSession with
            member x.EvalInteraction text = evalInteraction text
            member x.EvalScript path = evalScript path
            member x.EvalExpression<'a> text = 
              match evalExpression text with
              | Some value ->
                match value.ReflectionValue with
                | :? 'a as v -> v
                | o -> failwithf "the returned value (%O) doesn't match the expected type (%A) but has type %A" o (typeof<'a>) (o.GetType())
              | _ -> failwith "no value was returned by expression: %s" text 
        }
    session.Reference (System.Reflection.Assembly.GetExecutingAssembly().Location)
    session.Open "Yaaf.FSharp.Scripting"
    session