#if UNITY_INCLUDE_TESTS
// EditMode tests for world-space nameplates (quality rule 1: never mirrored / no negative scale).
using NUnit.Framework;
using UnityEngine;

namespace Hawkes.Net.Tests
{
    public class HawkesNetNamePlateTests
    {
        Camera cam;

        [SetUp]
        public void SetUp()
        {
            cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 2f, -6f);
            cam.transform.LookAt(Vector3.up * 1.2f);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in Object.FindObjectsOfType<GameObject>())
                Object.DestroyImmediate(go);
        }

        [Test]
        public void NamePlateScalesStayPositive()
        {
            var root = new GameObject("Player").transform;
            root.position = Vector3.zero;
            var plate = HawkesNetNamePlate.Attach(root, "Ada", Color.cyan);
            Assert.IsNotNull(plate);
            plate.Set("Ada", Color.cyan);
            Assert.IsTrue(HawkesNetNamePlate.AllScalesPositive(out var offender), "negative scale on " + offender);
            Assert.IsFalse(HawkesNetNamePlate.HasNegativeScale(plate.transform));
            foreach (var t in plate.GetComponentsInChildren<Transform>(true))
                Assert.IsFalse(HawkesNetNamePlate.HasNegativeScale(t), t.name + " has a negative scale");
        }

        [Test]
        public void NegativeScaleOnTextIsDetected()
        {
            var go = new GameObject("BadText");
            var tm = go.AddComponent<TextMesh>();
            tm.text = "Mirror";
            go.transform.localScale = new Vector3(-1f, 1f, 1f);
            Assert.IsFalse(HawkesNetNamePlate.AllScalesPositive(out var offender));
            Assert.AreEqual("Mirror", offender);
        }

        [Test]
        public void CameraCopyLeavesNoNegativeScaleAfterOrbitAngles()
        {
            var root = new GameObject("Remote").transform;
            root.position = new Vector3(2f, 0f, 1f);
            var plate = HawkesNetNamePlate.Attach(root, "Bot", Color.yellow);
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f * Mathf.Deg2Rad;
                cam.transform.position = root.position + new Vector3(Mathf.Cos(ang) * 5f, 2.5f, Mathf.Sin(ang) * 5f);
                cam.transform.LookAt(root.position + Vector3.up * 1.2f);
                // Copy camera rotation the same way LateUpdate does.
                plate.transform.rotation = cam.transform.rotation;
                plate.transform.localScale = Vector3.one * 1.2f;
                Assert.IsFalse(HawkesNetNamePlate.HasNegativeScale(plate.transform), "angle " + i);
                Assert.Greater(Vector3.Dot(plate.transform.forward, cam.transform.forward), 0.9f, "should copy camera forward");
            }
            Assert.IsTrue(HawkesNetNamePlate.AllScalesPositive(out _));
        }
    }
}
#endif
