// DSpaceSerializerTests.cs - The custom JSON layer that DataPersistence relies on

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DaemonVision.Data;

namespace DaemonVision.Tests
{
    public class DSpaceSerializerTests
    {
        [Serializable]
        private class Inner
        {
            public string Label;
            public Vector3 Position;
            public float? OptionalScore;
        }

        [Serializable]
        private class Outer
        {
            public string Name;
            public int Count;
            public List<Inner> Items = new List<Inner>();
            public Dictionary<string, int> Tally = new Dictionary<string, int>();
            [NonSerialized] public int Transient = 42;
        }

        [Test]
        public void ComplexObject_RoundTrips()
        {
            var original = new Outer
            {
                Name = "quote \"me\" and \\ slash\nnewline",
                Count = 3,
                Items =
                {
                    new Inner { Label = "a", Position = new Vector3(1.5f, -2f, 3.25f), OptionalScore = 0.5f },
                    new Inner { Label = "b", Position = Vector3.zero, OptionalScore = null }
                },
                Tally = { { "alpha", 1 }, { "beta", 2 } },
                Transient = 99
            };

            string json = DSpaceSerializer.Serialize(original);
            var restored = DSpaceSerializer.Deserialize<Outer>(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(original.Name, restored.Name);
            Assert.AreEqual(3, restored.Count);
            Assert.AreEqual(2, restored.Items.Count);
            Assert.AreEqual("a", restored.Items[0].Label);
            Assert.AreEqual(new Vector3(1.5f, -2f, 3.25f), restored.Items[0].Position);
            Assert.AreEqual(0.5f, restored.Items[0].OptionalScore);
            Assert.IsNull(restored.Items[1].OptionalScore);
            Assert.AreEqual(2, restored.Tally.Count);
            Assert.AreEqual(1, restored.Tally["alpha"]);
            Assert.AreEqual(2, restored.Tally["beta"]);
            Assert.AreEqual(42, restored.Transient, "NonSerialized fields keep their default");
        }

        [Test]
        public void Deserialize_ReturnsDefaultForMalformedInput()
        {
            Assert.IsNull(DSpaceSerializer.Deserialize<Outer>("this is not json"));
            Assert.IsNull(DSpaceSerializer.Deserialize<Outer>(""));
            Assert.IsNull(DSpaceSerializer.Deserialize<Outer>("null"));
        }

        [Test]
        public void Serialize_UsesInvariantCultureForFloats()
        {
            string json = DSpaceSerializer.Serialize(new Inner { Label = "x", Position = new Vector3(0.5f, 0, 0) });
            StringAssert.Contains("0.5", json);
            StringAssert.DoesNotContain("0,5", json);
        }

        [Test]
        public void Dictionary_WithEnumKeys_RoundTrips()
        {
            var original = new Dictionary<DayOfWeek, string> { { DayOfWeek.Monday, "m" }, { DayOfWeek.Friday, "f" } };

            string json = DSpaceSerializer.Serialize(original);
            var restored = DSpaceSerializer.Deserialize<Dictionary<DayOfWeek, string>>(json);

            Assert.AreEqual("m", restored[DayOfWeek.Monday]);
            Assert.AreEqual("f", restored[DayOfWeek.Friday]);
        }
    }
}
