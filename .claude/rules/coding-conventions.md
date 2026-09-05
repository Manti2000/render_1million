# Coding Conventions

## Script Structure
Order members:
1. Constants and static fields/properties
2. Inspector fields
3. Public properties
4. Events
5. Private fields/properties for script state
6. Methods (lifecycle → public interface → responsibility-specific helpers)

## Formatting
- Separate scripts into `#region` blocks — no blank line between `#region` lines and surrounding code
- Explicit `private` access modifier on private fields, methods, properties
- Private fields use `_camelCase`
- Inline comment next to non-trivial fields explaining purpose
- In longer methods with distinct sections, use inline comment separators padded with dashes (e.g. `// Section name -----...`), all ending at the same column within a file, with a closing separator (`// -----...`) after the last section
- Only use `var` when the type is obvious from the right side
- Omit curly braces where possible; prefer code that reads linearly top to bottom
- Minimize nesting with early exits — use guard clauses (`return`, `continue`, `break`) for edge cases upfront; prefer this over `if`-wrapping the happy path
- Do not use local functions — extract them as private methods instead
- One responsibility per method, struct, class
- Remove unused code rather than leaving it in (unless part of the public interface)
- Wrap fields/constants only used in specific preprocessor contexts with matching `#if` directives, with an inline comment on the `#if` line explaining why
- Keep method signatures on a single line. If a signature feels too long, restructure (fewer parameters, group into a struct, split responsibility) — don't wrap

## Method Structure
- Orchestrator methods read as a table of contents — coordinate distinct steps by calling well-named private helpers (`TryAdvanceWallClock`, `SnapshotPeakKeys`, `ShrinkSource`). Orchestrator body stays flat: a couple of guards, straight-line helper calls, at most one loop whose body is a single helper call. Nested loops, branching, and multi-line inline blocks belong in helpers.
- Helpers own their own documentation via name + XML doc — don't narrate steps in the orchestrator.

## Naming
- Names should be talkative — describe *what the thing represents*, so a reader can infer the role from the name alone without chasing the declaration.

## Documentation
- Use XML doc comments (`///`) for every documented member (methods, properties, classes, structs, enums). Never use `//` blocks above members — IDE tooltips and generated docs only read `///`.
- Use inline `//` comments only inside method bodies, for non-obvious logic.
- Inspector field docs use `[Tooltip("...")]`, not XML doc comments.

## Async
- Async methods return `UniTask` or `UniTask<T>`, never `async void` (except Unity event methods)

## Error Handling
- Do not `throw` from runtime/game code — log via `Debug.LogError` (or `LogWarning` for recoverable) with a `[Component]` tag prefix and return a safe default (null, early return, no-op). Exceptions are reserved for editor-only tools.

## Architecture
- Abstract base class pattern for extensibility
- Editor code in separate `Editor/` folders with dedicated assembly definitions
- Demo/project scripts in `Assets/Scripts/`, package code in the respective package
- Extend existing seams before cutting new ones. Watch for parallel-construction smells — a new dict beside an existing dict, a new event beside an existing event, names built by analogy (`_parkedTransfers` beside `_reconcileTransfers`, `ParkingPinOwner` beside `SelectionPinOwner`). That analogy is the signal the old thing should absorb the new role.

## UI Toolkit
- Define styling in a dedicated USS file rather than inline C# — unless the styling is minimal
