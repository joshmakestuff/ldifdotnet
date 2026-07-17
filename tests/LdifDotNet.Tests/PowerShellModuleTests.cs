#pragma warning disable MA0048 // Deliberate: the gating attribute is colocated with the tests it gates

using System.Diagnostics;

namespace LdifDotNet.Tests;

/// <summary>
/// Runs only where pwsh (PowerShell 7+) is on PATH. With LDIF_REQUIRE_PWSH=1
/// (set in CI, whose images promise pwsh) a missing pwsh fails the tests
/// instead of silently skipping the coverage the job is supposed to provide.
/// </summary>
public sealed class PwshFactAttribute : FactAttribute
{
    public PwshFactAttribute()
    {
        if (PowerShellModuleTests.PwshPath is null
            && Environment.GetEnvironmentVariable("LDIF_REQUIRE_PWSH") != "1")
        {
            Skip = "pwsh (PowerShell 7+) not found on PATH.";
        }
    }
}

/// <summary>
/// End-to-end tests of the binary module: real pwsh imports the built manifest
/// and exercises the cmdlets. Runs on every CI OS.
/// </summary>
public class PowerShellModuleTests
{
    internal static string? PwshPath { get; } = FindPwsh();

    private static string ManifestPath =>
        Path.Combine(AppContext.BaseDirectory, "LdifDotNet.PowerShell.psd1");

    [PwshFact]
    public void Module_imports_cleanly_and_exports_exactly_the_specified_cmdlets()
    {
        string output = RunPwsh("""
            $warnings = @()
            Import-Module $args[0] -WarningVariable warnings -WarningAction SilentlyContinue
            "warnings=$($warnings.Count)"
            "cmdlets=" + (((Get-Command -Module LdifDotNet.PowerShell).Name | Sort-Object) -join ',')
            """, ManifestPath);

        Assert.Contains("warnings=0", output);
        Assert.Contains("cmdlets=ConvertFrom-Ldif,ConvertTo-Ldif,Export-Ldif,Import-Ldif", output);
    }

    [PwshFact]
    public void ConvertFrom_Ldif_parses_piped_file_content()
    {
        string output = RunPwsh("""
            Import-Module $args[0]
            Get-Content $args[1] | ConvertFrom-Ldif | ForEach-Object Dn
            """, ManifestPath, Fixtures.PathOf("rfc2849", "example1.ldif"));

        Assert.Contains("cn=Barbara Jensen, ou=Product Development, dc=airius, dc=com", output);
        Assert.Contains("cn=Bjorn Jensen, ou=Accounting, dc=airius, dc=com", output);
    }

    [PwshFact]
    public void Records_filter_naturally_in_the_pipeline()
    {
        string output = RunPwsh("""
            Import-Module $args[0]
            $found = Import-Ldif $args[1] | Where-Object Dn -like '*Accounting*'
            "count=$(@($found).Count)"
            "cn=$($found.cn)"
            """, ManifestPath, Fixtures.PathOf("rfc2849", "example1.ldif"));

        Assert.Contains("count=1", output);
        Assert.Contains("cn=Bjorn Jensen", output);
    }

    [PwshFact]
    public void Content_attributes_surface_as_friendly_properties()
    {
        string output = RunPwsh("""
            Import-Module $args[0]
            $e = Import-Ldif $args[1] | Where-Object Dn -like '*Barbara*'
            "sn=$($e.sn)"
            "sntype=$($e.sn.GetType().Name)"
            "cncount=$(@($e.cn).Count)"
            "occount=$(@($e.objectClass).Count)"
            "ci=$($e.SN -eq $e.sn)"
            "view=$($e.PSObject.TypeNames -contains 'LdifDotNet.PowerShell.LdifEntry')"
            """, ManifestPath, Fixtures.PathOf("rfc2849", "example1.ldif"));

        Assert.Contains("sn=Jensen", output);
        Assert.Contains("sntype=String", output);  // single value -> scalar
        Assert.Contains("cncount=3", output);       // several values -> array
        Assert.Contains("occount=3", output);
        Assert.Contains("ci=True", output);         // case-insensitive access
        Assert.Contains("view=True", output);       // formatting type name applied
    }

    [PwshFact]
    public void Binary_attribute_surfaces_as_byte_array()
    {
        string output = RunPwsh("""
            Import-Module $args[0]
            $e = "dn: dc=x`njpegPhoto:: AQID" | ConvertFrom-Ldif
            "isbytes=$($e.jpegPhoto -is [byte[]])"
            "bytes=$([string]::Join(',', $e.jpegPhoto))"
            """, ManifestPath);

        Assert.Contains("isbytes=True", output);
        Assert.Contains("bytes=1,2,3", output);
    }

