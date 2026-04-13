using System;
using System.Reflection;
using UnityEngine;

namespace SavitGame.AR {
    /// <summary>
    /// Bridges a MediaPipe Selfie Segmentation solution output to PersonOcclusionMaskSource.
    ///
    /// This script intentionally avoids compile-time dependency on any MediaPipe package.
    /// Drag the MediaPipe solution component (or its GameObject) into mediaPipeSolutionObject.
    /// The bridge uses reflection to find a Texture/Texture2D/RenderTexture field or property
    /// likely containing the segmentation mask.
    /// </summary>
    public sealed class MediaPipePersonMaskBridge : MonoBehaviour {
        [Header("Target")]
        public PersonOcclusionMaskSource target;

        [Header("MediaPipe Source")]
        [Tooltip("Drag the MediaPipe SelfieSegmentation Solution component here (or its GameObject).")]
        public UnityEngine.Object mediaPipeSolutionObject;

        [Tooltip("If set, will also look for a component with this FullName on the given GameObject.")]
        public string preferredComponentFullName = "Mediapipe.Unity.SelfieSegmentation.SelfieSegmentationSolution";

        [Tooltip("How often to poll the solution for a mask texture.")]
        [Range(0.01f, 0.2f)]
        public float pollIntervalSeconds = 0.033f;

        private object solutionInstance;
        private MemberInfo maskMember;
        private bool warnedMissing;
        private float nextPollTime;

        private static readonly string[] CandidateMemberNames = {
            "segmentationMaskTexture",
            "SegmentationMaskTexture",
            "maskTexture",
            "MaskTexture",
            "outputMaskTexture",
            "OutputMaskTexture"
        };

        private void Awake() {
            if (target == null)
                target = GetComponent<PersonOcclusionMaskSource>();
        }

        private void Update() {
            if (target == null)
                return;

            if (Time.unscaledTime < nextPollTime)
                return;

            nextPollTime = Time.unscaledTime + pollIntervalSeconds;

            if (!EnsureSolutionBound()) {
                if (!warnedMissing && mediaPipeSolutionObject != null) {
                    Debug.LogWarning($"[PersonOcclusion] MediaPipe bridge couldn't bind to a solution component on '{mediaPipeSolutionObject.name}'. Drag the SelfieSegmentationSolution component directly if possible.");
                    warnedMissing = true;
                }
                return;
            }

            if (maskMember == null)
                maskMember = ResolveMaskMember(solutionInstance);

            if (maskMember == null) {
                if (!warnedMissing) {
                    Debug.LogWarning($"[PersonOcclusion] MediaPipe bridge couldn't find a mask Texture member on '{solutionInstance.GetType().FullName}'. You may need to customize MediaPipePersonMaskBridge to your plugin version.");
                    warnedMissing = true;
                }
                return;
            }

            var maskTex = ReadTextureMember(solutionInstance, maskMember);
            if (maskTex == null)
                return;

            if (target.maskTexture != maskTex)
                target.maskTexture = maskTex;
        }

        private bool EnsureSolutionBound() {
            if (solutionInstance != null)
                return true;

            if (mediaPipeSolutionObject == null)
                return false;

            // If they dragged the component itself.
            if (mediaPipeSolutionObject is Component c) {
                solutionInstance = c;
                return true;
            }

            // If they dragged a GameObject, try to get the preferred component.
            if (mediaPipeSolutionObject is GameObject go) {
                var comps = go.GetComponents<Component>();
                foreach (var comp in comps) {
                    if (comp == null)
                        continue;
                    if (string.Equals(comp.GetType().FullName, preferredComponentFullName, StringComparison.Ordinal)) {
                        solutionInstance = comp;
                        return true;
                    }
                }

                // Fallback: if there's only one MonoBehaviour, take it.
                foreach (var comp in comps) {
                    if (comp is MonoBehaviour) {
                        solutionInstance = comp;
                        return true;
                    }
                }
            }

            // Any other UnityEngine.Object – just use it.
            solutionInstance = mediaPipeSolutionObject;
            return solutionInstance != null;
        }

        private static MemberInfo ResolveMaskMember(object instance) {
            var type = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Prefer known member names.
            foreach (var name in CandidateMemberNames) {
                var p = type.GetProperty(name, flags);
                if (p != null && typeof(Texture).IsAssignableFrom(p.PropertyType))
                    return p;

                var f = type.GetField(name, flags);
                if (f != null && typeof(Texture).IsAssignableFrom(f.FieldType))
                    return f;
            }

            // Heuristic: first Texture-like property containing "mask".
            foreach (var p in type.GetProperties(flags)) {
                if (!typeof(Texture).IsAssignableFrom(p.PropertyType))
                    continue;
                if (p.Name.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0)
                    return p;
            }

            foreach (var f in type.GetFields(flags)) {
                if (!typeof(Texture).IsAssignableFrom(f.FieldType))
                    continue;
                if (f.Name.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0)
                    return f;
            }

            return null;
        }

        private static Texture ReadTextureMember(object instance, MemberInfo member) {
            try {
                switch (member) {
                    case PropertyInfo p:
                        return p.GetValue(instance, null) as Texture;
                    case FieldInfo f:
                        return f.GetValue(instance) as Texture;
                    default:
                        return null;
                }
            } catch {
                return null;
            }
        }
    }
}
