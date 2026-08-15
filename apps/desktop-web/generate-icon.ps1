[CmdletBinding()]
param(
    [string] $Source = (Join-Path $PSScriptRoot 'app-icon.png')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$magick = Get-Command magick -ErrorAction Stop
$target = Join-Path $PSScriptRoot 'app.ico'
$square = Join-Path ([IO.Path]::GetTempPath()) ('dsh-app-icon-' + [guid]::NewGuid().ToString('N') + '.png')

try {
    & $magick.Source $Source -fuzz '3%' -trim +repage -bordercolor white -border '24x24' -resize '512x512' -gravity center -background white -extent '512x512' $square
    if ($LASTEXITCODE -ne 0) { throw "ImageMagick failed to prepare the square icon (exit $LASTEXITCODE)." }

    & $magick.Source $square -define 'icon:auto-resize=256,128,64,48,32,24,16' $target
    if ($LASTEXITCODE -ne 0) { throw "ImageMagick failed to write $target (exit $LASTEXITCODE)." }
}
finally {
    if (Test-Path -LiteralPath $square) { Remove-Item -LiteralPath $square }
}

Write-Output "Generated $target"
