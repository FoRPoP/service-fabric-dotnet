[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $ModulePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[string] $script:ResolvedModulePath = (Resolve-Path -LiteralPath $ModulePath).Path
[int] $script:Checks = 0

function Main {
    if (Get-Variable -Name SFHttpClusterConnection -Scope Global -ErrorAction SilentlyContinue) {
        throw 'Run this test in a separate PowerShell process without a cluster connection.'
    }

    Import-Module -Name $script:ResolvedModulePath
    CheckHelpRegeneration
    foreach ($name in @('Set-SFCodePackagePatch', 'Clear-SFCodePackagePatch', 'Get-SFCodePackagePatch')) {
        [object] $help = Get-Help -Name $name -Full
        AssertEqual $name $help.Name 'Command help name'
        AssertEqual $true (-not [string]::IsNullOrWhiteSpace($help.Synopsis)) 'Command help synopsis'
        AssertEqual $false (($help.Description.Text -join ' ') -match 'Configure the connection ClientTimeout') 'Help uses the operation deadline'
    }
    [object] $setHelp = Get-Help -Name Set-SFCodePackagePatch -Full
    AssertEqual $true ($setHelp.Parameters.Parameter.Name -contains 'CodePackageName') 'Target name in help'
    AssertEqual $true ($setHelp.Parameters.Parameter.Name -contains 'Source') 'Typed source in help'
    [object] $sourceHelp = $setHelp.Parameters.Parameter | Where-Object Name -EQ Source
    AssertEqual $true (($sourceHelp.Description.Text -join ' ') -match 'retained HotPatches') 'Source help describes direct publication'
    [object] $getHelp = Get-Help -Name Get-SFCodePackagePatch -Full
    [object] $nodesHelp = $getHelp.Parameters.Parameter | Where-Object Name -EQ Nodes
    AssertEqual $true (($nodesHelp.Description.Text -join ' ') -match 'raw CSV value') 'Get help does not require pre-encoding'
    Add-Type -TypeDefinition @'
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class PatchRecordingHandler : DelegatingHandler
{
    public int Calls { get; private set; }
    public int Uploads { get; private set; }
    public int Deletes { get; private set; }
    public int OwnerQueries { get; private set; }
    public System.Collections.Concurrent.ConcurrentQueue<string> UploadPaths { get; } = new System.Collections.Concurrent.ConcurrentQueue<string>();
    public string Body { get; private set; }
    public string SetBody { get; private set; }
    public System.Uri RequestUri { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        RequestUri = request.RequestUri;
        Body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
        if (request.Method == HttpMethod.Put)
        {
            Uploads++;
            UploadPaths.Enqueue(System.Uri.UnescapeDataString(request.RequestUri.AbsolutePath).Replace('\\', '/'));
        }
        if (request.Method == HttpMethod.Delete) Deletes++;
        if (request.RequestUri.AbsolutePath.EndsWith("/SetCodePackagePatch")) SetBody = Body;
        string response;
        if (request.RequestUri.AbsolutePath.EndsWith("/GetApplicationName"))
        {
            OwnerQueries++;
            response = "{\"Id\":\"test-application\"}";
        }
        else if (request.RequestUri.AbsolutePath == "/Applications/test-application")
        {
            OwnerQueries++;
            response = "{\"TypeName\":\"test\"}";
        }
        else if (request.RequestUri.AbsolutePath.EndsWith("/GetClusterManifest"))
            response = "{\"Manifest\":\"<ClusterManifest xmlns='http://schemas.microsoft.com/2011/01/fabric'><FabricSettings><Section Name='Management'><Parameter Name='ImageStoreConnectionString' Value='fabric:ImageStore'/></Section></FabricSettings></ClusterManifest>\"}";
        else
            response = "{\"SuccessfulNodes\":[],\"FailedNodes\":[],\"UnresolvedNodes\":[],\"PatchId\":\"test-target\",\"ContentId\":\"test-content\",\"ImageStoreRelativePath\":\"Store/test/HotPatches/test-content\"}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(response)
        };
    }
}
'@

    [object] $handler = [PatchRecordingHandler]::new()
    [object] $builder = [Microsoft.ServiceFabric.Client.ServiceFabricClientBuilder]::new()
    [void] $builder.UseEndpoints([uri] 'http://hot-patching-test.invalid')
    [void] [Microsoft.ServiceFabric.Client.Http.ServiceFabricClientBuilderExtensions]::ConfigureHttpClientSettings(
        $builder, [System.Net.Http.HttpClientHandler]::new(), [System.Net.Http.DelegatingHandler[]] @($handler))
    [object] $client = $builder.BuildAsync().GetAwaiter().GetResult()
    [string] $localDirectory = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
    Set-Variable -Name SFHttpClusterConnection -Scope Global -Value $client
    try {
        [string] $suffix = [guid]::NewGuid().ToString('N')
        [hashtable] $target = @{
            ServiceName = "fabric:/test$suffix"
            CodePackageName = "target$suffix"
            CodePackageVersion = "version$suffix"
        }
        [string] $sourcePath = "Store/test/HotPatches/$suffix"
        [object] $source = [Microsoft.ServiceFabric.Common.ImageStoreCodePackagePatchSource]::new($sourcePath)
        [object] $command = Get-Command Set-SFCodePackagePatch
        foreach ($parameterSet in $command.ParameterSets) {
            AssertEqual $true ($parameterSet.Parameters | Where-Object Name -EQ CodePackageName).IsMandatory 'Target package name is required'
            AssertEqual $true ($parameterSet.Parameters | Where-Object Name -EQ CodePackageVersion).IsMandatory 'Target version is required'
            AssertEqual $true ($parameterSet.Parameters | Where-Object Name -EQ Source).IsMandatory 'Typed Source is required'
        }

        Set-SFCodePackagePatch @target -Source $source -WhatIf
        AssertEqual 0 $handler.Calls 'WhatIf must not submit a request'

        [Diagnostics.Stopwatch] $elapsed = [Diagnostics.Stopwatch]::StartNew()
        [object] $result = Set-SFCodePackagePatch @target -Source $source -ContentName $suffix -Nodes N1,N2 -Confirm:$false
        [object] $request = $handler.Body | ConvertFrom-Json
        AssertEqual $target.CodePackageName $request.CodePackageName 'Image Store target name'
        AssertEqual $target.CodePackageVersion $request.CodePackageVersion 'Image Store target version'
        AssertEqual 'ImageStore' $request.Source.Kind 'Image Store discriminator'
        AssertEqual $sourcePath $request.Source.ImageStoreRelativePath 'Image Store source path'
        AssertEqual $suffix $request.ContentName 'Publication name'
        AssertEqual 'N1,N2' ($request.Nodes -join ',') 'Array recipient list'
        AssertTimeout $handler.RequestUri 1800 $elapsed.Elapsed
        AssertEqual 'CodePackagePatchResult' $result.GetType().Name 'Typed cmdlet result'

        [string] $sourceName = "source$suffix"
        $source = [Microsoft.ServiceFabric.Common.ProvisionedCodePackagePatchSource]::new("type$suffix", $suffix, "manifest$suffix", $sourceName)
        $elapsed.Restart()
        [void] (Set-SFCodePackagePatch @target -Source $source -ServerTimeout 2400 -Confirm:$false)
        $request = $handler.Body | ConvertFrom-Json
        AssertEqual $target.CodePackageName $request.CodePackageName 'Provisioned target name'
        AssertEqual $sourceName $request.Source.CodePackageName 'Independent provisioned source name'
        AssertEqual 'ProvisionedCodePackage' $request.Source.Kind 'Provisioned discriminator'
        AssertTimeout $handler.RequestUri 2400 $elapsed.Elapsed

        $source = [Microsoft.ServiceFabric.Common.LocalDirectoryCodePackagePatchSource]::new($localDirectory)
        AssertEqual 'LocalDirectory' $source.Kind.ToString() 'Client-only local source enum'
        [int] $beforeLocal = $handler.Calls
        Set-SFCodePackagePatch @target -Source $source -WhatIf
        AssertEqual $beforeLocal $handler.Calls 'Local-directory WhatIf performs no upload'
        [void] [IO.Directory]::CreateDirectory($localDirectory)
        [IO.File]::WriteAllBytes((Join-Path $localDirectory 'code.bin'), [guid]::NewGuid().ToByteArray())
        [void] (Set-SFCodePackagePatch @target -Source $source -Confirm:$false)
        $request = $handler.SetBody | ConvertFrom-Json
        AssertEqual $true ($handler.Uploads -ge 2) 'Local files and completion markers uploaded automatically'
        AssertEqual 2 $handler.OwnerQueries 'Existing queries resolve the owning application type'
        AssertEqual 'ImageStore' $request.Source.Kind 'Local source becomes a stored wire reference'
        AssertEqual $true ($request.Source.ImageStoreRelativePath.StartsWith('Store/test/HotPatches/')) 'Direct retained publication path'
        [string] $uploadPath = ''
        foreach ($uploadPath in $handler.UploadPaths) {
            AssertEqual $true ($uploadPath.StartsWith('/ImageStore/' + $request.Source.ImageStoreRelativePath + '/')) 'Files uploaded directly under the Set source'
        }
        AssertEqual 0 $handler.Deletes 'Completed publication is not deleted'
        AssertEqual $false ($handler.SetBody.Contains($localDirectory)) 'Local path never sent in JSON'

        [string] $patchId = "v1/target$suffix/version&value/package"
        [string] $nodes = '"N,1",N&2'
        [void] (Get-SFCodePackagePatch -PatchId $patchId -Nodes $nodes)
        AssertEqual $null $handler.Body 'Get has no request body'
        AssertEqual $true ($handler.RequestUri.Query.Contains('PatchId=' + [uri]::EscapeDataString($patchId))) 'Encoded target identifier'
        AssertEqual $true ($handler.RequestUri.Query.Contains('Nodes=' + [uri]::EscapeDataString($nodes))) 'One encoded CSV node value'

        [int] $beforeClear = $handler.Calls
        Clear-SFCodePackagePatch -PatchId $patchId -Nodes N1 -WhatIf
        AssertEqual $beforeClear $handler.Calls 'Clear WhatIf must not submit a request'
        [void] (Clear-SFCodePackagePatch -PatchId $patchId -Nodes N1 -Confirm:$false)
        $request = $handler.Body | ConvertFrom-Json
        AssertEqual $patchId $request.PatchId 'Clear target identifier'
        AssertEqual 'Nodes,PatchId' (($request.PSObject.Properties.Name | Sort-Object) -join ',') 'Clear has no source, target selector, or wire Force flag'

        [int] $calls = $handler.Calls
        [bool] $rejected = $false
        try {
            [void] (Set-SFCodePackagePatch @target -Source $source -ServerTimeout 0 -Confirm:$false)
        }
        catch [System.Management.Automation.ParameterBindingException] {
            $rejected = $true
        }
        AssertEqual $true $rejected 'PowerShell rejects an invalid timeout'
        AssertEqual $calls $handler.Calls 'Invalid binding sends no request'
        Write-Output "PASS: $script:Checks PowerShell binding and request checks; no network requests."
    }
    finally {
        $client.Dispose()
        Remove-Variable -Name SFHttpClusterConnection -Scope Global
        if ([IO.Directory]::Exists($localDirectory)) { [IO.Directory]::Delete($localDirectory, $true) }
    }
}

