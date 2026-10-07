# HTTP Client Generation

Generate HTTP interfaces, models, converters, and PowerShell output from the matching SF-Swagger change. Keep unrelated
API output intact when the Swagger and client checkouts are on different baselines.

## Code Package Patches

- Keep the handwritten `Common/CodePackagePatchSourceKind.cs` instead of copying the generated enum of the same name.
  It adds client-only LocalDirectory while preserving ImageStore and ProvisionedCodePackage values. The REST schema
  contains only the two stored-reference kinds. The pinned generator cannot skip this referenced enum through its annotation.
- Preserve `Client.Http/CodePackageClient.cs`: it validates the source, resolves its target owner, uploads directly to a new
  retained HotPatches path, and scopes query escaping and deadlines before calling generated methods. Local directories
  must never reach the generated HTTP serializer. Reuse Image Store upload operations without adding a local snapshot or
  an intermediate remote folder. Completed publications must not be deleted when Set returns or fails.
- Directory uploads use streamed ordinary file PUTs, supported by both Windows and Linux gateways. Do not select the
  Windows-only upload-session routes based only on file size. Each retry must reopen the file; completion markers follow
  successful file transfers. Derive relative paths from the source-root boundary, not a replace-all or fixed extra offset.
- Preserve the handwritten Set/Clear PowerShell wrappers. The pinned generator merges target/source package names and
  generates collection parameters that ordinary PowerShell arrays cannot bind to. Get remains generated.
- Preserve the Set/Clear help entries as well. After building the PowerShell module, run
  `eng/UpdateCodePackagePatchHelp.ps1 -GeneratedHelp <generated-help> -ModulePath <built-module> -TargetHelp <repo-help>`
  in a separate PowerShell process. This regenerates their metadata from the compiled wrappers, imports generated Get help,
  and preserves unrelated commands.

Run `test/Client.Http` and the isolated `test/Powershell.Http/CodePackagePatchTests.ps1` checks after regeneration. Their fake
HTTP handlers validate uploads, publication ordering, cancellation, remaining deadlines, and typed command binding without
a cluster. The `code-package-patch` workflow runs the PowerShell checks on Windows and Linux. Runtime behavior and version
compatibility require a matching WindowsFabric change.