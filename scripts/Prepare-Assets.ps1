$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$xml = '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">'
Get-ChildItem Assets/Icons -Filter *.svg | ForEach-Object {
  [xml]$svg = Get-Content $_.FullName -Raw
  $paths = @($svg.SelectNodes("//*[local-name()='path']") | ForEach-Object { $_.GetAttribute('d') })
  if (!$paths.Count) { throw "Missing icon paths: $($_.Name)" }
  $xml += '<Geometry x:Key="Icon-' + $_.BaseName + '">F1 ' + ($paths -join ' ') + '</Geometry>'
}
$xml += '</ResourceDictionary>'
Set-Content Assets/Icons.xaml $xml -Encoding utf8
Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Image]::FromFile((Join-Path $PWD 'Assets/app-icon.png'))
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
  $bitmap = [System.Drawing.Bitmap]::new($size,$size)
  $g = [System.Drawing.Graphics]::FromImage($bitmap)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.DrawImage($source,0,0,$size,$size)
  $g.Dispose()
  $stream = [System.IO.MemoryStream]::new()
  $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
  $frames += @{Size=$size; Data=$stream.ToArray()}
  $stream.Dispose(); $bitmap.Dispose()
}
$source.Dispose()
$file = [System.IO.File]::Create((Join-Path $PWD 'Assets/app.ico'))
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
  $dimension = if ($frame.Size -eq 256) {0} else {$frame.Size}
  $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
  $writer.Write([byte]0); $writer.Write([byte]0)
  $writer.Write([uint16]1); $writer.Write([uint16]32)
  $writer.Write([uint32]$frame.Data.Length); $writer.Write([uint32]$offset)
  $offset += $frame.Data.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
$writer.Dispose()
