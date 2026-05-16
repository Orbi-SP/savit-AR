#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace SavitGame.Editor.BuildValidation {
    /// <summary>
    /// Prevents shipping an Android build where the core runtime prefab is accidentally disabled in the AR build scene.
    /// This project wires Api/MediaPipe to live inside the scene PrefabInstance named "GameScenePrefab".
    /// If that instance is inactive, the hand-tracking "IA" never runs.
    /// </summary>
    public sealed class EnsureGameScenePrefabActiveInBuildScene : IPreprocessBuildWithReport {
        public int callbackOrder => 0;

        private const string BuildScenePath = "Assets/Fries and Seagull/Interior 01/BRP Sample SceneGabinete.unity";
        private const string PrefabInstanceName = "GameScenePrefab";

        public void OnPreprocessBuild(BuildReport report) {
            var fullPath = Path.GetFullPath(BuildScenePath);
            if (!File.Exists(fullPath)) {
                throw new BuildFailedException($"Build validation failed: scene not found: '{BuildScenePath}' (fullPath='{fullPath}').");
            }

            // Scene YAML is text-based; we can validate without loading/switching editor scenes.
            // We search for the PrefabInstance modification that renames the instance to GameScenePrefab,
            // and then read its m_IsActive override value.
            var lines = File.ReadAllLines(fullPath);

            for (int i = 0; i < lines.Length - 3; i++) {
                if (!lines[i].Contains("propertyPath: m_Name")) continue;

                // Expect the next line to carry the value.
                var next = lines[i + 1];
                if (next.IndexOf($"value: {PrefabInstanceName}", StringComparison.Ordinal) < 0) continue;

                // Look ahead for m_IsActive within a small window.
                for (int j = i + 2; j < Math.Min(lines.Length - 1, i + 60); j++) {
                    if (!lines[j].Contains("propertyPath: m_IsActive")) continue;

                    var valueLine = lines[j + 1].Trim();
                    if (!valueLine.StartsWith("value:", StringComparison.Ordinal)) break;

                    var raw = valueLine.Substring("value:".Length).Trim();
                    if (raw == "1") return; // OK

                    if (raw == "0") {
                        throw new BuildFailedException(
                            $"Build validation failed: '{PrefabInstanceName}' is inactive (m_IsActive: 0) in '{BuildScenePath}'. " +
                            "Enable it in the scene (Hierarchy checkbox) so Api/HandTracker run.");
                    }

                    throw new BuildFailedException(
                        $"Build validation failed: unexpected m_IsActive value '{raw}' for '{PrefabInstanceName}' in '{BuildScenePath}'.");
                }

                // Found the name block but not the active flag close by.
                throw new BuildFailedException(
                    $"Build validation failed: couldn't find m_IsActive override for '{PrefabInstanceName}' in '{BuildScenePath}'.");
            }

            throw new BuildFailedException(
                $"Build validation failed: couldn't find a PrefabInstance named '{PrefabInstanceName}' in '{BuildScenePath}'.");
        }
    }
}
#endif
