namespace Yaaf.FSharp.Scripting

/// Represents a simple F# interactive session.
type IFsiSession =
    /// Evaluate the given interaction.
    abstract member EvalInteraction : string -> unit
    /// Evaluate the given expression and return its result.
    abstract member EvalExpression<'a> : string -> 'a
    /// Evaluate the given script.
    abstract member EvalScript : string -> unit

/// A type used internally to hook objects into the interactive. Do not use it in code.
type HookHelper<'a> =
    { mutable item : 'a option}
    static member Empty = { item = (None : 'a option) }