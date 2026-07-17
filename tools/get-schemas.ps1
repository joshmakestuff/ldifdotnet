# Fetches the LDAP schema corpus into tests/fixtures/schemas/:
#  - openldap/: every *.schema shipped with OpenLDAP at the pinned release tag
#  - contrib/:  well-known third-party schemas pinned to exact commits, verified
#               against recorded SHA-256 checksums
param(
    [string]$OpenLdapTag = 'OPENLDAP_REL_ENG_2_6_10'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $repoRoot 'tests/fixtures/schemas'
$clone = Join-Path ([IO.Path]::GetTempPath()) "openldap-corpus-$OpenLdapTag"

# --- OpenLDAP's own schemas, from the same pinned clone the LDIF corpus uses ---
if (-not (Test-Path $clone)) {
    git clone --depth 1 --branch $OpenLdapTag --filter=blob:none --sparse `
        https://github.com/openldap/openldap.git $clone
    git -C $clone sparse-checkout set tests/data
}
git -C $clone sparse-checkout add servers/slapd/schema

$openldapDest = Join-Path $dest 'openldap'
if (Test-Path $openldapDest) { Remove-Item $openldapDest -Recurse -Force }
New-Item -ItemType Directory -Force $openldapDest | Out-Null
Copy-Item (Join-Path $clone 'LICENSE') (Join-Path $openldapDest 'LICENSE.OpenLDAP')
$openldapSchemas = Get-ChildItem (Join-Path $clone 'servers/slapd/schema') -Filter '*.schema' -File
$openldapSchemas | Copy-Item -Destination $openldapDest
"OpenLDAP $OpenLdapTag — copied $($openldapSchemas.Count) schema files."

# --- Well-known third-party schemas, pinned to exact commits ---
# name = destination file; url = raw content at a pinned commit; sha256 = content hash.
$contrib = @(
    @{
        Name   = 'eduperson.schema'
        Url    = 'https://raw.githubusercontent.com/REFEDS/eduperson/c672bc4be5e081cba95013b1ac1f7e18fd247ca1/schema/openldap/eduperson.schema'
        Sha256 = 'C6247977BE7F1AC89FCAC18CB640BE87DFDB149CDAD6EB94CD10BBFA7614E891'
    }
    @{
        Name   = 'rfc2307bis.schema'
        Url    = 'https://raw.githubusercontent.com/jtyr/rfc2307bis/4fb02fcfc5816e62716e34a9e27c506e2bedd9c8/rfc2307bis.schema'
        Sha256 = 'D209D2F1A7B0626C41571CF49D2782C0088E6C6B9E6F5D42A873889DAD3AC0CA'
    }
    @{
        Name   = 'sudo.schema'
        Url    = 'https://raw.githubusercontent.com/sudo-project/sudo/2afb054bcc05b410a4cab8d94b39f3cec2f02c97/docs/schema.OpenLDAP'
        Sha256 = 'C3B81A7FB19F1309FE830EC7760CBD99F7DE2AC0276496F51CDDB4B1A14173D4'
    }
    @{
        Name   = 'openssh-lpk.schema'
        Url    = 'https://raw.githubusercontent.com/AndriiGrytsenko/openssh-ldap-publickey/afc095a0e9be1ca0a1c47ac2891f70cda2cdbfe2/misc/openssh-lpk-openldap.schema'
        Sha256 = 'D456A6D55CFEA5168F2BE6DDD7E761ED2304062B4E89CFBD17BAD38F5AC0A21E'
    }
)

$contribDest = Join-Path $dest 'contrib'
if (Test-Path $contribDest) { Remove-Item $contribDest -Recurse -Force }
New-Item -ItemType Directory -Force $contribDest | Out-Null

foreach ($schema in $contrib) {
    $file = Join-Path $contribDest $schema.Name
    Invoke-WebRequest -Uri $schema.Url -OutFile $file
    $actual = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($actual -ne $schema.Sha256) {
        throw "$($schema.Name): SHA-256 mismatch. Expected $($schema.Sha256), got $actual. " +
              'The pinned source changed content unexpectedly — investigate before accepting.'
    }
    "contrib: $($schema.Name) verified ($actual)"
}
