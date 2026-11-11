#if UNITY_INCLUDE_TESTS
// EditMode tests for the damped follow camera (SphereCast pull-in, turn cap, idle recentre).
using NUnit.Framework;
using UnityEngine;

namespace Hawkes.Net.Tests
{
    public class HawkesNetFollowCameraTests
    {
        Transform player;
        Camera cam;
        HawkesNetFollowCamera follow;

        [SetUp]
        public void SetUp()
        {
            HawkesNetSceneAudit.ResetFollowInside();
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2.4f, 1f, 2.4f);
            player = new GameObject("Player").transform;
            player.position = Vector3.zero;
            player.rotation = Quaternion.identity;
            cam = new GameObject("Camera").AddComponent<Camera>();
            cam.nearClipPlane = HawkesNetSceneAudit.MinNearClip;
            follow = HawkesNetFollowCamera.Attach(cam, player);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in Object.FindObjectsOfType<GameObject>())
                Object.DestroyImmediate(go);
            HawkesNetSceneAudit.ResetFollowInside();
        }

        [Test]
        public void AttachStartsBehindTheAvatar()
        {
            Assert.AreEqual(0f, follow.Yaw, 0.5f);
            Assert.AreEqual(follow.DefaultPitch, follow.Pitch, 0.5f);
            var look = (player.position + Vector3.up * follow.LookHeight - cam.transform.position).normalized;
            Assert.Greater(Vector3.Dot(cam.transform.forward, look), 0.95f);
        }

        [Test]
        public void AWallBetweenAvatarAndCameraPullsTheDistanceIn()
        {
            player.rotation = Quaternion.identity;
            follow.ResetBehindTarget();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Edge wall";
            wall.transform.position = new Vector3(0f, 1f, -3f);
            wall.transform.localScale = new Vector3(8f, 3f, 0.5f);
            Physics.SyncTransforms();

            // Several ticks so pull-in rate reaches the SphereCast hit.
            for (int i = 0; i < 30; i++) follow.Tick(1f / 60f, 1f / 60f);
            float dist = Vector3.Distance(cam.transform.position, player.position + Vector3.up * follow.LookHeight);
            Assert.Less(dist, follow.Distance - 0.5f, "camera should pull in short of the wall (dist=" + dist + ")");
            Assert.IsFalse(HawkesNetSceneAudit.CameraInsideStatic(cam.transform.position, out _), "eye must stay outside the wall");
            Assert.AreEqual(0, follow.InsideFrames);
        }

        [Test]
        public void TurnRateNeverExceedsThirtyDegreesInOneStep()
        {
            follow.ResetBehindTarget();
            follow.SetWantedYaw(180f);
            float before = follow.Yaw;
            follow.Tick(1f / 60f, 1f / 60f);
            float delta = Mathf.Abs(Mathf.DeltaAngle(before, follow.Yaw));
            Assert.LessOrEqual(delta, HawkesNetFollowCamera.MaxTurnDegPerFrame + 0.01f, "yaw delta " + delta);
            Assert.LessOrEqual(follow.MaxYawDelta, HawkesNetFollowCamera.MaxTurnDegPerFrame + 0.01f);
        }

        [Test]
        public void IdleRecentreBringsYawBehindTheAvatar()
        {
            follow.ResetBehindTarget();
            player.rotation = Quaternion.Euler(0f, 90f, 0f);
            follow.SetWantedYaw(0f);
            follow.SetStillFor(HawkesNetFollowCamera.IdleSeconds + 0.1f);
            for (int i = 0; i < 180; i++) follow.Tick(1f / 60f, 1f / 60f);
            Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(follow.Yaw, 90f)), HawkesNetFollowCamera.IdleBehindToleranceDeg + 1f,
                "yaw=" + follow.Yaw);
            Assert.IsTrue(follow.IdleBehindOk);
        }
    }
}
#endif
