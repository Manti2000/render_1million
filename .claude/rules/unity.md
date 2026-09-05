# Unity-Specific Rules

## Null Checks
- Interface references to Unity objects bypass Unity's overloaded `==` operator — `myInterface != null` won't detect destroyed objects. Always null-check via the underlying `Object` reference (e.g., cast `(Object)myInterface != null`) or gate on the `Object` field that the interface was assigned from.

## Inspector
- Always use `[SerializeField]` and private fields for inspector fields

## Performance
- Write performance-critical code with `[BurstCompile]` in mind — use `NativeArray`, `Unity.Mathematics` types, and struct-based jobs where possible
- Minimize GC allocations where possible, but not at the expense of code readability or quality

## Assembly Definitions
- Unless a Unity MCP server is available, do not search for or modify asmdef GUID references — just warn the user about any needed asmdef changes and let them handle it manually
