# Yaaf.FSharp.Scripting

This library tries to improve the scripting capabilities of FSharp.

It builds on top of FSharp.Compiler.Service but provides a nice an clean API:

```fsharp
open Yaaf.FSharp.Scripting
let fsiSession = ScriptHost.CreateNew()
fsiSession.Reference (@"C:\MyAssembly.dll")
fsiSession.Open ("MyAssembly")

// hook 25 into the session with name test (ie "let test = 25")
// this works with any object
fsiSession.Let "test" 25

// Get a value out of the evaluator
let v = fsiSession.EvalExpression<int> "test"
assert (v = 25)
```



The library also provides some nice members which are missing in FSharp.Compiler.Service:

```fsharp
open Yaaf.FSharp.Scripting
open Microsoft.FSharp.Compiler.SourceCodeServices

// A Assembly -> FSharpEntity mapping (extension) function
let fsAssembly = FSharpAssembly.FromAssembly typeof<MyType>.Assembly

// A extension method to find the entity type
let fsType = fsAssembly.Value.FindType typeof<MyType>
```

And some extensions for System.Type to get the FSharp type name

```fsharp
// with concrete types.
let def = typeof<option<int>>
test <@ def.Name = "FSharpOption`1" @>
test <@ def.Namespace = "Microsoft.FSharp.Core" @>

test <@ def.FSharpParamList = "<System.Int32>" @>
test <@ def.FSharpFullName = "Microsoft.FSharp.Core.Option" @>
test <@ def.FSharpFullNameWithTypeArgs = "Microsoft.FSharp.Core.Option<System.Int32>" @>
test <@ def.FSharpName = "Option" @>

// With typedefs.
let def = typedefof<option<int>>
test <@ def.Name = "FSharpOption`1" @>
test <@ def.FullName = "Microsoft.FSharp.Core.FSharpOption`1" @>

test <@ def.FSharpParamList = "<_>" @>
test <@ def.FSharpFullName = "Microsoft.FSharp.Core.Option" @>
test <@ def.FSharpFullNameWithTypeArgs = "Microsoft.FSharp.Core.Option<_>" @>
test <@ def.FSharpName = "Option" @>

```