    [PwshFact]
    public void Import_and_Export_round_trip_through_a_file()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"ldifdotnet-ps-{Guid.NewGuid():N}.ldif");
        try
        {
            string output = RunPwsh("""
                Import-Module $args[0]
                Import-Ldif $args[1] | Export-Ldif $args[2]
                $reimported = Import-Ldif $args[2]
                "count=$(@($reimported).Count)"
                "dn=$($reimported[0].Dn)"
                """, ManifestPath, Fixtures.PathOf("rfc2849", "example1.ldif"), temp);

            Assert.Contains("count=2", output);
            Assert.Contains("dn=cn=Barbara Jensen", output);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [PwshFact]
    public void Export_Ldif_WhatIf_does_not_create_the_destination()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"ldifdotnet-ps-{Guid.NewGuid():N}.ldif");
        try
        {
            string output = RunPwsh("""
                Import-Module $args[0]
                Import-Ldif $args[1] | Export-Ldif $args[2] -WhatIf
                "exists=$(Test-Path $args[2])"
                """, ManifestPath, Fixtures.PathOf("rfc2849", "example1.ldif"), temp);

            Assert.Contains("exists=False", output);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [PwshFact]
    public void Export_Ldif_NoClobber_refuses_existing_destination()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"ldifdotnet-ps-{Guid.NewGuid():N}.ldif");
        try
        {
            string output = RunPwsh("""
                Import-Module $args[0]
                Set-Content $args[2] 'original'
                try { Import-Ldif $args[1] | Export-Ldif $args[2] -NoClobber } catch { "error=$($_.FullyQualifiedErrorId)" }
                "content=$(Get-Content $args[2])"
                """, ManifestPath, Fixtures.PathOf("rfc2849", "example1.ldif"), temp);

            Assert.Contains("error=FileExists", output);
            Assert.Contains("content=original", output);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [PwshFact]
    public void Failed_export_preserves_existing_destination_and_leaves_no_temp_file()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"ldifdotnet-ps-{Guid.NewGuid():N}.ldif");
        try
        {
            string output = RunPwsh("""
                Import-Module $args[0]
                Set-Content $args[1] 'original'
                # The tolerant reader parses a dn-only record; the strict writer rejects it.
                $bad = "dn: dc=x" | ConvertFrom-Ldif
                try { $bad | Export-Ldif $args[1] } catch { "caught=True" }
                "content=$(Get-Content $args[1])"
                $leftover = @(Get-ChildItem -Path (Split-Path $args[1]) -Filter ((Split-Path $args[1] -Leaf) + '.*.tmp'))
                "tmpcount=$($leftover.Count)"
                """, ManifestPath, temp);

            Assert.Contains("caught=True", output);
            Assert.Contains("content=original", output);
            Assert.Contains("tmpcount=0", output);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [PwshFact]
    public void Export_Ldif_leaves_an_unrelated_sibling_tmp_untouched()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"ldifdotnet-ps-{Guid.NewGuid():N}.ldif");
        string sibling = temp + ".tmp";
        try
        {
            string output = RunPwsh("""
                Import-Module $args[0]
                Set-Content ($args[2] + '.tmp') 'do not touch'
                Import-Ldif $args[1] | Export-Ldif $args[2]
                "sibling=$(Get-Content ($args[2] + '.tmp'))"
                "exported=$(@(Import-Ldif $args[2]).Count)"
                """, ManifestPath, Fixtures.PathOf("rfc2849", "example1.ldif"), temp);

            Assert.Contains("sibling=do not touch", output);
            Assert.Contains("exported=2", output);
        }
        finally
        {
            File.Delete(temp);
            File.Delete(sibling);
        }
    }

    [PwshFact]
    public void ConvertTo_Ldif_produces_parseable_output_and_honors_NoWrap()
    {
        string output = RunPwsh("""
            Import-Module $args[0]
            $records = Import-Ldif $args[1]
            $ldif = $records | ConvertTo-Ldif -NoWrap -NoVersionLine
            "starts=$($ldif.StartsWith('dn: cn=Barbara'))"
            "roundtrip=$(@($ldif | ConvertFrom-Ldif).Count)"
            """, ManifestPath, Fixtures.PathOf("rfc2849", "example1.ldif"));

        Assert.Contains("starts=True", output);
        Assert.Contains("roundtrip=2", output);
    }

    [PwshFact]
    public void Change_records_surface_as_typed_objects()
    {
        string output = RunPwsh("""
            Import-Module $args[0]
            $records = Import-Ldif $args[1]
            "types=" + (($records | ForEach-Object { $_.GetType().Name } | Select-Object -Unique) -join ',')
            $modify = $records | Where-Object { $_ -is [LdifDotNet.LdifModifyRecord] } | Select-Object -First 1
            "mods=$($modify.Modifications.Count)"
            """, ManifestPath, Fixtures.PathOf("rfc2849", "example6.ldif"));

        Assert.Contains("LdifAddRecord", output);
        Assert.Contains("LdifDeleteRecord", output);
        Assert.Contains("LdifModDnRecord", output);
        Assert.Contains("LdifModifyRecord", output);
        Assert.Contains("mods=4", output);
    }

    private static string RunPwsh(string script, params string[] scriptArgs)
    {
        Assert.True(PwshPath is not null, "pwsh (PowerShell 7+) is not on PATH, but LDIF_REQUIRE_PWSH=1 promises it.");
        string scriptFile = Path.Combine(Path.GetTempPath(), $"ldifdotnet-test-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptFile, "$ErrorActionPreference = 'Stop'\n" + script);
        try
        {
            var startInfo = new ProcessStartInfo(PwshPath!)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptFile);
            foreach (string argument in scriptArgs)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)!;
            var stdOut = process.StandardOutput.ReadToEndAsync();
            var stdErr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, $"pwsh failed:\n{stdErr.Result}\n{stdOut.Result}");
            return stdOut.Result;
        }
        finally
        {
            File.Delete(scriptFile);
        }
    }

    private static string? FindPwsh()
    {
        string executable = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim(), executable);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
