@{
    RootModule           = 'LdifDotNet.PowerShell.dll'
    ModuleVersion        = '0.1.0'
    FormatsToProcess     = @('LdifDotNet.PowerShell.format.ps1xml')
    GUID                 = 'e5b3a4d2-9c41-4f8e-b6d7-2a8f0c1d5e93'
    Author               = 'Joshua Toon'
    Copyright            = '(c) 2026 Joshua Toon. Licensed under the MIT License.'
    Description          = 'Read and write LDIF (RFC 2849): ConvertFrom-Ldif, ConvertTo-Ldif, Import-Ldif, Export-Ldif.'
    PowerShellVersion    = '7.6'
    CompatiblePSEditions = @('Core')
    CmdletsToExport      = @('ConvertFrom-Ldif', 'ConvertTo-Ldif', 'Import-Ldif', 'Export-Ldif')
    FunctionsToExport    = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
    PrivateData          = @{
        PSData = @{
            Tags       = @('LDIF', 'LDAP', 'RFC2849', 'OpenLDAP')
            LicenseUri = 'https://github.com/joshmakestuff/ldifdotnet/blob/main/LICENSE'
            ProjectUri = 'https://github.com/joshmakestuff/ldifdotnet'
        }
    }
}
