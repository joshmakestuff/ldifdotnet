# Captures the subschema subentry a real OpenLDAP 2.6 server publishes into
# tests/fixtures/subschema/openldap-2.6.ldif. The fixture is what a live
# server's cn=Subschema actually answers — runtime-generated output that cannot
# be vendored from the OpenLDAP source tree. Requires docker. Re-run to
# refresh; never hand-edit the captured file.
param(
    # Debian 13 ships OpenLDAP 2.6; the digest pins the base image, and the
    # script prints the exact slapd version each capture actually ran.
    [string]$Image = 'debian:13-slim@sha256:020c0d20b9880058cbe785a9db107156c3c75c2ac944a6aa7ab59f2add76a7bd'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $repoRoot 'tests/fixtures/subschema'
New-Item -ItemType Directory -Force $dest | Out-Null

# The slapd.conf mirrors the differential CI job's schema set
# (core + cosine + inetorgperson), so fixture and live test agree.
$script = @'
set -e
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq >/dev/null
apt-get install -y -qq --no-install-recommends slapd ldap-utils >/dev/null 2>&1
mkdir -p /tmp/ldap-data /run/slapd
cat > /tmp/slapd.conf <<EOF
include /etc/ldap/schema/core.schema
include /etc/ldap/schema/cosine.schema
include /etc/ldap/schema/inetorgperson.schema
modulepath /usr/lib/ldap
moduleload back_mdb
database mdb
suffix "dc=example,dc=com"
rootdn "cn=admin,dc=example,dc=com"
directory /tmp/ldap-data
EOF
/usr/sbin/slapd -f /tmp/slapd.conf -h ldapi:///
for i in $(seq 1 50); do
    ldapsearch -H ldapi:/// -x -s base -b "" "(objectClass=*)" namingContexts >/dev/null 2>&1 && break
    sleep 0.2
done
/usr/sbin/slapd -V 2>&1 | head -1
ldapsearch -LL -H ldapi:/// -x -s base -b cn=Subschema "(objectClass=subschema)" \
    attributeTypes objectClasses ldapSyntaxes > /out/openldap-2.6.ldif
'@

docker run --rm -v "${dest}:/out" $Image bash -c $script
if ($LASTEXITCODE -ne 0) { throw "docker capture failed with exit code $LASTEXITCODE" }

$fixture = Join-Path $dest 'openldap-2.6.ldif'
$text = Get-Content $fixture -Raw
$counts = 'attributeTypes', 'objectClasses', 'ldapSyntaxes' | ForEach-Object {
    "$_=$(([regex]::Matches($text, "(?m)^$($_):")).Count)"
}
"Captured $fixture ($counts)."
