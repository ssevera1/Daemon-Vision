// SpatialMathTests.cs - Haversine distance, address helpers, and detection NMS

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Detection;
using DaemonVision.Spatial;

namespace DaemonVision.Tests
{
    public class SpatialMathTests
    {
        [Test]
        public void DistanceBetween_OneDegreeOfLatitude_IsAboutOneHundredElevenKilometres()
        {
            float meters = GPSLocationProvider.DistanceBetween(0, 0, 1, 0);
            Assert.AreEqual(111319f, meters, 500f);
        }

        [Test]
        public void DistanceBetween_SamePoint_IsZero()
        {
            Assert.AreEqual(0f, GPSLocationProvider.DistanceBetween(37.7749, -122.4194, 37.7749, -122.4194), 1e-3f);
        }

        [Test]
        public void DistanceBetween_IsSymmetric()
        {
            float ab = GPSLocationProvider.DistanceBetween(51.5074, -0.1278, 48.8566, 2.3522);
            float ba = GPSLocationProvider.DistanceBetween(48.8566, 2.3522, 51.5074, -0.1278);
            Assert.AreEqual(ab, ba, 0.01f);
            // London to Paris is roughly 343 km
            Assert.AreEqual(343000f, ab, 3000f);
        }

        [Test]
        public void AddressUtil_HandlesNullShortAndLongInput()
        {
            Assert.AreEqual(string.Empty, AddressUtil.Prefix(null));
            Assert.AreEqual("abc", AddressUtil.Prefix("abc"));
            Assert.AreEqual("01234567", AddressUtil.Prefix("0123456789abcdef"));

            Assert.AreEqual("(unknown)", AddressUtil.Short(null));
            Assert.AreEqual("abc", AddressUtil.Short("abc"));
            Assert.AreEqual("01234567...", AddressUtil.Short("0123456789abcdef"));
        }

        [Test]
        public void NonMaxSuppression_KeepsHighestConfidenceOfOverlappingBoxes()
        {
            var detections = new List<DetectionResult>
            {
                new DetectionResult { BoundingBox = new Rect(0.10f, 0.10f, 0.30f, 0.60f), Confidence = 0.6f },
                new DetectionResult { BoundingBox = new Rect(0.12f, 0.11f, 0.30f, 0.60f), Confidence = 0.9f },
                new DetectionResult { BoundingBox = new Rect(0.70f, 0.20f, 0.20f, 0.50f), Confidence = 0.8f }
            };

            var kept = MLPersonDetector.NonMaxSuppression(detections, 0.45f);

            Assert.AreEqual(2, kept.Count);
            Assert.AreEqual(0.9f, kept[0].Confidence, 1e-6f);
            Assert.AreEqual(0.8f, kept[1].Confidence, 1e-6f);
        }

        [Test]
        public void NonMaxSuppression_EmptyInput_ProducesEmptyOutput()
        {
            Assert.AreEqual(0, MLPersonDetector.NonMaxSuppression(new List<DetectionResult>(), 0.5f).Count);
            Assert.AreEqual(0, MLPersonDetector.NonMaxSuppression(null, 0.5f).Count);
        }
    }
}
