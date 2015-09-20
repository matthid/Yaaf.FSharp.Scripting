module Test.Yaaf.FSharp.Scripting.ExtensionMethodTests
type T () =
    let _ = ()
type TGen<'a> () =
    let _ = ()
open System.IO
open NUnit.Framework
open Test.Yaaf.FSharp.Scripting.FsiUnquote
open Yaaf.FSharp.Scripting
open Microsoft.FSharp.Compiler.SourceCodeServices

let withNoWarnings f =
    let builder = new System.Text.StringBuilder()
    use writer = new StringWriter(builder)
    Log.source.Switch.Level <- System.Diagnostics.SourceLevels.Warning
    use listener =
        new System.Diagnostics.TextWriterTraceListener(
            writer,
            Filter = new System.Diagnostics.EventTypeFilter(System.Diagnostics.SourceLevels.Warning))
    let listenerIndex = Log.source.Listeners.Add(listener)
    f ()
    Log.source.Listeners.RemoveAt(listenerIndex)
    writer.Flush()
    let logText = builder.ToString()
    test <@ System.String.IsNullOrWhiteSpace logText @>

let testType<'a> () =
  withNoWarnings (fun () ->
    // A Assembly -> FSharpEntity mapping (extension) function
    let fsAssembly = FSharpAssembly.FromAssembly typeof<'a>.Assembly
    test <@ fsAssembly.IsSome @>
    test <@ fsAssembly.Value.FileName.IsSome @>
    // we don't require the exact same path (could be loaded from somewhere else)
    test <@ Path.GetFileName fsAssembly.Value.FileName.Value = Path.GetFileName typeof<'a>.Assembly.Location @>
    test <@ fsAssembly.Value.SimpleName = typeof<'a>.Assembly.GetName().Name @>
    // A extension method to find the type
    let fsType = fsAssembly.Value.FindType typeof<'a>
    test <@ fsType.IsSome @>
    test <@ fsType.Value.FullName = typeof<'a>.NamespaceName.Replace("+", ".") @>
    ())

[<Test>]
let ``check that default assemblies generate no warnings`` () =
    let frameworkVersion = FSharpAssemblyHelper.defaultFrameworkVersion
    let sysLibs = FSharpAssemblyHelper.getDefaultSystemReferences frameworkVersion
    let fsCore = FSharpAssemblyHelper.findFSCore [] []
    //for sysLib in sysLibs do
    let projFileName, args = FSharpAssemblyHelper.getCheckerArguments frameworkVersion sysLibs (Some fsCore) [] [] []
    let options = FSharpAssemblyHelper.checker.GetProjectOptionsFromCommandLineArgs(projFileName, args)
    let results = FSharpAssemblyHelper.checker.ParseAndCheckProject(options) |> Async.RunSynchronously
    test <@ results.Errors.Length = 0 @>

[<Test>]
let ``check that we can get the fsassembly and fstype of a custom nested type`` () =
    testType<T>()

[<Test>]
let ``check that we can get the fsassembly and fstype of a custom generic type`` () =
    testType<int TGen>()

[<Test>]
let ``check that we can get the fsassembly and fstype of a interface type`` () =
    testType<Yaaf.FSharp.Scripting.IFsiSession>()


[<Test>]
let ``check that we can get the fsassembly and fstype of int`` () =
    testType<int> ()

[<Test>]
let ``check that we can get the fsassembly and fstype of option`` () =
    testType<option<int>> ()

[<Test>]
let ``check that the type extensions works for typedefof`` () =
    
    let def = typedefof<option<int>>
    test <@ def.Name = "FSharpOption`1" @>
    test <@ def.FullName = "Microsoft.FSharp.Core.FSharpOption`1" @>

    test <@ def.FSharpParamList = "<_>" @>
    test <@ def.FSharpFullName = "Microsoft.FSharp.Core.Option" @>
    test <@ def.FSharpFullNameWithTypeArgs = "Microsoft.FSharp.Core.Option<_>" @>
    test <@ def.FSharpName = "Option" @>

[<Test>]
let ``check that the type extensions works for typeof`` () =
    
    let def = typeof<option<int>>
    test <@ def.Name = "FSharpOption`1" @>
    test <@ def.Namespace = "Microsoft.FSharp.Core" @>

    test <@ def.FSharpParamList = "<System.Int32>" @>
    test <@ def.FSharpFullName = "Microsoft.FSharp.Core.Option" @>
    test <@ def.FSharpFullNameWithTypeArgs = "Microsoft.FSharp.Core.Option<System.Int32>" @>
    test <@ def.FSharpName = "Option" @>