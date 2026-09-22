$src = (Get-Location).Path
$dst = Join-Path $src '.clean-design-v2'
if (Test-Path $dst) { Remove-Item -LiteralPath $dst -Recurse -Force }
New-Item -ItemType Directory -Path $dst | Out-Null
$excluded = @('bin','obj','.artifacts','.vs','TestResults','coverage','.clean-design','.clean-design-v2')
$files = Get-ChildItem -LiteralPath $src -File -Recurse | Where-Object {
  $rel = $_.FullName.Substring($src.Length + 1)
  $parts = $rel -split '[\\/]'
  (-not ($parts | Where-Object { $excluded -contains $_ -or $_ -like '*.binlog' })) -and ($rel -notlike '.clean-design-*\\*')
}
foreach ($f in $files) {
  $rel = $f.FullName.Substring($src.Length + 1)
  $out = Join-Path $dst $rel
  New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
  Copy-Item -LiteralPath $f.FullName -Destination $out -Force
}
Write-Output "MANIFEST=$($files.Count)"
