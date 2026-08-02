using System.Collections.Generic;

/// <summary>
/// Seeded pseudo-random source used by every stage of dungeon generation.
///
/// Deliberately not <c>UnityEngine.Random</c> (global mutable state shared with the
/// rest of the game) and deliberately not <c>string.GetHashCode()</c> (not guaranteed
/// stable across runtimes or Unity versions). Both would silently break the guarantee
/// the save system relies on: the same seed must rebuild the same dungeon, on any
/// machine, forever.
///
/// Seed strings are hashed with FNV-1a; the stream itself is xorshift128.
/// </summary>
public sealed class DeterministicRandom
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    private uint _x, _y, _z, _w;

    /// <summary>The seed string this stream was created from.</summary>
    public string Seed { get; }

    /// <summary>Creates a stream from a seed string. Equal strings always give equal streams.</summary>
    public DeterministicRandom(string seed)
    {
        Seed = seed ?? string.Empty;

        uint hash = Hash(Seed);
        // Xorshift is degenerate when the whole state is zero; the constants also
        // decorrelate the four words so short seeds ("1", "2") don't produce
        // near-identical streams.
        _x = hash != 0 ? hash : 0x9E3779B9;
        _y = _x ^ 0x85EBCA6B;
        _z = _x ^ 0xC2B2AE35;
        _w = _x ^ 0x27D4EB2F;

        // Discard the first few outputs so low-entropy seeds mix before first use.
        for (int i = 0; i < 8; i++) NextUInt();
    }

    /// <summary>
    /// Creates an independent stream for a sub-system, so that changing how many
    /// numbers one stage consumes cannot shift every later stage's results.
    /// </summary>
    public DeterministicRandom Derive(string salt)
    {
        return new DeterministicRandom($"{Seed}::{salt}");
    }

    /// <summary>Stable 32-bit FNV-1a hash of a string, computed over UTF-16 code units.</summary>
    public static uint Hash(string value)
    {
        uint hash = FnvOffsetBasis;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            hash = (hash ^ (byte)(c & 0xFF)) * FnvPrime;
            hash = (hash ^ (byte)(c >> 8)) * FnvPrime;
        }
        return hash;
    }

    /// <summary>Next raw 32-bit value.</summary>
    public uint NextUInt()
    {
        uint t = _x ^ (_x << 11);
        _x = _y; _y = _z; _z = _w;
        _w = _w ^ (_w >> 19) ^ t ^ (t >> 8);
        return _w;
    }

    /// <summary>Uniform value in <c>[0, 1)</c>.</summary>
    public float NextFloat()
    {
        // 24 bits: the most a float can represent without rounding to 1.0.
        return (NextUInt() >> 8) / (float)(1 << 24);
    }

    /// <summary>
    /// Uniform integer in <c>[minInclusive, maxExclusive)</c>. Returns
    /// <paramref name="minInclusive"/> when the range is empty.
    /// </summary>
    public int Range(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        uint span = (uint)(maxExclusive - minInclusive);
        // Rejection sampling: plain modulo would bias the low end of the range.
        uint limit = uint.MaxValue - (uint.MaxValue % span);
        uint value;
        do { value = NextUInt(); } while (value >= limit);
        return minInclusive + (int)(value % span);
    }

    /// <summary>Uniform integer in <c>[minInclusive, maxInclusive]</c>.</summary>
    public int RangeInclusive(int minInclusive, int maxInclusive)
    {
        return Range(minInclusive, maxInclusive + 1);
    }

    /// <summary>True with the given probability; <c>p &lt;= 0</c> never, <c>p &gt;= 1</c> always.</summary>
    public bool Chance(float probability)
    {
        if (probability <= 0f) return false;
        if (probability >= 1f) return true;
        return NextFloat() < probability;
    }

    /// <summary>Returns a random element, or <c>default</c> for an empty list.</summary>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items == null || items.Count == 0) return default;
        return items[Range(0, items.Count)];
    }

    /// <summary>In-place Fisher-Yates shuffle.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        if (items == null) return;
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Range(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
