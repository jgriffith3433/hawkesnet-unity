using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// Motor on the baked avatar prefab: Animator plus the walk/run thresholds the controller was built with.
    /// It is a top-level class in a file of its own name on purpose: Unity can only save a MonoBehaviour into a prefab
    /// (and load it in a player build) when it has a MonoScript, and a nested class has none, so it came back as
    /// "The referenced script on this Behaviour (Game Object 'HawkesNetAvatar') is missing!".
    /// </summary>
    public sealed class HawkesNetAvatarMotor : MonoBehaviour
    {
        public Animator Animator;
        public float WalkSpeed = 1.2f;
        public float RunSpeed = 3.5f;
    }
}