function CheckHelpRegeneration {
    [string] $directory = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
    [string] $generated = Join-Path $directory 'generated.xml'
    [string] $target = Join-Path $directory 'target.xml'
    [string] $updater = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../eng/UpdateCodePackagePatchHelp.ps1')).Path
    [xml] $fixture = Get-Content -LiteralPath (Join-Path (Split-Path $script:ResolvedModulePath) 'Microsoft.ServiceFabric.Powershell.Http-Help.xml') -Raw
    foreach ($entry in @($fixture.SelectNodes("//*[local-name()='command']"))) {
        [string] $name = $entry.SelectSingleNode("*[local-name()='details']/*[local-name()='name']").InnerText
        if ($name -notin @('Get-SFCodePackagePatch', 'Get-SFNode')) {
            [void] $entry.ParentNode.RemoveChild($entry)
        }
    }
    [string] $unrelated = $fixture.SelectSingleNode("//*[local-name()='command'][*[local-name()='details']/*[local-name()='name']='Get-SFNode']").OuterXml
    [string] $get = $fixture.SelectSingleNode("//*[local-name()='command'][*[local-name()='details']/*[local-name()='name']='Get-SFCodePackagePatch']").OuterXml
    [void] [IO.Directory]::CreateDirectory($directory)
    try {
        foreach ($newLine in @("`r`n", "`n")) {
            [string] $unrelatedText = [regex]::Replace("    $unrelated" + $newLine, '\r\n|\r|\n', $newLine)
            [string] $fixtureText = [regex]::Replace('<helpItems>' + $newLine + "    $get" + $newLine +
                $unrelatedText + '</helpItems>' + $newLine, '\r\n|\r|\n', $newLine)
            [IO.File]::WriteAllText($generated, $fixtureText)
            [IO.File]::Copy($generated, $target, $true)
            & (Get-Command pwsh).Source -NoProfile -File $updater -GeneratedHelp $generated -ModulePath $script:ResolvedModulePath -TargetHelp $target
            AssertEqual 0 $LASTEXITCODE 'Help regeneration from Get-only output'
            [string] $first = [IO.File]::ReadAllText($target)
            & (Get-Command pwsh).Source -NoProfile -File $updater -GeneratedHelp $generated -ModulePath $script:ResolvedModulePath -TargetHelp $target
            AssertEqual 0 $LASTEXITCODE 'Repeated help regeneration'
            AssertEqual $first ([IO.File]::ReadAllText($target)) 'Help regeneration is byte-idempotent'
            AssertEqual $first ([regex]::Replace($first, '\r\n|\r|\n', $newLine)) 'Help line endings are consistent'
            AssertEqual $true ($first.Contains($unrelatedText)) 'Unrelated help bytes and indentation preserved'
            [xml] $result = $first
            [object[]] $entries = @($result.SelectNodes("//*[local-name()='command']"))
            AssertEqual 4 $entries.Count 'One unrelated command and three patch commands'
            foreach ($name in @('Set-SFCodePackagePatch', 'Clear-SFCodePackagePatch', 'Get-SFCodePackagePatch')) {
                AssertEqual 1 @($entries | Where-Object { $_.SelectSingleNode("*[local-name()='details']/*[local-name()='name']").InnerText -ceq $name }).Count 'Unique patch help entry'
            }
            AssertEqual $unrelated ($result.SelectSingleNode("//*[local-name()='command'][*[local-name()='details']/*[local-name()='name']='Get-SFNode']").OuterXml) 'Unrelated help preserved'
        }
    }
    finally {
        [IO.Directory]::Delete($directory, $true)
    }
}

function AssertTimeout([uri] $uri, [long] $seconds, [timespan] $elapsed) {
    [Text.RegularExpressions.Match] $match = [regex]::Match($uri.Query, '(?:[?&])timeout=([0-9]+)(?:&|$)')
    AssertEqual $true $match.Success 'Timeout query value exists'
    [long] $actual = [long] $match.Groups[1].Value
    [long] $minimum = [Math]::Max(1, $seconds - [long] [Math]::Ceiling($elapsed.TotalSeconds))
    AssertEqual $true ($actual -ge $minimum -and $actual -le $seconds) 'Request timeout stays within elapsed operation budget'
}

function AssertEqual([object] $expected, [object] $actual, [string] $reason) {
    if ($expected -cne $actual) {
        throw "$reason. Expected '$expected', got '$actual'."
    }
    $script:Checks++
}

Main