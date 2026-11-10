#if UNITY_INCLUDE_TESTS
// EditMode tests for the Avatars sample scene audit (quality rules 2, 3, 4, 11).
using NUnit.Framework;
using UnityEngine;

namespace Hawkes.Net.Tests
{
    public class HawkesNetSceneAuditTests
    {
        GameObject ground;
        Camera cam;

        [SetUp]
        public void SetUp()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2.4f, 1f, 2.4f); // 24 m like ArenaHalf 12
            cam = new GameObject("Camera").AddComponent<Camera>();
            cam.nearClipPlane = HawkesNetSceneAudit.MinNearClip;
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in Object.FindObjectsOfType<GameObject>())
                Object.DestroyImmediate(go);
        }

        static GameObject Prop(string name, Vector3 centre, Vector3 size, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = centre;
            go.transform.localScale = size;
            go.transform.rotation = rot;
            HawkesNetSceneAudit.SnapBoundsToGround(go.transform);
            Physics.SyncTransforms();
            return go;
        }

        [Test]
        public void CleanArenaPropsPassTheStaticAudit()
        {
            Prop("Step", new Vector3(-8f, 0.25f, 6f), new Vector3(2.5f, 0.5f, 2.5f), Quaternion.identity);
            Prop("Bench E", new Vector3(9f, 0.2f, 0f), new Vector3(0.5f, 0.4f, 2.2f), Quaternion.identity);
            Prop("Banner N", new Vector3(0f, 0.8f, 11.2f), new Vector3(3f, 1.6f, 0.12f), Quaternion.identity);
            Prop("Ramp", new Vector3(8f, 0.35f, -6f), new Vector3(4f, 0.7f, 2.5f), Quaternion.Euler(0f, 0f, -12f));
            Prop("Post NE", new Vector3(10f, 1f, 10f), new Vector3(0.4f, 2f, 0.4f), Quaternion.identity);

            var report = new HawkesNetSceneAudit.Report();
            HawkesNetSceneAudit.AuditStatic(report, cam);

            Assert.IsEmpty(report.Floating, "floating: " + string.Join(", ", report.Floating));
            Assert.IsEmpty(report.Sunk, "sunk: " + string.Join(", ", report.Sunk));
            Assert.IsEmpty(report.ZFight, "zfight: " + string.Join(", ", report.ZFight));
            Assert.IsEmpty(report.InsideOut, "insideOut: " + string.Join(", ", report.InsideOut));
            Assert.IsTrue(report.NearClipOk, "nearClip " + report.NearClip);
        }

        [Test]
        public void APropLiftedOffTheGroundFailsWithItsName()
        {
            var bench = Prop("Bench E", new Vector3(9f, 0.2f, 0f), new Vector3(0.5f, 0.4f, 2.2f), Quaternion.identity);
            bench.transform.position += new Vector3(0f, 0.25f, 0f); // 25 cm float
            Physics.SyncTransforms();

            var report = new HawkesNetSceneAudit.Report();
            HawkesNetSceneAudit.AuditStatic(report, cam);

            Assert.Contains("Bench E", report.Floating);
            Assert.IsEmpty(report.Sunk);
        }

        [Test]
        public void NegativeScaleIsFlaggedInsideOut()
        {
            var go = Prop("Mirror Post", new Vector3(0f, 1f, 0f), new Vector3(0.4f, 2f, 0.4f), Quaternion.identity);
            go.transform.localScale = new Vector3(-0.4f, 2f, 0.4f);
            Physics.SyncTransforms();

            var report = new HawkesNetSceneAudit.Report();
            HawkesNetSceneAudit.AuditStatic(report, cam);

            Assert.Contains("Mirror Post", report.InsideOut);
        }

        [Test]
        public void OrbitAroundTheArenaKeepsTheCameraClear()
        {
            Prop("Post NE", new Vector3(10f, 1f, 10f), new Vector3(0.4f, 2f, 0.4f), Quaternion.identity);
            Prop("Banner N", new Vector3(0f, 0.8f, 11.2f), new Vector3(3f, 1.6f, 0.12f), Quaternion.identity);
            // Edge wall (collider kept) at the north rim — orbit radius 10 stays inside.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Edge wall";
            wall.transform.position = new Vector3(0f, 1f, 12.25f);
            wall.transform.localScale = new Vector3(25f, 2f, 0.5f);
            Physics.SyncTransforms();

            var report = new HawkesNetSceneAudit.Report();
            HawkesNetSceneAudit.AuditStatic(report, cam);
            HawkesNetSceneAudit.OrbitSteps(new Vector3(0f, 1.2f, 0f), 10f, 5f, 72, report);

            Assert.AreEqual(0, report.CameraInsideFrames, "inside: " + string.Join(", ", report.CameraInside));
            Assert.IsTrue(report.Done);
            Assert.Greater(report.OrbitFrames, 0);
        }

        [Test]
        public void NearClipBelowMinimumFails()
        {
            cam.nearClipPlane = 0.01f;
            var report = new HawkesNetSceneAudit.Report();
            HawkesNetSceneAudit.AuditStatic(report, cam);
            Assert.IsFalse(report.NearClipOk);
        }
    }
}
#endif
