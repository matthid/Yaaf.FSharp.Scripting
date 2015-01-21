[<AutoOpen>]
module Yaaf.FSharp.Scripting.CompilerServiceExtensions

open System
open System.Reflection
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.Interactive.Shell
open Microsoft.FSharp.Compiler.SourceCodeServices

module internal FSharpAssemblyHelper =
    open System.IO
    let checker = FSharpChecker.Create()
        
    let getProjectReferences otherFlags libDirs dllFiles = 
        let otherFlags = defaultArg otherFlags []
        let libDirs = defaultArg libDirs []
        let base1 = Path.GetTempFileName()
        let dllName = Path.ChangeExtension(base1, ".dll")
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
                    yield "--out:" + dllName
                    yield "--doc:test.xml" 
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
        
    let getProjectReferencesSimple dllFiles = 
        let references =
            getProjectReferences None None dllFiles
            |> Seq.choose (fun r -> r.FileName |> Option.map (fun f -> f, r))
            |> dict
        dllFiles |> List.map (fun file -> references.[file])
    let getProjectReferenceFromFile dllFile = 
        getProjectReferencesSimple [ dllFile ]
        |> Seq.exactlyOne

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
        else Some (FSharpAssemblyHelper.getProjectReferenceFromFile loc)

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