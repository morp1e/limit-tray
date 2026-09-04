<#
.SYNOPSIS
  Fails if an em dash appears on a published surface.

.DESCRIPTION
  The em dash is the most visible tell of unedited machine writing, so nothing that
  goes outward carries one. A dash almost always joins two clauses that read better
  as two sentences or with a colon, so the fix is to rewrite, not to swap characters.

  This runs in CI because the rule was broken 68 times in a single day while being
  written down and agreed. A grep does not forget.

  In scope: documentation, workflows, source comments, anything a reader can reach.
  Out of scope: docs/specs and docs/plans, which this repository deliberately keeps
  as historical records of how it was built, and binary or generated files.

.EXAMPLE
  pwsh -File .github/scripts/Test-NoEmDash.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot '..' '..'),

    # Written as a character code so this file does not contain the thing it forbids.
    [char]$Forbidden = [char]0x2014
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path $Root).Path

$include = @('*.md', '*.yml', '*.yaml', '*.cs', '*.xaml', '*.csproj', '*.sln', '*.ps1', '*.json')
$excludeDirs = @(
    "$([IO.Path]::DirectorySeparatorChar)docs$([IO.Path]::DirectorySeparatorChar)specs",
    "$([IO.Path]::DirectorySeparatorChar)docs$([IO.Path]::DirectorySeparatorChar)plans",
    "$([IO.Path]::DirectorySeparatorChar)bin$([IO.Path]::DirectorySeparatorChar)",
    "$([IO.Path]::DirectorySeparatorChar)obj$([IO.Path]::DirectorySeparatorChar)",
    "$([IO.Path]::DirectorySeparatorChar).git$([IO.Path]::DirectorySeparatorChar)",
    "$([IO.Path]::DirectorySeparatorChar)publish$([IO.Path]::DirectorySeparatorChar)"
)

$files = Get-ChildItem -Path $Root -Recurse -File -Include $include |
    Where-Object {
        $path = $_.FullName
        -not ($excludeDirs | Where-Object { $path -like "*$_*" })
    }

$hits = foreach ($file in $files) {
    $number = 0
    foreach ($line in (Get-Content -LiteralPath $file.FullName -Encoding utf8NoBOM)) {
        $number++
        if ($line.IndexOf($Forbidden) -ge 0) {
            [pscustomobject]@{
                File = [IO.Path]::GetRelativePath($Root, $file.FullName)
                Line = $number
                Text = $line.Trim()
            }
        }
    }
}

if (-not $hits) {
    Write-Host "no-em-dash: clean ($($files.Count) files checked)"
    exit 0
}

Write-Host "no-em-dash: FAILED, $($hits.Count) occurrence(s)."
Write-Host "Rewrite the sentence. Two sentences, or a colon, almost always reads better."
Write-Host ''
foreach ($hit in $hits) { "{0}:{1}: {2}" -f $hit.File, $hit.Line, $hit.Text }
exit 1
