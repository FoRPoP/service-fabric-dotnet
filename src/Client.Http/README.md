# Microsoft.ServiceFabric.Client.Http

An HTTP client for connecting to [Service Fabric](https://learn.microsoft.com/azure/service-fabric) clusters and managing
applications, services, partitions, nodes, and other cluster resources.

## Key Types

- `ServiceFabricClientBuilder` — builder for configuring cluster endpoints, security, and retry settings.
- `IServiceFabricClient` — the main client interface, exposing API groups for applications, services, nodes, cluster operations,
  and more.

## Usage

Connect to a local development cluster:

```csharp
IServiceFabricClient client = await new ServiceFabricClientBuilder()
    .UseEndpoints(new Uri("http://localhost:19080"))
    .BuildAsync();

PagedData<NodeInfo> nodes = await client.Nodes.GetNodeInfoListAsync();
```

Connect to a secured cluster:

```csharp
IServiceFabricClient client = await new ServiceFabricClientBuilder()
    .UseEndpoints(new Uri("https://mycluster.eastus.cloudapp.azure.com:19080"))
    .UseX509Security(cancellation => Task.FromResult<SecuritySettings>(
        new X509SecuritySettings(clientCertificate, remoteSecuritySettings)))
    .BuildAsync();
```

### Code Package Patches

Set and Clear return per-node acceptance, not proof that replacement code is running. Get reports saved mappings,
content readiness, and process observations. One typed Source supplies a local directory, an Image Store path, or a
provisioned code-package reference. Local directories are uploaded automatically using existing Image Store operations
directly into `Store/<ApplicationType>/HotPatches/<ContentId>` before Set submits the stored reference. Stream inputs are
not supported.

Set uses one 1800-second default deadline for local preparation, upload, publication, and submission. HTTP requests use the
remaining budget rather than the connection's shorter default timeout. Other APIs retain their existing connection timeout.
Cancellation or timeout does not undo work already accepted by a node.

```csharp
IServiceFabricClient client = await new ServiceFabricClientBuilder()
    .UseEndpoints(new Uri("http://localhost:19080"))
    .BuildAsync();

CodePackagePatchResult result = await client.CodePackages.SetCodePackagePatchAsync(
    new CodePackagePatchSetDescription("fabric:/Orders/Worker", "Worker.Code", "1.0",
        new LocalDirectoryCodePackagePatchSource("./Hotfix")));

CodePackagePatchResult observations = await client.CodePackages.GetCodePackagePatchListAsync(patchId: result.PatchId);
```

PatchId identifies a stable version-qualified target. A fresh Clear removes its current mapping, even if a later Set
selected different content for that target. ContentId identifies a reusable immutable publication; clearing a mapping does
not delete that content. Use the returned ImageStoreRelativePath in ImageStoreCodePackagePatchSource to reuse a publication.

The client resolves the owning application type and reads the source directory directly, without creating a snapshot or an
intermediate Image Store folder. Keep the source unchanged during upload. Filesystem links and the reserved `_.dir`
completion marker are rejected. ContentName, when supplied, must be a nonempty path segment without control characters,
`<>:"/\|?*`, or a trailing dot or space; it prefixes a newly generated ContentId.

Directory uploads use the existing ordinary file PUT endpoint, including for files larger than 25 MiB. Files are streamed
with bounded buffers rather than loaded entirely into memory. A failed transfer retries that file from the beginning.
Set validates and finalizes the uploaded retained folder in place without another copy or ContentId.

Completed uploads remain retained even if Set fails or its outcome is unknown. An incomplete upload is eligible for
best-effort deletion using the remaining operation budget; cancellation or cleanup failure can leave partial files behind.
Upload or submission exceptions include `Data["CodePackagePatchUploadPath"]` for recovery. The REST Set body never contains
the caller-local path. The runtime applies normal SF deployment access permissions to replacement files, rather than
preserving the source machine's ACLs or exact Unix file modes.

## Documentation

- [Service Fabric REST API reference](https://learn.microsoft.com/rest/api/servicefabric)
- [Connect to a secure cluster](https://learn.microsoft.com/azure/service-fabric/service-fabric-connect-to-secure-cluster)
- [Source code](https://github.com/microsoft/service-fabric-dotnet)
