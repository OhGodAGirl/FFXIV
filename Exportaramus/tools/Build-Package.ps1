param([string]$OutputDirectory = "$PSScriptRoot/../project/bin/Release")
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$files = @('Exportaramus.ConsumerKit.V1_1.dll','Exportaramus.ConsumerKit.V1_2.dll','Exportaramus.deps.json','Exportaramus.dll','Exportaramus.json','images/icon.png')
foreach ($file in $files) { if (!(Test-Path -LiteralPath (Join-Path $output $file) -PathType Leaf)) { throw "Missing runtime file: $file" } }
$directory = Join-Path $output 'Exportaramus'
[void][IO.Directory]::CreateDirectory($directory)
$path = Join-Path $directory 'latest.zip'
$temporary = $path + '.tmp'
$stream = [IO.File]::Open($temporary, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $files) {
            $entry = $archive.CreateEntry($file, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $source = [IO.File]::OpenRead((Join-Path $output $file)); $destination = $entry.Open()
            try { $source.CopyTo($destination) } finally { $destination.Dispose(); $source.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $stream.Dispose() }
[IO.File]::Move($temporary, $path, $true)
Copy-Item -LiteralPath "$output/Exportaramus.json" -Destination "$directory/Exportaramus.json" -Force
