# Microsoft.ServiceFabric.Powershell.Http

PowerShell module for managing [Service Fabric](https://learn.microsoft.com/azure/service-fabric) clusters over HTTP. Provides
cmdlets for deploying and managing applications, services, nodes, and other cluster resources.

## Key Cmdlets

- `Connect-SFCluster` — connects to a Service Fabric cluster endpoint.
- `Get-SFApplication` / `New-SFApplication` / `Remove-SFApplication` — manage applications.
- `Get-SFService` / `New-SFService` / `Remove-SFService` — manage services.
- `Get-SFNode` — query cluster nodes.
- `Copy-SFApplicationPackage` — upload application packages to the image store.

## Usage

```powershell
# Connect to a local development cluster
Connect-SFCluster -ConnectionEndpoint http://localhost:19080

# List all applications
Get-SFApplication

# Deploy an application
Copy-SFApplicationPackage -ApplicationPackagePath .\MyAppPkg
Register-SFApplicationType -ImageStorePath -ApplicationTypeBuildPath MyAppPkg
New-SFApplication -Name fabric:/MyApp -TypeName MyApp -TypeVersion 1.0.0
```

### Code Package Patches

Set and Clear return per-node acceptance; Get reports whether replacement code is actually running. Set accepts one typed
Source: LocalDirectoryCodePackagePatchSource, ImageStoreCodePackagePatchSource, or ProvisionedCodePackagePatchSource.
The source Kind is an enum. CodePackageName and CodePackageVersion identify the original target, independently of the
replacement source. Local directories are uploaded automatically; no separate upload command is needed.

Local uploads read the source directly into a new `Store/<ApplicationType>/HotPatches/<ContentId>` publication, without a
snapshot or intermediate upload folder. Keep the source unchanged during upload. Completed content remains retained after
Set returns or fails; only incomplete uploads are eligible for best-effort deletion. Replacement access uses normal SF
deployment permissions, not preservation of the source machine's ACLs or exact Unix modes.

```powershell
Connect-SFCluster -ConnectionEndpoint http://localhost:19080
$source = [Microsoft.ServiceFabric.Common.LocalDirectoryCodePackagePatchSource]::new("$PWD/Hotfix")
$result = Set-SFCodePackagePatch -ServiceName fabric:/Orders/Worker -CodePackageName Worker.Code `
	-CodePackageVersion 1.0 -Source $source -Nodes N1,N2
Get-SFCodePackagePatch -PatchId $result.PatchId -Nodes 'N1,N2'
Clear-SFCodePackagePatch -PatchId $result.PatchId -Nodes N1,N2 -WhatIf
```

Set defaults to one 1800-second ServerTimeout across preparation, upload, and submission. These patch commands allow HTTP
to wait for the operation budget without changing unrelated requests' ClientTimeout. Get uses one CSV string for Nodes,
while Set and Clear accept arrays. A fresh Clear acts on the current mapping for the returned version-qualified PatchId,
and does not delete its Image Store publication. Set and Clear support WhatIf and Confirm; WhatIf performs no upload.

## Documentation

- [Manage applications using PowerShell](https://learn.microsoft.com/azure/service-fabric/service-fabric-deploy-remove-applications)
- [Connect to a secure cluster](https://learn.microsoft.com/azure/service-fabric/service-fabric-connect-to-secure-cluster)
- [Source code](https://github.com/microsoft/service-fabric-dotnet)
