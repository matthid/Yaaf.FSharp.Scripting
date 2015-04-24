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
          
      let getProjectReferences otherFlags libDirs dllFiles = 
          let dllFiles, hasCoreLib =
            dllFiles
            |> Seq.fold (fun (files, hasCoreLib) dllFile -> 
               dllFile :: files, 
               hasCoreLib || 
               let name = (Path.GetFileName dllFile).ToLowerInvariant()
               name = "fsharp.core.dll" || name = "mscorlib.dll" 
            ) ([], false)

          let otherFlags = defaultArg otherFlags Seq.empty
          let libDirs = defaultArg libDirs Seq.empty
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
                      if hasCoreLib then
                        yield "--noframework"
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
                  // TODO: handle more cases / versions.
                  // file references only valid on Windows 
                  // NOTE: we use 4.3.0.0 as even when you specify 4.3.1.0 you will get a 4.3.0.0 reference as result 
                  // (this will break above when we try to find for every file its reference)
                  sprintf @"C:\Program Files (x86)\Reference Assemblies\Microsoft\FSharp\.NETFramework\v4.0\4.3.0.0\FSharp.Core.dll"
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

/// Represents a simple F# interactive session.
#if YAAF_FSHARP_SCRIPTING_PUBLIC
type IFsiSession =
#else 
type internal IFsiSession =
#endif
    /// Evaluate the given interaction.
    abstract member EvalInteraction : string -> unit
    /// Try to evaluate the given expression and return its result.
    abstract member TryEvalExpression : string -> (obj * System.Type) option
    /// Evaluate the given script.
    abstract member EvalScript : string -> unit

[<AutoOpen>]
#if YAAF_FSHARP_SCRIPTING_PUBLIC
module Extensions =
#else
module internal Extensions =
#endif
  type IFsiSession with
      /// Evaluate the given expression and return its result.
      member x.EvalExpression<'a> text = 
        match x.TryEvalExpression text with
        | Some (value, _) ->
          match value with
          | :? 'a as v -> v
          | o -> failwithf "the returned value (%O) doesn't match the expected type (%A) but has type %A" o (typeof<'a>) (o.GetType())
        | _ -> failwith "no value was returned by expression: %s" text 
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
    
module internal Helper =
  open System
  open System.Collections.Generic
  open Microsoft.FSharp.Compiler.Interactive.Shell
  open System.IO
  open System.Text
  open Microsoft.FSharp.Compiler.SourceCodeServices

  let getSession (defines, fsi : obj) =
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
          let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration(fsi, false)
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
      
      let session =
        { new IFsiSession with
            member x.EvalInteraction text = evalInteraction text
            member x.EvalScript path = evalScript path
            member x.TryEvalExpression text = 
              evalExpression text |> Option.map (fun r -> r.ReflectionValue, r.ReflectionType)
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
    static member CreateNew (?defines : string list, ?fsiObj : obj) =
        Helper.getSession(defaultArg defines [], defaultArg fsiObj (Microsoft.FSharp.Compiler.Interactive.Shell.Settings.fsi :> obj))
