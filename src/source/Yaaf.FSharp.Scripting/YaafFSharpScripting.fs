namespace Yaaf.FSharp.Scripting

module internal Env =
  let isMono = try System.Type.GetType("Mono.Runtime") <> null with _ -> false 

open Env
[<AutoOpen>]
#if YAAF_FSHARP_SCRIPTING_PUBLIC
module CompilerServiceExtensions =
#else
module internal CompilerServiceExtensions =
#endif
  open System
  open System.Reflection
  open Microsoft.FSharp.Compiler
  open Microsoft.FSharp.Compiler.Interactive.Shell
  open Microsoft.FSharp.Compiler.SourceCodeServices
  open System.IO

  module internal FSharpAssemblyHelper =
      open System.IO
      let checker = FSharpChecker.Create()
      
      let (++) a b = System.IO.Path.Combine(a,b)
      let (=?) s1 s2 = System.String.Equals(s1, s2, System.StringComparison.InvariantCultureIgnoreCase)
      let sysDir =
        if System.Environment.OSVersion.Platform = System.PlatformID.Win32NT then
          @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0"
        else
          System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
      let getLib dir nm = 
          dir ++ nm + ".dll" 
      let sysLib = getLib sysDir
      let fsCore4300Dir = 
          if System.Environment.OSVersion.Platform = System.PlatformID.Win32NT then // file references only valid on Windows 
              @"C:\Program Files (x86)\Reference Assemblies\Microsoft\FSharp\.NETFramework\v4.0\4.3.0.0"  
          else 
              sysDir
      
      let fscoreResolveDirs libDirs =
        [ yield sysDir
          yield System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
          yield! libDirs
          yield Environment.CurrentDirectory
          yield fsCore4300Dir
          yield! try Path.GetDirectoryName (Assembly.GetExecutingAssembly().Location)
                     |> Seq.singleton
                 with :? NotSupportedException -> Seq.empty
          yield! try Path.GetDirectoryName 
                        (typeof<Microsoft.FSharp.Compiler.Interactive
                         .Shell.Settings.InteractiveSettings>.Assembly.Location)
                     |> Seq.singleton
                 with :? NotSupportedException -> Seq.empty
          if isMono then
            // See https://github.com/fsharp/FSharp.Compiler.Service/issues/317
            yield Path.GetDirectoryName sysDir ++ "4.0"
        ]

      let tryCheckFsCore fscorePath =
        let lib = fscorePath
        let opt = Path.ChangeExtension (lib, "optdata")
        let sig' = Path.ChangeExtension(lib, "sigdata")
        if [ lib; opt; sig' ] |> Seq.forall File.Exists then
          Some lib
        else None

      let findFSCore dllFiles libDirs =
        // lets find ourself some FSharp.Core.dll
        let tried =
          dllFiles @ (fscoreResolveDirs libDirs 
                      |> List.map (fun (l:string) -> getLib l "FSharp.Core"))
        match tried |> Seq.tryPick tryCheckFsCore with
        | Some s -> s
        | None -> failwithf "Could not find a FSharp.Core.dll (with bundled .optdata and .sigdata) in %A" tried

      let getProjectReferences otherFlags libDirs dllFiles =
          let otherFlags = defaultArg otherFlags Seq.empty
          let libDirs = defaultArg libDirs Seq.empty |> Seq.toList
          let dllFiles = dllFiles |> Seq.toList
          let hasAssembly asm = 
            dllFiles |> Seq.exists (fun a -> Path.GetFileNameWithoutExtension a =? asm) ||
            libDirs |> Seq.exists (fun lib ->
              Directory.EnumerateFiles(lib)
              |> Seq.filter (fun file -> Path.GetExtension file =? ".dll")
              |> Seq.exists (fun file -> Path.GetFileNameWithoutExtension file =? asm))
          let hasFsCoreLib = hasAssembly "FSharp.Core"
          let fsCoreLib =
            if not hasFsCoreLib then
              Some (findFSCore dllFiles libDirs)
            else None
          let defaultReferences = 
            Directory.EnumerateFiles(sysDir)
            |> Seq.filter (fun file -> Path.GetExtension file =? ".dll")
            |> Seq.map Path.GetFileNameWithoutExtension
            |> Seq.filter (fun f -> not (f =? "FSharp.Core"))
            |> Seq.filter (not << hasAssembly)
          let base1 = Path.GetTempFileName()
          let dllName = Path.ChangeExtension(base1, ".dll")
          let xmlName = Path.ChangeExtension(base1, ".xml")
          let fileName1 = Path.ChangeExtension(base1, ".fs")
          let projFileName = Path.ChangeExtension(base1, ".fsproj")
          File.WriteAllText(fileName1, """module M""")
          let options =
              checker.GetProjectOptionsFromCommandLineArgs(projFileName,
                  [| //yield "--debug:full" 
                      yield "--define:DEBUG" 
                      //yield "--optimize-" 
                      yield "--nooptimizationdata"
                      yield "--noframework"
                      yield sprintf "-I:%s" sysDir
                      for ref in defaultReferences do
                        yield sprintf "-r:%s" (sysLib ref)
                      if fsCoreLib.IsSome then
                        yield sprintf "-r:%s" fsCoreLib.Value
                      yield "--out:" + dllName
                      yield "--doc:" + xmlName
                      yield "--warn:3" 
                      yield "--fullpaths" 
                      yield "--flaterrors" 
                      yield "--target:library" 
                      for dllFile in dllFiles do
                          yield "-r:"+dllFile
                      for libDir in libDirs do
                          yield "-I:"+libDir
                      yield! otherFlags
                      yield fileName1 |])
          let results = checker.ParseAndCheckProject(options) |> Async.RunSynchronously
          if results.HasCriticalErrors then
              let builder = new System.Text.StringBuilder()
              for err in results.Errors do
                  builder.AppendLine(sprintf "**** %s: %s" (if err.Severity = Microsoft.FSharp.Compiler.FSharpErrorSeverity.Error then "error" else "warning") err.Message)
                  |> ignore
              failwith (builder.ToString())

          let references = results.ProjectContext.GetReferencedAssemblies()
          references
      let referenceMap references =
          references
          |> Seq.choose (fun (r:FSharpAssembly) -> r.FileName |> Option.map (fun f -> f, r))
      let resolve dllFiles references =
          let referenceMap = 
            referenceMap references
            |> dict
          dllFiles |> Seq.map (fun file -> file, if referenceMap.ContainsKey file then Some referenceMap.[file] else None)
        
      let getProjectReferencesSimple dllFiles = 
        let dllFiles = dllFiles |> Seq.toList
        getProjectReferences None None dllFiles
        |> resolve dllFiles
        
      let getProjectReferenceFromFile dllFile = 
          getProjectReferencesSimple [ dllFile ]
          |> Seq.exactlyOne
          |> snd

      let rec enumerateEntities (e:FSharpEntity) =
          [
              yield e
              yield! e.NestedEntities |> Seq.collect enumerateEntities
          ]
       
  type Type with
      /// The FullName but without any generic parameter types.
      member x.NamespaceName = 
          x.FullName.Substring(0, match x.FullName.IndexOf("[") with | -1 -> x.FullName.Length | _ as i -> i)

  type FSharpAssembly with
      static member LoadFiles (dllFiles, ?libDirs, ?otherFlags, ?manualResolve) =
        let resolveDirs = defaultArg manualResolve true
        let libDirs = defaultArg libDirs Seq.empty
        let dllFiles = dllFiles |> Seq.toList
        let findReferences libDir =
          Directory.EnumerateFiles(libDir, "*.dll")
          |> Seq.map Path.GetFullPath
          |> Seq.filter (fun file -> dllFiles |> List.exists (fun binary -> binary = file) |> not)
        
        // See https://github.com/tpetricek/FSharp.Formatting/commit/22ffb8ec3c743ceaf069893a46a7521667c6fc9d
        let blacklist =
          [ "FSharp.Core.dll"; "mscorlib.dll" ]

        // See https://github.com/tpetricek/FSharp.Formatting/commit/5d14f45cd7e70c2164a7448ea50a6b9995166489
        let _dllFiles, _libDirs =
          if resolveDirs then
            libDirs
            |> Seq.collect findReferences
            |> Seq.append dllFiles
            |> Seq.filter (fun file -> blacklist |> List.exists (fun black -> black = Path.GetFileName file) |> not),
            Seq.empty
          else dllFiles |> List.toSeq, libDirs
        FSharpAssemblyHelper.getProjectReferences otherFlags (Some _libDirs) _dllFiles
        |> FSharpAssemblyHelper.resolve dllFiles
            

      static member FromAssembly (assembly:Assembly) =
          let isWindows = System.Environment.OSVersion.Platform = System.PlatformID.Win32NT
          let loc =
              if isWindows && assembly.GetName().Name = "FSharp.Core" then
                  FSharpAssemblyHelper.findFSCore [] []
              else
                  assembly.Location
          if loc = null then None
          else FSharpAssemblyHelper.getProjectReferenceFromFile loc

      member x.FindType (t:Type) =
          x.Contents.Entities 
              |> Seq.collect FSharpAssemblyHelper.enumerateEntities 
              |> Seq.tryPick (fun entity ->
                  let namespaceName = t.NamespaceName.Replace("+", ".")
                  match entity.TryFullName with
                  | Some fullName when namespaceName = fullName -> 
                      Some entity
                  | _ -> None)


  module internal TypeNameHelper =
      let rec fallbackName (t:System.Type) =
          t.Name
      and getFSharpTypeName (t:System.Type) =
          let optFsharpName = 
              match FSharpAssembly.FromAssembly t.Assembly with
              | Some fsAssembly ->
                  match fsAssembly.FindType t with
                  | Some entity -> Some entity.DisplayName
                  | None -> None
              | None -> None
          match optFsharpName with
          | Some fsharpName -> fsharpName
          | None -> fallbackName t

  type Type with
      /// The name of the current type instance in F# source code.
      member x.FSharpName = TypeNameHelper.getFSharpTypeName x
      /// Gets the FullName of the current type in F# source code.
      member x.FSharpFullName = x.Namespace + "." + x.FSharpName 
      
  module internal TypeParamHelper =
      let rec getFSharpTypeParameterList (t:System.Type) =
          let builder = new System.Text.StringBuilder()
          if t.IsGenericType then
              let args = t.GetGenericArguments()
              builder.Append "<" |> ignore
              if t.IsGenericTypeDefinition then
                  args |> Seq.iter (fun t -> builder.Append "_," |> ignore)
              else
                  args |> Seq.iter (fun t -> builder.Append (sprintf "%s," (t.FSharpFullName + getFSharpTypeParameterList t)) |> ignore)
              builder.Length <- builder.Length - 1
              builder.Append ">" |> ignore
          builder.ToString() 

  type Type with
      /// The parameter list of the current type, sets "_" if the current instance is a generic definition.
      member x.FSharpParamList = TypeParamHelper.getFSharpTypeParameterList x
      /// Gets a string that can be used in F# source code to reference the current type instance.
      member x.FSharpFullNameWithTypeArgs = x.FSharpFullName + x.FSharpParamList

type InteractionResult =
  { Output : string; Error : string }
/// Represents a simple F# interactive session.
#if YAAF_FSHARP_SCRIPTING_PUBLIC
type IFsiSession =
#else 
type internal IFsiSession =
#endif
    /// Evaluate the given interaction.
    abstract member EvalInteractionWithOutput : string -> InteractionResult
    /// Try to evaluate the given expression and return its result.
    abstract member TryEvalExpressionWithOutput : string -> InteractionResult * ((obj * System.Type) option)
    /// Evaluate the given script.
    abstract member EvalScriptWithOutput : string -> InteractionResult

[<AutoOpen>]
#if YAAF_FSHARP_SCRIPTING_PUBLIC
module Extensions =
#else
module internal Extensions =
#endif
  type IFsiSession with
      member x.EvalInteraction s = x.EvalInteractionWithOutput s |> ignore
      member x.TryEvalExpression s = x.TryEvalExpressionWithOutput s |> snd
      member x.EvalScript s = x.EvalScriptWithOutput s |> ignore
      
      member x.EvalExpressionWithOutput<'a> text =
        match x.TryEvalExpressionWithOutput text with
        | int, Some (value, _) ->
          match value with
          | :? 'a as v -> int, v
          | o -> failwithf "the returned value (%O) doesn't match the expected type (%A) but has type %A" o (typeof<'a>) (o.GetType())
        | _ -> failwith "no value was returned by expression: %s" text  
      /// Evaluate the given expression and return its result.
      member x.EvalExpression<'a> text = 
        x.EvalExpressionWithOutput<'a> text |> snd
      /// Assigns the given object to the given name (ie "let varName = obj") 
      member x.Let<'a> varName obj =
          let typeName = typeof<'a>.FSharpFullNameWithTypeArgs
          x.EvalInteraction (sprintf "let mutable __hook = ref Unchecked.defaultof<%s>" typeName)
          let __hook = x.EvalExpression<'a ref> "__hook"
          __hook := obj
          x.EvalInteraction (sprintf "let %s = !__hook" varName)
          
      member x.Open ns = 
          x.EvalInteraction (sprintf "open %s" ns)
      member x.Reference file = 
          x.EvalInteraction (sprintf "#r @\"%s\"" file)

#if YAAF_FSHARP_SCRIPTING_PUBLIC
module Shell =
#else
module internal Shell =
#endif
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
    let mutable args = System.Environment.GetCommandLineArgs()
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

module internal ArgParser =
  let (|StartsWith|_|) start (s:string) =
    if s.StartsWith (start) then
      StartsWith(s.Substring(start.Length))
      |> Some
    else 
      None
  let (|FsiBoolArg|_|) argName s =
    match s with
    | StartsWith argName rest ->
      match rest with
      | null | "" | "+" -> Some true
      | "-" -> Some false
      | _ -> None
    | _ -> None
open ArgParser
#if YAAF_FSHARP_SCRIPTING_PUBLIC
type DebugMode =
#else
type internal DebugMode =
#endif
  | Full
  | PdbOnly
  | NoDebug
  
#if YAAF_FSHARP_SCRIPTING_PUBLIC
type OptimizationType =
#else
type internal OptimizationType =
#endif
  | NoJitOptimize
  | NoJitTracking
  | NoLocalOptimize
  | NoCrossOptimize
  | NoTailCalls
 
/// See https://msdn.microsoft.com/en-us/library/dd233172.aspx
#if YAAF_FSHARP_SCRIPTING_PUBLIC
type FsiOptions =
#else
type internal FsiOptions =
#endif
  { Checked : bool option
    Codepage : int option
    CrossOptimize : bool option
    Debug : DebugMode option
    Defines : string list
    Exec : bool
    FullPaths : bool
    Gui : bool option
    LibDirs : string list
    Loads : string list
    NoFramework : bool
    NoLogo : bool
    NonInteractive : bool
    NoWarns : int list
    Optimize : (bool * OptimizationType list) list
    Quiet : bool
    QuotationsDebug : bool
    ReadLine : bool option
    References : string list
    TailCalls : bool option
    Uses : string list
    Utf8Output : bool
    /// Sets a warning level (0 to 5). The default level is 3. Each warning is given a level based on its severity. Level 5 gives more, but less severe, warnings than level 1.
    /// Level 5 warnings are: 21 (recursive use checked at runtime), 22 (let rec evaluated out of order), 45 (full abstraction), and 52 (defensive copy). All other warnings are level 2.
    WarnLevel : int option
    WarnAsError : bool option
    WarnAsErrorList : (bool * int list) list
    ScriptArgs : string list }
  static member Empty =
    { Checked = None
      Codepage = None
      CrossOptimize = None
      Debug = None
      Defines = []
      Exec = false
      FullPaths = false
      Gui = None
      LibDirs  = []
      Loads  = []
      NoFramework = false
      NoLogo = false
      NonInteractive = false
      NoWarns  = []
      Optimize = []
      Quiet = false
      QuotationsDebug = false
      ReadLine = None
      References  = []
      TailCalls = None
      Uses  = []
      Utf8Output = false
      /// Sets a warning level (0 to 5). The default level is 3. Each warning is given a level based on its severity. Level 5 gives more, but less severe, warnings than level 1.
      /// Level 5 warnings are: 21 (recursive use checked at runtime), 22 (let rec evaluated out of order), 45 (full abstraction), and 52 (defensive copy). All other warnings are level 2.
      WarnLevel= None
      WarnAsError = None
      WarnAsErrorList = []
      ScriptArgs  = [] } 
  static member Default =
    let includes =
      if isMono then
        // Workaround that FSC doesn't find a FSharp.Core.dll
        let runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
        let monoDir = System.IO.Path.GetDirectoryName runtimeDir
        // prefer current runtime (which FSC would find anyway, but fallback to 4.0 if nothing is found in 4.5 or higher)
        // See also https://github.com/fsharp/fsharp/pull/389, https://github.com/fsharp/fsharp/pull/388
        [ runtimeDir; System.IO.Path.Combine (monoDir, "4.0") ]
      else []
    { FsiOptions.Empty with
        LibDirs = includes
        NonInteractive = true }
  static member ofArgs args =
    args 
    |> Seq.fold (fun (parsed, state) (arg:string) ->
      match state, arg with
      | (false, Some cont), _ when not (arg.StartsWith ("--")) ->
        let parsed, (userArgs, newCont) = cont arg
        parsed, (userArgs, unbox newCont)
      | _, "--" -> parsed, (true, None)
      | (true, _), a -> { parsed with ScriptArgs = a :: parsed.ScriptArgs }, state
      | _, FsiBoolArg "--checked" enabled ->
        { parsed with Checked = Some enabled }, state
      | _, StartsWith "--codepage:" res -> { parsed with Codepage = Some (int res) }, state
      | _, FsiBoolArg "--crossoptimize" enabled -> 
        { parsed with CrossOptimize = Some enabled }, state
      | _, StartsWith "--debug:" "pdbonly"
      | _, StartsWith "-g:" "pdbonly" ->
        { parsed with Debug = Some DebugMode.PdbOnly }, state
      | _, StartsWith "--debug:" "full"
      | _, StartsWith "-g:" "full" 
      | _, FsiBoolArg "--debug" true
      | _, FsiBoolArg "-g" true ->
        { parsed with Debug = Some DebugMode.Full }, state
      | _, FsiBoolArg "--debug" false
      | _, FsiBoolArg "-g" false ->
        { parsed with Debug = Some DebugMode.NoDebug }, state
      | _, StartsWith "-d:" def 
      | _, StartsWith "--define:" def ->
        { parsed with Defines = def :: parsed.Defines }, state
      | _, "--exec" ->
        { parsed with Exec = true }, state
      | _, "--noninteractive" ->
        { parsed with NonInteractive = true }, state
      | _, "--fullpaths" ->
        { parsed with FullPaths = true }, state
      | _, FsiBoolArg "--gui" enabled ->
        { parsed with Gui = Some enabled }, state
      | _, StartsWith "-I:" lib 
      | _, StartsWith "--lib:" lib ->
        { parsed with LibDirs = lib :: parsed.LibDirs }, state
      | _, StartsWith "--load:" load ->
        { parsed with Loads = load :: parsed.Loads }, state
      | _, "--noframework" ->
        { parsed with NoFramework = true }, state
      | _, "--nologo" ->
        { parsed with NoLogo = true }, state
      | _, StartsWith "--nowarn:" warns ->
        let noWarns =
          warns.Split([|','|])
          |> Seq.map int
          |> Seq.toList
        { parsed with NoWarns = noWarns @ parsed.NoWarns }, state
      | _, FsiBoolArg "--optimize" enabled ->
        let cont (arg:string) =
          let optList =
            arg.Split([|','|])
            |> Seq.map (function
              | "nojitoptimize" -> NoJitOptimize
              | "nojittracking" -> NoJitTracking
              | "nolocaloptimize" -> NoLocalOptimize
              | "nocrossoptimize" -> NoCrossOptimize
              | "notailcalls" -> NoTailCalls
              | unknown -> failwithf "Unknown optimization option %s" unknown)
            |> Seq.toList
          { parsed with Optimize = (enabled, optList) :: parsed.Optimize}, (false, box None)
        { parsed with Optimize = (enabled, []) :: parsed.Optimize}, (false, Some cont)
      | _, "--quiet" ->
        { parsed with Quiet = true }, state
      | _, "--quotations-debug" ->
        { parsed with QuotationsDebug = true }, state
      | _, FsiBoolArg "--readline" enabled ->
        { parsed with ReadLine = Some enabled }, state
      | _, StartsWith "-r:" ref
      | _, StartsWith "--reference:" ref ->
        { parsed with References = ref :: parsed.References }, state
      | _, FsiBoolArg "--tailcalls" enabled ->
        { parsed with TailCalls = Some enabled }, state
      | _, StartsWith "--use:" useFile ->
        { parsed with Uses = useFile :: parsed.Uses }, state
      | _, "--utf8output" ->
        { parsed with Utf8Output = true }, state
      | _, StartsWith "--warn:" warn ->
        { parsed with WarnLevel = Some (int warn) }, state
      | _, FsiBoolArg "--warnaserror" enabled ->
        { parsed with WarnAsError = Some enabled }, state
      | _, StartsWith "--warnaserror" warnOpts ->
        let parseList (l:string) =
          l.Split [|','|]
          |> Seq.map int
          |> Seq.toList
        match warnOpts.[0], if warnOpts.Length > 1 then Some warnOpts.[1] else None with
        | ':', _ -> 
          { parsed with WarnAsErrorList = (true, parseList (warnOpts.Substring 1)) :: parsed.WarnAsErrorList }, state
        | '+', Some ':' ->
          { parsed with WarnAsErrorList = (true, parseList (warnOpts.Substring 2)) :: parsed.WarnAsErrorList }, state 
        | '-', Some ':' ->
          { parsed with WarnAsErrorList = (false, parseList (warnOpts.Substring 2)) :: parsed.WarnAsErrorList }, state
        | _ -> failwithf "invalid --warnaserror argument: %s" arg
      | _, unknown -> { parsed with ScriptArgs = unknown :: parsed.ScriptArgs }, (true, None)
    ) (FsiOptions.Empty, (false, None))
    |> fst
    |> (fun p -> 
      { p with
          ScriptArgs = p.ScriptArgs |> List.rev
          Defines = p.Defines |> List.rev
          References = p.References |> List.rev
          LibDirs = p.LibDirs |> List.rev
          Loads = p.Loads |> List.rev
          Uses = p.Uses |> List.rev })
  member x.AsArgs =
    let maybeArg opt =
      match opt with
      | Some a -> Seq.singleton a
      | None -> Seq.empty
    let maybeArgMap opt f =
      opt
      |> Option.map f
      |> maybeArg
    let getMinusPlus b = if b then "+" else "-"
    let getFsiBoolArg name opt =
      maybeArgMap opt (getMinusPlus >> sprintf "%s%s" name)
    let getSimpleBoolArg name b =
      if b then
        Some name
      else None
      |> maybeArg
    [|
      yield! getFsiBoolArg "--checked" x.Checked
      yield! maybeArgMap x.Codepage (fun i -> sprintf "--codepage:%d" i)
      yield! getFsiBoolArg "--crossoptimize" x.CrossOptimize
      yield! maybeArgMap x.Debug (function
        | Full -> "-g+"
        | PdbOnly -> "-g:pdbonly"
        | NoDebug -> "-g-")
      yield! x.Defines
             |> Seq.map (sprintf "--define:%s")
      yield! getSimpleBoolArg "--exec" x.Exec
      yield! getSimpleBoolArg "--fullpaths" x.FullPaths
      yield! getFsiBoolArg "--gui" x.Gui
      yield! x.LibDirs
             |> Seq.map (sprintf "-I:%s")
      yield! x.Loads
             |> Seq.map (sprintf "--load:%s")
      yield! getSimpleBoolArg "--noframework" x.NoFramework
      yield! getSimpleBoolArg "--nologo" x.NoLogo
      yield! getSimpleBoolArg "--noninteractive" x.NonInteractive
      
      yield! (match x.NoWarns with
              | [] -> None
              | l ->
                l
                |> Seq.map string
                |> String.concat "," 
                |> sprintf "--nowarn:%s"
                |> Some)
             |> maybeArg 
      yield!
        match x.Optimize with
        | [] -> Seq.empty
        | opts ->
          opts
          |> Seq.map (fun (enable, types) -> 
            seq {
              yield sprintf "--optimize%s" (getMinusPlus enable)
              match types with
              | [] -> ()
              | _ ->
                yield
                  types
                  |> Seq.map (function
                    | NoJitOptimize -> "nojitoptimize"
                    | NoJitTracking -> "nojittracking"
                    | NoLocalOptimize -> "nolocaloptimize"
                    | NoCrossOptimize -> "nocrossoptimize"
                    | NoTailCalls -> "notailcalls")
                  |> String.concat ","
            }
          )
        |> Seq.concat
        
      yield! getSimpleBoolArg "--quiet" x.Quiet
      yield! getSimpleBoolArg "--quotations-debug" x.QuotationsDebug
      yield! getFsiBoolArg "--readline" x.ReadLine
      
      yield! x.References
             |> Seq.map (sprintf "-r:%s")
             
      yield! getFsiBoolArg "--tailcalls" x.TailCalls
      yield! x.Uses
             |> Seq.map (sprintf "--use:%s")
             
      yield! getSimpleBoolArg "--utf8output" x.Utf8Output
      
      yield! maybeArgMap x.WarnLevel (fun i -> sprintf "--warn:%d" i) 
        
      yield! getFsiBoolArg "--warnaserror" x.WarnAsError

      yield! x.WarnAsErrorList 
             |> Seq.map (fun (enable, warnNums) -> 
               warnNums
               |> Seq.map string
               |> String.concat ","
               |> sprintf "--warnaserror%s:%s" (getMinusPlus enable))

      match x.ScriptArgs with
      | [] -> ()
      | l ->
        yield "--"
        yield! l
    |]  

module internal Helper =
  open System
  open System.Collections.Generic
  open Microsoft.FSharp.Compiler.Interactive.Shell
  open System.IO
  open System.Text
  open Microsoft.FSharp.Compiler.SourceCodeServices

  let getSession (fsi : obj, options : FsiOptions, reportGlobal) =
      // Intialize output and input streams
      let globalOut = new StringBuilder()
      let globalErr = new StringBuilder()
      let sbOut = new StringBuilder()
      let sbErr = new StringBuilder()
      let sbInput = new StringBuilder()
      let inStream = new StringReader("")
      let outStream = new StringWriter(sbOut)
      let errStream = new StringWriter(sbErr)

      // Build command line arguments & start FSI session

      let args =
        [| yield "C:\\fsi.exe"
           yield! options.AsArgs |]
      let saveOutput () =
        let out, err = sbOut.ToString(), sbErr.ToString()
        if reportGlobal then
          globalOut.Append(out) |> ignore
          globalErr.Append(err) |> ignore
        sbOut.Clear() |> ignore
        sbErr.Clear() |> ignore
        { Output = out; Error = err }
      let getMessages () =
        if reportGlobal then
          globalErr.ToString(), globalOut.ToString(), sbInput.ToString()
        else
          sbErr.ToString(), sbOut.ToString(), sbInput.ToString()

      let fsiSession =
        try
          let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration(fsi, false)
          let session = FsiEvaluationSession.Create(fsiConfig, args, inStream, outStream, errStream)
          saveOutput() |> ignore
          session
        with e ->
          let err, out, inp = getMessages()
          raise <| new Exception(
            sprintf "Error in creating a fsi session: %s\nConcrete exn: %A\nOutput: %s\nInput: %s" err e out inp,
            e)
    
      let save_ f text =
        try
          let res = f text
          saveOutput(), res
        with e ->
          let err, out, inp = getMessages()
          raise <| new Exception(
            sprintf "Evaluation of (\n%s\n) failed: %s\nConcrete exn: %A\nOutput: %s\nInput: %s" text err e out inp,
            e)
      
      let save f = 
          save_ (fun text ->
              if reportGlobal then
                sbInput.AppendLine(text) |> ignore
              f text)
      let saveScript f = 
          save_ (fun path ->
              if reportGlobal then
                sbInput.AppendLine(File.ReadAllText path) |> ignore
              f path)

      let evalInteraction = save fsiSession.EvalInteraction 
      let evalExpression = save fsiSession.EvalExpression
      let evalScript = saveScript fsiSession.EvalScript
      
      let session =
        { new IFsiSession with
            member x.EvalInteractionWithOutput text = evalInteraction text |> fst
            member x.EvalScriptWithOutput path = evalScript path |> fst
            member x.TryEvalExpressionWithOutput text = 
              let i, r = evalExpression text 
              i, r |> Option.map (fun r -> r.ReflectionValue, r.ReflectionType)
        }
      // This works around a FCS bug, I would expect "fsi" to be defined already...
      // This is probably not the case because we do not have any type with the correct signature loaded
      // We just compile ourself a forwarder to fix that.
      //session.Reference (typeof<Microsoft.FSharp.Compiler.Interactive.Shell.Settings.InteractiveSettings>.Assembly.Location)
      //session.Let "fsi" fsi
      session.Let "__rawfsi" (box fsi)
      session.EvalInteraction """
module __ReflectHelper =
  open System
  open System.Reflection
  let rec tryFindMember (name : string) (memberType : MemberTypes) (declaringType : Type) =
      match declaringType.GetMember
        ( name, 
          memberType, 
          ( System.Reflection.BindingFlags.Instance ||| 
            System.Reflection.BindingFlags.Public ||| 
            System.Reflection.BindingFlags.NonPublic)) with
      | [||] -> declaringType.GetInterfaces() |> Array.tryPick (tryFindMember name memberType)
      | [|m|] -> Some m
      | _ -> raise <| new System.Reflection.AmbiguousMatchException(sprintf "Ambiguous match for member '%s'" name)

  let getInstanceProperty (obj:obj) (nm:string) =
      let p = (tryFindMember nm System.Reflection.MemberTypes.Property <| obj.GetType()).Value :?> PropertyInfo
      p.GetValue(obj, [||]) |> unbox

  let setInstanceProperty (obj:obj) (nm:string) (v:obj) =
      let p = (tryFindMember nm System.Reflection.MemberTypes.Property <| obj.GetType()).Value :?> PropertyInfo
      p.SetValue(obj, v, [||]) |> unbox

  let callInstanceMethod0 (obj:obj) (typeArgs : System.Type []) (nm:string) =
      let m = (tryFindMember nm System.Reflection.MemberTypes.Method <| obj.GetType()).Value :?> MethodInfo
      let m = match typeArgs with [||] -> m | _ -> m.MakeGenericMethod(typeArgs)
      m.Invoke(obj, [||]) |> unbox

  let callInstanceMethod1 (obj:obj) (typeArgs : Type []) (nm:string) (v:obj) =
      let m = (tryFindMember nm System.Reflection.MemberTypes.Method <| obj.GetType()).Value :?> MethodInfo
      let m = match typeArgs with [||] -> m | _ -> m.MakeGenericMethod(typeArgs)
      m.Invoke(obj, [|v|]) |> unbox

  type ForwardEventLoop(ev) =
    member x.Inner = ev
    member x.Run () = 
      callInstanceMethod0 ev [||] "Run" : unit
    member x.Invoke<'T>(f:unit -> 'T) = 
      callInstanceMethod1 ev [| typeof<'T> |] "Invoke" f : 'T
    member x.ScheduleRestart() = 
      callInstanceMethod0 ev [||] "ScheduleRestart" : unit

  type ForwardingInteractiveSettings(fsiObj) =
    member self.FloatingPointFormat 
      with get() = getInstanceProperty fsiObj "FloatingPointFormat" : string
      and set (v:string) = setInstanceProperty fsiObj "FloatingPointFormat" v
    member self.FormatProvider 
      with get() = getInstanceProperty fsiObj "FormatProvider"  : System.IFormatProvider
      and set (v: System.IFormatProvider) = setInstanceProperty fsiObj "FormatProvider" v
    member self.PrintWidth  
      with get() = getInstanceProperty fsiObj "PrintWidth" :int
      and set (v:int) = setInstanceProperty fsiObj "PrintWidth" v
    member self.PrintDepth 
      with get() = getInstanceProperty fsiObj "PrintDepth" :int
      and set (v:int) = setInstanceProperty fsiObj "PrintDepth" v
    member self.PrintLength 
      with get() = getInstanceProperty fsiObj "PrintLength"  :int
      and set (v:int) = setInstanceProperty fsiObj "PrintLength" v
    member self.PrintSize 
      with get() = getInstanceProperty fsiObj "PrintSize"  :int
      and set (v:int) = setInstanceProperty fsiObj "PrintSize" v
    member self.ShowDeclarationValues 
      with get() = getInstanceProperty fsiObj "ShowDeclarationValues" :bool
      and set (v:bool) = setInstanceProperty fsiObj "ShowDeclarationValues" v
    member self.ShowProperties
      with get() = getInstanceProperty fsiObj "ShowProperties" :bool
      and set (v:bool) = setInstanceProperty fsiObj "ShowProperties" v
    member self.ShowIEnumerable 
      with get() = getInstanceProperty fsiObj "ShowIEnumerable" :bool
      and set (v:bool) = setInstanceProperty fsiObj "ShowIEnumerable" v
    member self.ShowIDictionary 
      with get() = getInstanceProperty fsiObj "ShowIDictionary" :bool
      and set (v:bool) = setInstanceProperty fsiObj "ShowIDictionary" v
    member self.AddedPrinters 
      with get() = getInstanceProperty fsiObj "AddedPrinters" : Choice<System.Type * (obj -> string), System.Type * (obj -> obj)> list
      and set (v:Choice<System.Type * (obj -> string), System.Type * (obj -> obj)> list) = setInstanceProperty fsiObj "AddedPrinters" v
    member self.CommandLineArgs
      with get() = getInstanceProperty fsiObj "CommandLineArgs" :string array
      and set (v:string array) = setInstanceProperty fsiObj "CommandLineArgs" v
    member self.AddPrinter(printer : 'T -> string) =
      callInstanceMethod1 fsiObj [|typeof<'T>|] "AddPrinter" printer : unit

    member self.EventLoop
      with get() = ForwardEventLoop(getInstanceProperty fsiObj "EventLoop")
      and set (v:ForwardEventLoop) = setInstanceProperty fsiObj "EventLoop" v.Inner

    member self.AddPrintTransformer(printer : 'T -> obj) =
      callInstanceMethod1 fsiObj [|typeof<'T>|] "AddPrintTransformer" printer
let fsi = __ReflectHelper.ForwardingInteractiveSettings(__rawfsi)"""
      session

#if YAAF_FSHARP_SCRIPTING_PUBLIC
type ScriptHost private() =
#else
type internal ScriptHost private() =
#endif
  /// Create a new IFsiSession by specifying all arguments manually.
  static member Create (opts : FsiOptions, ?fsiObj : obj, ?reportGlobal) =
    Helper.getSession(
      defaultArg fsiObj (Microsoft.FSharp.Compiler.Interactive.Shell.Settings.fsi :> obj), 
      opts,
      defaultArg reportGlobal false)
  /// Quickly create a new IFsiSession with some same defaults
  static member CreateNew (?defines : string list, ?fsiObj : obj, ?reportGlobal) =
    let opts =
      { FsiOptions.Default with
          Defines = defaultArg defines []
      }
    ScriptHost.Create(opts, ?fsiObj = fsiObj, ?reportGlobal = reportGlobal)
