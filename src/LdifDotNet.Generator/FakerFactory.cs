using Bogus;

namespace LdifDotNet.Generator;

/// <summary>
/// The single construction site for generator fakers, so both generators share
/// one determinism configuration and cannot drift.
/// </summary>
internal static class FakerFactory
{
    /// <summary>
    /// Fixed reference instant for all time-derived values. Bogus date methods
    /// and tokens (e.g. {{date.past}}) are relative to the faker's
    /// DateTimeReference — left unset they read the wall clock, which would
    /// break seeded determinism.
    /// </summary>
    internal static readonly DateTime GenerationEpoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Creates a faker pinned to <see cref="GenerationEpoch"/>; null seed keeps Bogus's random seed.</summary>
    internal static Faker Create(string locale, int? seed)
    {
        var faker = new Faker(locale) { DateTimeReference = GenerationEpoch };
        if (seed is { } value)
            faker.Random = new Randomizer(value);
        return faker;
    }
}
