param(
    [Parameter(Mandatory)][string] $GeneratedHelp,
    [Parameter(Mandatory)][string] $ModulePath,
    [Parameter(Mandatory)][string] $TargetHelp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[string] $script:GeneratedHelp = (Resolve-Path -LiteralPath $GeneratedHelp).Path
[string] $script:ModulePath = (Resolve-Path -LiteralPath $ModulePath).Path
[string] $script:TargetHelp = (Resolve-Path -LiteralPath $TargetHelp).Path
[hashtable] $script:Namespaces = @{
    command = 'http://schemas.microsoft.com/maml/dev/command/2004/10'
    maml = 'http://schemas.microsoft.com/maml/2004/10'
    dev = 'http://schemas.microsoft.com/maml/dev/2004/10'
}

function Main {
    Import-Module -Name $script:ModulePath
    [xml] $generated = Get-Content -LiteralPath $script:GeneratedHelp -Raw
    [xml] $comments = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($script:ModulePath, '.xml')) -Raw
    [string] $original = [IO.File]::ReadAllText($script:TargetHelp)
    [string] $newLine = if ($original.Contains("`r`n")) { "`r`n" } else { "`n" }
    [xml] $existing = $original
    [Xml.XmlNamespaceManager] $namespaces = [Xml.XmlNamespaceManager]::new($generated.NameTable)
    $namespaces.AddNamespace('command', $script:Namespaces.command)
    [Xml.XmlElement] $get = $generated.SelectSingleNode("//command:command[command:details/command:name='Get-SFCodePackagePatch']", $namespaces)
    if (-not $get) { throw 'Generated Get help was not found.' }
    [Xml.XmlElement[]] $entries = @(
        $get
        (NewCommandHelp 'Set-SFCodePackagePatch' $comments)
        (NewCommandHelp 'Clear-SFCodePackagePatch' $comments)
    )
    [string[]] $names = @('Get-SFCodePackagePatch', 'Set-SFCodePackagePatch', 'Clear-SFCodePackagePatch')
    [Collections.Generic.List[string]] $replaced = [Collections.Generic.List[string]]::new()
    [regex] $commands = '(?ms)(?:^[ \t]*)?<command:command\b[^>]*>.*?</command:command>[ \t]*(?:\r?\n)?'
    $original = $commands.Replace($original, {
        param($match)
        [xml] $entry = $match.Value
        [string] $name = $entry.SelectSingleNode("//*[local-name()='details']/*[local-name()='name']").InnerText
        if ($name -in $names) {
            $replaced.Add($name)
            return ''
        }
        return $match.Value
    })
    if ($replaced.Count -ne @($replaced | Select-Object -Unique).Count) {
        throw 'The existing patch help contains duplicate command entries.'
    }
    [Text.StringBuilder] $addition = [Text.StringBuilder]::new()
    foreach ($entry in $entries) {
        [IO.StringWriter] $text = [IO.StringWriter]::new()
        [Xml.XmlWriterSettings] $settings = [Xml.XmlWriterSettings]::new()
        $settings.Indent = $true
        $settings.OmitXmlDeclaration = $true
        $settings.NewLineChars = $newLine
        [Xml.XmlWriter] $writer = [Xml.XmlWriter]::Create($text, $settings)
        try { $entry.WriteTo($writer) } finally { $writer.Dispose() }
        [void] $addition.Append($text.ToString()).Append($newLine)
        $text.Dispose()
    }
    [int] $closing = $original.LastIndexOf('</helpItems>', [StringComparison]::Ordinal)
    if ($closing -lt 0) { throw 'The expected help root was not found.' }
    [string] $updated = $original.Insert($closing, $addition.ToString())
    [xml] $validated = $updated
    if ($validated.helpItems.ChildNodes.Count -ne $existing.helpItems.ChildNodes.Count + 3 - $replaced.Count) {
        throw 'Help update changed the existing command inventory.'
    }
    [IO.File]::WriteAllText($script:TargetHelp, $updated, [Text.UTF8Encoding]::new($false))
    Write-Output 'Updated three patch help entries; all unrelated command text was preserved.'
}

function NewCommandHelp([string] $name, [xml] $comments) {
    [object] $command = Get-Command $name
    [type] $type = $command.ImplementingType
    [Reflection.PropertyInfo[]] $properties = $type.GetProperties([Reflection.BindingFlags] 'Public,Instance,DeclaredOnly')
    [xml] $document = [xml] "<command:command xmlns:command='$($script:Namespaces.command)' xmlns:maml='$($script:Namespaces.maml)' xmlns:dev='$($script:Namespaces.dev)'/>"
    [Xml.XmlElement] $root = $document.DocumentElement
    [Xml.XmlElement] $details = AddElement $root command details
    [void] (AddElement $details command name $name)
    [void] (AddElement $details command verb $command.Verb)
    [void] (AddElement $details command noun $command.Noun.Substring(2))
    [string] $summary = $comments.SelectSingleNode("//member[@name='T:$($type.FullName)']/summary").InnerText.Trim()
    [void] (AddElement (AddElement $details maml description) maml para $summary)
    [void] (AddElement (AddElement $root maml description) maml para 'Success means saved intent, not completed process work. ServerTimeout bounds the whole operation, including local preparation and upload for Set. These commands use that deadline independently of the connection timeout. Content is retained independently of the mapping.')
    [Xml.XmlElement] $syntax = AddElement $root command syntax
    foreach ($parameterSet in $command.ParameterSets) {
        [Xml.XmlElement] $item = AddElement $syntax command syntaxItem
        [void] (AddElement $item maml name $name)
        foreach ($parameter in $parameterSet.Parameters | Where-Object { $_.Name -in $properties.Name }) {
            [Xml.XmlElement] $node = AddElement $item command parameter
            $node.SetAttribute('required', $parameter.IsMandatory.ToString().ToLowerInvariant())
            [void] (AddElement $node maml name $parameter.Name)
            [void] (AddElement $node command parameterValue $parameter.ParameterType.Name)
        }
    }
    [Xml.XmlElement] $parameters = AddElement $root command parameters
    foreach ($property in $properties) {
        [object] $metadata = $command.Parameters[$property.Name]
        [bool] $required = @($metadata.Attributes | Where-Object { $_ -is [Management.Automation.ParameterAttribute] -and $_.Mandatory }).Count -gt 0
        [Xml.XmlElement] $node = AddElement $parameters command parameter
        $node.SetAttribute('required', $required.ToString().ToLowerInvariant())
        [void] (AddElement $node maml name $property.Name)
        [string] $description = $comments.SelectSingleNode("//member[@name='P:$($type.FullName).$($property.Name)']/summary").InnerText.Trim()
        [void] (AddElement (AddElement $node maml description) maml para $description)
        [void] (AddElement $node command parameterValue $property.PropertyType.Name)
        [void] (AddElement (AddElement $node dev type) maml name $property.PropertyType.Name)
    }
    [Xml.XmlElement] $returnValue = AddElement (AddElement $root command returnValues) command returnValue
    [void] (AddElement (AddElement $returnValue dev type) maml name 'CodePackagePatchResult')
    return ,$root
}

function AddElement([Xml.XmlElement] $parent, [string] $prefix, [string] $name, [string] $text = '') {
    [Xml.XmlElement] $element = $parent.OwnerDocument.CreateElement($prefix, $name, $script:Namespaces[$prefix])
    if ($text) { $element.InnerText = $text }
    [void] $parent.AppendChild($element)
    return ,$element
}

Main