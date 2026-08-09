$ErrorActionPreference = 'Stop'
$root = Join-Path ([System.IO.Path]::GetTempPath()) 'FolderPeekTest'

New-Item -ItemType Directory -Force -Path $root | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $root 'Empty') | Out-Null
$small = New-Item -ItemType Directory -Force -Path (Join-Path $root 'Small')
New-Item -ItemType Directory -Force -Path (Join-Path $small 'FolderA') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $small 'FolderB') | Out-Null
Set-Content -LiteralPath (Join-Path $small 'README.md') -Value '# FolderPeek test file'
Set-Content -LiteralPath (Join-Path $small 'photo.jpg') -Value 'placeholder'

$many = New-Item -ItemType Directory -Force -Path (Join-Path $root 'ManyItems')
1..500 | ForEach-Object {
    $path = Join-Path $many ("item-{0:D4}.txt" -f $_)
    if (-not (Test-Path -LiteralPath $path)) { Set-Content -LiteralPath $path -Value $_ }
}

$deep = New-Item -ItemType Directory -Force -Path (Join-Path $root 'DeepButDoNotRecurse')
$level = New-Item -ItemType Directory -Force -Path (Join-Path $deep 'Level1')
$level = New-Item -ItemType Directory -Force -Path (Join-Path $level 'Level2')
New-Item -ItemType Directory -Force -Path (Join-Path $level 'Level3') | Out-Null

Write-Host "Created FolderPeek fixtures at $root"
