# Fetches the LDIF test corpus from the OpenLDAP project at a pinned release tag
# into tests/fixtures/openldap/. Re-run to refresh; bump $Tag to take a new corpus.
param(
    [string]$Tag = 'OPENLDAP_REL_ENG_2_6_10'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $repoRoot 'tests/fixtures/openldap'
$clone = Join-Path ([IO.Path]::GetTempPath()) "openldap-corpus-$Tag"

if (-not (Test-Path $clone)) {
    git clone --depth 1 --branch $Tag --filter=blob:none --sparse `
        https://github.com/openldap/openldap.git $clone
    git -C $clone sparse-checkout set tests/data
}

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Force $dest | Out-Null

# OpenLDAP Public License: redistribution requires the license text accompany the files.
Copy-Item (Join-Path $clone 'LICENSE') (Join-Path $dest 'LICENSE.OpenLDAP')

$all = Get-ChildItem (Join-Path $clone 'tests/data') -Filter '*.ldif' -File
$copied = 0
$skipped = 0
foreach ($file in $all) {
    # Skip template files containing @SUBST@ tokens that OpenLDAP's scripts sed-replace;
    # they are not valid LDIF as-is.
    if (Select-String -Path $file.FullName -Pattern '@[A-Z][A-Z0-9_]*@' -Quiet) {
        $skipped++
        continue
    }
    Copy-Item $file.FullName (Join-Path $dest $file.Name)
    $copied++
}

"Corpus: $Tag — copied $copied LDIF files, skipped $skipped substitution templates."
