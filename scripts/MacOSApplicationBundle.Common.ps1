function Test-MacOSSymbolFile {
    param(
        [Parameter(Mandatory)]
        [string] $PublishRoot,

        [Parameter(Mandatory)]
        [IO.FileInfo] $File
    )

    $relativePath = [IO.Path]::GetRelativePath($PublishRoot, $File.FullName)
    return ($File.Extension -in '.pdb', '.dbg', '.xml') -or
        ($relativePath -match '(^|[/\\])[^/\\]+\.dSYM([/\\]|$)')
}

function Write-InfoPlist {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $ApplicationVersion
    )

    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Encoding = [Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.NewLineChars = "`n"
    $settings.NewLineHandling = [Xml.NewLineHandling]::Replace

    $writer = [Xml.XmlWriter]::Create($Path, $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteDocType(
            'plist',
            '-//Apple//DTD PLIST 1.0//EN',
            'http://www.apple.com/DTDs/PropertyList-1.0.dtd',
            [NullString]::Value)
        $writer.WriteStartElement('plist')
        $writer.WriteAttributeString('version', '1.0')
        $writer.WriteStartElement('dict')

        $values = [ordered]@{
            CFBundleDevelopmentRegion = 'en'
            CFBundleDisplayName = 'Open Kusto Explorer'
            CFBundleExecutable = 'OpenKustoExplorer'
            CFBundleIconFile = 'OpenKustoExplorer.icns'
            CFBundleIdentifier = 'io.github.i-am-shodan.OpenKustoExplorer'
            CFBundleInfoDictionaryVersion = '6.0'
            CFBundleName = 'Kusto Explorer'
            CFBundlePackageType = 'APPL'
            CFBundleShortVersionString = $ApplicationVersion
            CFBundleVersion = $ApplicationVersion
            LSMinimumSystemVersion = '14.0'
        }

        foreach ($entry in $values.GetEnumerator()) {
            $writer.WriteElementString('key', $entry.Key)
            $writer.WriteElementString('string', $entry.Value)
        }

        $writer.WriteElementString('key', 'NSHighResolutionCapable')
        $writer.WriteStartElement('true')
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    }
    finally {
        $writer.Dispose()
    }
}