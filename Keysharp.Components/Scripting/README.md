# Optional scripting units

Keysharp treats source processing as two optional, first-party deployment units:

- `parser` contains the lexer, parser, internal AST, and Roslyn-free syntax validation.
- `compiler` contains lowering and Roslyn compilation. It depends on the parser assembly internally, but does not advertise syntax validation; a host which asks to validate syntax must have the dedicated `parser` unit.

`Keysharp.Core` references only the small `Keysharp.Components.Scripting` contract assembly. A compiled script which does not process source therefore needs neither implementation unit nor Roslyn.

## Identity and compatibility

The IDs are fixed. This boundary supports optional first-party packaging and capability discovery; it is not a general third-party provider-selection API. Each `component.json` declares:

- `schemaVersion`: the descriptor format version;
- `contractVersion`: the host/component API version;
- `id`: `parser` or `compiler`;
- the implementation assembly and type;
- the exact capability and payload files.

The registry rejects unknown IDs, capabilities, schema versions, and contract versions. `ComponentAvailable` means the matching unit was found, validated, and loaded successfully. Search roots may contain another copy of the same unit, so a broken earlier copy falls through to a valid later one.

## Deployment

Normal executables and `.cks` files deploy required units below `components/scripting/<id>`. The runtime searches beside the executing artifact before its own installation. Minimal executables embed an integrity manifest and component assets, verify their SHA-256 hashes, then extract them to a versioned per-user cache on first use.

Compiler requirements are recorded while lowering statically visible `RunScript` and `ParseScript` calls. `--with-*` adds a unit explicitly and `--without-*` overrides automatic inclusion. Raw assembly output to stdout is rejected when sidecars are required because there is no destination in which to deploy them.

The custom load context shares only assemblies whose type identity crosses the boundary: Core, the scripting and package contracts, and Semver. Other dependencies resolve relative to the unit, avoiding broad host-version substitution.
