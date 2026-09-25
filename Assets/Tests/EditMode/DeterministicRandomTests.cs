using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Guards the reproducibility contract the save system depends on: a seed must always
/// produce the same stream, and different seeds must not collide in practice.
/// </summary>
public class DeterministicRandomTests
{
    [Test]
    public void SameSeedProducesSameSequence()
    {
        var first = new DeterministicRandom("dungeon-1");
        var second = new DeterministicRandom("dungeon-1");

        for (int i = 0; i < 1000; i++)
            Assert.AreEqual(first.NextUInt(), second.NextUInt(), $"diverged at draw {i}");
    }

    [Test]
    public void DifferentSeedsProduceDifferentSequences()
    {
        var first = new DeterministicRandom("dungeon-1");
        var second = new DeterministicRandom("dungeon-2");

        bool differs = false;
        for (int i = 0; i < 32 && !differs; i++)
            differs = first.NextUInt() != second.NextUInt();

        Assert.IsTrue(differs, "two different seeds produced 32 identical draws");
    }

    [Test]
    public void ShortSeedsDoNotCollide()
    {
        // Short numeric seeds are the realistic worst case: players type "1", "2", "3".
        var streams = new HashSet<uint>();
        for (int i = 0; i < 2000; i++)
            streams.Add(new DeterministicRandom(i.ToString()).NextUInt());

        Assert.AreEqual(2000, streams.Count, "short seeds collided on their first draw");
    }

    [Test]
    public void DerivedStreamsAreIndependentAndReproducible()
    {
        var parent = new DeterministicRandom("seed");
        uint rooms = parent.Derive("rooms").NextUInt();
        uint links = parent.Derive("links").NextUInt();

        Assert.AreNotEqual(rooms, links, "two salts produced the same stream");
        Assert.AreEqual(rooms, new DeterministicRandom("seed").Derive("rooms").NextUInt());
    }

    [Test]
    public void RangeStaysWithinBounds()
    {
        var random = new DeterministicRandom("range");
        for (int i = 0; i < 10000; i++)
        {
            int value = random.Range(-5, 7);
            Assert.GreaterOrEqual(value, -5);
            Assert.Less(value, 7);
        }
    }

    [Test]
    public void RangeIsUnbiasedAcrossBuckets()
    {
        var random = new DeterministicRandom("bias");
        var counts = new int[6];
        const int draws = 60000;

        for (int i = 0; i < draws; i++)
            counts[random.Range(0, counts.Length)]++;

        // Rejection sampling should keep every bucket within a few percent of 1/6.
        int expected = draws / counts.Length;
        foreach (int count in counts)
            Assert.That(count, Is.EqualTo(expected).Within(expected * 0.1f));
    }

    [Test]
    public void EmptyRangeReturnsMinimum()
    {
        var random = new DeterministicRandom("empty");
        Assert.AreEqual(4, random.Range(4, 4));
        Assert.AreEqual(4, random.Range(4, 1));
    }

    [Test]
    public void NextFloatStaysInUnitInterval()
    {
        var random = new DeterministicRandom("float");
        for (int i = 0; i < 10000; i++)
        {
            float value = random.NextFloat();
            Assert.GreaterOrEqual(value, 0f);
            Assert.Less(value, 1f);
        }
    }

    [Test]
    public void ShuffleIsAPermutationAndReproducible()
    {
        List<int> shuffled = Sequence(50);
        new DeterministicRandom("shuffle").Shuffle(shuffled);

        CollectionAssert.AreEquivalent(Sequence(50), shuffled);
        CollectionAssert.AreNotEqual(Sequence(50), shuffled, "shuffle left the order untouched");

        List<int> again = Sequence(50);
        new DeterministicRandom("shuffle").Shuffle(again);
        CollectionAssert.AreEqual(shuffled, again);
    }

    private static List<int> Sequence(int count)
    {
        var items = new List<int>(count);
        for (int i = 0; i < count; i++) items.Add(i);
        return items;
    }
}
