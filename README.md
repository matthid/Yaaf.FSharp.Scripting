# Yaaf.FSharp.Scripting

## [Documentation](https://matthid.github.io/Yaaf.FSharp.Scripting/)

[![Join the chat at https://gitter.im/matthid/Yaaf](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/matthid/Yaaf?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

## Build status

**Development Branch**

[![Build Status](https://travis-ci.org/matthid/Yaaf.FSharp.Scripting.svg?branch=develop)](https://travis-ci.org/matthid/Yaaf.FSharp.Scripting)
[![Build status](https://ci.appveyor.com/api/projects/status/od970xa4gvkh4tea/branch/develop?svg=true)](https://ci.appveyor.com/project/matthid/yaaf-fsharp/branch/develop)

**Master Branch**

[![Build Status](https://travis-ci.org/matthid/Yaaf.FSharp.Scripting.svg?branch=master)](https://travis-ci.org/matthid/Yaaf.FSharp.Scripting)
[![Build status](https://ci.appveyor.com/api/projects/status/od970xa4gvkh4tea/branch/master?svg=true)](https://ci.appveyor.com/project/matthid/yaaf-fsharp/branch/master)

## NuGet

<div class="row">
  <div class="span1"></div>
  <div class="span6">
    <div class="well well-small" id="nuget">
      The Yaaf.FSharp.Scripting library can be <a href="https://nuget.org/packages/Yaaf.FSharp.Scripting">installed from NuGet</a>:
      <pre>PM> Install-Package Yaaf.FSharp.Scripting</pre>
    </div>
  </div>
  <div class="span1"></div>
</div>

## Include as source file 

### Paket

You can include the functionality directly into your application by using paket source files:

`paket.dependencies`:

```
github matthid/Yaaf.FSharp.Scripting src/source/Yaaf.FSharp.Scripting/YaafFSharpScripting.fs
```

`paket.references`:

```
File: YaafFSharpScripting.fs 
```

See http://fsprojects.github.io/Paket/github-dependencies.html for details.


### NuGet 

The `YaafFSharpScripting.fs` source code file is included in the nuget package as well.
You can find it in `content/YaafFSharpScripting.fs`.
By adding the source code file (as reference) to your project you don't need
to add a nuget dependency (`Yaaf.FSharp.Scripting`) to your final package. 

## Quick intro

This library tries to improve the scripting capabilities of FSharp.

It builds on top of FSharp.Compiler.Service but provides a nice an clean API:

```fsharp
open Yaaf.FSharp.Scripting
use fsiSession = ScriptHost.CreateNew()
fsiSession.Reference (@"C:\MyAssembly.dll")
fsiSession.Open ("MyAssembly")

// hook 25 into the session with name test (ie "let test = 25")
// this works with any object
fsiSession.Let "test" 25

// Get a value out of the evaluator
let v = fsiSession.EvalExpression<int> "test"
assert (v = 25)

// Try to get a value (with handling error cases)
match fsiSession.Handle<int> fsiSession.EvalExpression "test" with
| InvalidExpressionType e -> 
  // not of type int
  // e.Value contains the result object, e.ExpectedType the expected type (int in this case)
  ()
| InvalidCode e -> 
  // couldn't get app value (compiler error, not defined or exception in the running code)
  // e.Input is the given text, e.Result is the compiler or the script output
  // Note: script exceptions are written as strings within e.Result
  ()
| Result r -> 
  // r is the value.
  ()

// Get the output of the snippet
let v = fsiSession.EvalInteractionWithOutput """printf "test" """
assert (v.Output.ScriptOutput = "test")

// Get the error message of the compilation
try fsiSession.EvalInteraction """ Some_Invalid_F# """
with :? FsiEvaluationException ev ->
    printfn "FSI said: %s" ev.Result.Error.FsiOutput
    printfn "Complete Error: %O" ev

// Load scripts
fsiSession.EvalScript "Script.fsx"

```

The library also provides some nice members which are missing in FSharp.Compiler.Service:

```fsharp
open Yaaf.FSharp.Scripting
open FSharp.Compiler.SourceCodeServices

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

[Examples and configuration overview](https://matthid.github.io/Yaaf.FSharp.Scripting/IntroExamples.html) 

 