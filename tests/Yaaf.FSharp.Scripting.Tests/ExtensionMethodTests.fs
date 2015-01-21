module Test.Yaaf.FSharp.Scripting.ExtensionMethodTests
type T () =
    let t = ()
open System.IO
open NUnit.Framework
open Swensen.Unquote
open Yaaf.FSharp.Scripting
open Microsoft.FSharp.Compiler.SourceCodeServices
let testType<'a> () =
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
    ()
[<Test>]
let ``check that we can get the fsassembly and fstype of a custom nested type`` () =
    testType<T>()

[<Test>]
let ``check that we can get the fsassembly and fstype of a custom generic type`` () =
    testType<Yaaf.FSharp.Scripting.HookHelper<int>>()

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