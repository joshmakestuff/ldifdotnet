namespace LdifDotNet.Generator;

/// <summary>Options controlling fake directory generation.</summary>
public sealed class LdifGeneratorOptions
{
    /// <summary>
    /// Seed for deterministic output. The same seed, options and package version
    /// always produce the same records. Null uses a random seed.
    /// </summary>
    public int? Seed { get; set; }

    /// <summary>Bogus locale for names, addresses etc. Default "en".</summary>
    public string Locale { get; set; } = "en";

    /// <summary>Base DN of the generated tree. First RDN must be dc=, o= or ou=.</summary>
    public string BaseDn { get; set; } = "dc=example,dc=com";

    /// <summary>Number of person entries in <see cref="LdifGenerator.SampleDirectory"/>. Default 100.</summary>
    public int PeopleCount { get; set; } = 100;

    /// <summary>Number of group entries in <see cref="LdifGenerator.SampleDirectory"/>. Default 10.</summary>
    public int GroupCount { get; set; } = 10;

    /// <summary>
    /// Fraction of generated group members that reference a DN with no matching entry,
    /// for exercising consumers that must tolerate dangling references (not every LDAP
    /// deployment enforces referential integrity). 0 (default) keeps every member
    /// referentially sound and leaves output byte-identical to a generator without this
    /// option set; 1 makes every member dangle. Deterministic under <see cref="Seed"/>.
    /// </summary>
    public double DanglingMemberRatio { get; set; }
}
