using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

namespace OpenSee
{
    [RequireComponent(typeof(OpenSee))]
    public class OpenSeeVRMSync : MonoBehaviour
    {
        [Header("VRM Settings")]
        [Tooltip("Reference to the VRM10 instance to control")]
        public Vrm10Instance vrmInstance;

        [Header("Face ID")]
        [Tooltip("The ID of the face to track. Usually 0 for single-face tracking")]
        public int faceId = 0;

        [Header("Tracking Thresholds")]
        [Tooltip("Minimum confidence threshold for tracking")]
        [Range(0f, 1f)]
        public float minConfidence = 0.2f;

        [Tooltip("Maximum 3D fit error allowed")]
        public float maxFit3DError = 100f;

        [Header("Blendshape Mappings")]
        [Range(0f, 1f)]
        public float eyeOpenThreshold = 0.2f;

        [Range(0f, 1f)]
        public float mouthOpenMultiplier = 1f;
        public float mouthWideMultiplier = 1f;

        [Header("Head Tracking")]
        public bool enableHeadTracking = true;

        [Range(0f, 1f)]
        public float rotationSmoothing = 0.3f;

        [Range(0f, 5f)]
        public float rotationMultiplier = 1f;

        [Header("Advanced")]
        [Tooltip("Apply inverse rotation for head tracking")]
        public bool applyInverseRotation = false;

        [Tooltip("Initial head offset (if needed)")]
        public Vector3 headRotationOffset = Vector3.zero;

        [Tooltip("Rotation conversion mode")]
        public RotationConversionMode rotationMode = RotationConversionMode.Simple;

        public enum RotationConversionMode
        {
            Simple,
            OpenCV,
            Raw
        }

        // Reference to the OpenSee component
        private OpenSee openSee;

        // Last processed tracking data timestamp
        private double lastProcessedTime = 0.0;

        // Cached blendshape indices for performance
        private ExpressionKey? eyeBlinkLeftKey;
        private ExpressionKey? eyeBlinkRightKey;
        private ExpressionKey? mouthOpenKey;
        private ExpressionKey? mouthSmileKey;

        // Smoothing
        private Quaternion targetRotation = Quaternion.identity;
        private Quaternion currentRotation = Quaternion.identity;

        // Store initial head rotation for reference
        private Quaternion _initialHeadRotation = Quaternion.identity;
        private bool _headInitRotationSet = false;
        private bool _modelInitialized = false;

        private void Start()
        {
            // Get the OpenSee component
            openSee = GetComponent<OpenSee>();

            // Initialize VRM references
            if (vrmInstance == null)
            {
                Debug.LogError("VRM Instance is not assigned!");
                //enabled = false;
                //return;
            }

            // Set up blendshape keys
            SetupBlendshapeKeys();

            // Initialize the model pose
            StartCoroutine(InitializeModelPose());
        }

        private IEnumerator InitializeModelPose()
        {
            // Wait one frame for the VRM model to initialize
            yield return null;

            // Make sure humanoid reference is available
            if (vrmInstance.Humanoid != null)
            {
                // Store the initial head rotation if head is available
                if (vrmInstance.Humanoid.Head != null)
                {
                    _initialHeadRotation = vrmInstance.Humanoid.Head.localRotation;
                    _headInitRotationSet = true;
                    Debug.Log("Head rotation initialized: " + _initialHeadRotation.eulerAngles);
                }
                else
                {
                    Debug.LogWarning("VRM Humanoid Head reference is null. Head tracking will not work.");
                }

                // For VRM10, use the Expression system to reset expressions instead of ResetToInitialPose
                ResetAllExpressions();

                _modelInitialized = true;
                Debug.Log("VRM model initialized");
            }
            else
            {
                Debug.LogError("VRM Humanoid reference is null. Model may remain in T-pose.");
            }
        }

        private void ResetAllExpressions()
        {
            if (vrmInstance != null && vrmInstance.Runtime != null && vrmInstance.Runtime.Expression != null)
            {
                // Reset all expressions to zero
                foreach (ExpressionPreset preset in System.Enum.GetValues(typeof(ExpressionPreset)))
                {
                    ExpressionKey key = ExpressionKey.CreateFromPreset(preset);
                    vrmInstance.Runtime.Expression.SetWeight(key, 0);
                }
            }
        }

        private void SetupBlendshapeKeys()
        {
            // Initialize blendshape keys for VRM standard expressions
            eyeBlinkLeftKey = ExpressionKey.CreateFromPreset(ExpressionPreset.blinkLeft);
            eyeBlinkRightKey = ExpressionKey.CreateFromPreset(ExpressionPreset.blinkRight);
            mouthOpenKey = ExpressionKey.CreateFromPreset(ExpressionPreset.aa);  // "aa" is the mouth open expression
            mouthSmileKey = ExpressionKey.CreateFromPreset(ExpressionPreset.happy); // Mouth wide - using "fun" expression
        }

        private void Update()
        {
            if (!openSee || vrmInstance == null)
                return;

            OpenSee.OpenSeeData trackingData = openSee.GetOpenSeeData(faceId);

            // Check if we have valid tracking data
            if (trackingData == null)
                return;

            // Check if we have a new frame of data
            if (trackingData.time <= lastProcessedTime)
                return;

            // Check if 3D tracking is good enough
            if (trackingData.fit3DError > maxFit3DError)
                return;

            // Update our timestamp
            lastProcessedTime = trackingData.time;

            // Process VRM updates
            UpdateEyeBlink(trackingData);
            UpdateMouth(trackingData);

            if (enableHeadTracking && _headInitRotationSet)
                UpdateHeadRotation(trackingData);
        }

        private void UpdateEyeBlink(OpenSee.OpenSeeData trackingData)
        {
            if (eyeBlinkLeftKey.HasValue && eyeBlinkRightKey.HasValue &&
                vrmInstance.Runtime != null && vrmInstance.Runtime.Expression != null)
            {
                // Map the eye open values to blendshape values (inverted since blink is the opposite of open)
                float leftEyeBlink = 1f - Mathf.Clamp01(trackingData.leftEyeOpen / eyeOpenThreshold);
                float rightEyeBlink = 1f - Mathf.Clamp01(trackingData.rightEyeOpen / eyeOpenThreshold);

                // Apply to VRM blendshapes
                vrmInstance.Runtime.Expression.SetWeight(eyeBlinkLeftKey.Value, leftEyeBlink);
                vrmInstance.Runtime.Expression.SetWeight(eyeBlinkRightKey.Value, rightEyeBlink);
            }
        }

        private void UpdateMouth(OpenSee.OpenSeeData trackingData)
        {
            if (mouthOpenKey.HasValue && mouthSmileKey.HasValue &&
                vrmInstance.Runtime != null && vrmInstance.Runtime.Expression != null)
            {
                // Get mouth features from OpenSee
                float mouthOpen = Mathf.Clamp01(trackingData.features.MouthOpen * mouthOpenMultiplier);
                float mouthWide = Mathf.Clamp01(trackingData.features.MouthWide * mouthWideMultiplier);

                // Apply to VRM blendshapes
                vrmInstance.Runtime.Expression.SetWeight(mouthOpenKey.Value, mouthOpen);
                vrmInstance.Runtime.Expression.SetWeight(mouthSmileKey.Value, mouthWide);
            }
        }

        private void UpdateHeadRotation(OpenSee.OpenSeeData trackingData)
        {
            if (trackingData.got3DPoints && vrmInstance.Humanoid != null && vrmInstance.Humanoid.Head != null)
            {
                Vector3 eulerAngles = Vector3.zero;

                // Convert OpenSee rotation based on selected mode
                switch (rotationMode)
                {
                    case RotationConversionMode.Simple:
                        // Simple conversion - works for most cases
                        eulerAngles = new Vector3(
                            -trackingData.rotation.x * rotationMultiplier,
                            trackingData.rotation.y * rotationMultiplier,
                            -trackingData.rotation.z * rotationMultiplier
                        );
                        break;

                    case RotationConversionMode.OpenCV:
                        // Use OpenCV rotation directly
                        eulerAngles = new Vector3(
                            trackingData.rawEuler.x * rotationMultiplier,
                            -trackingData.rawEuler.y * rotationMultiplier,
                            trackingData.rawEuler.z * rotationMultiplier
                        );
                        break;

                    case RotationConversionMode.Raw:
                        // Use raw quaternion
                        Quaternion rawQuat = new Quaternion(
                            -trackingData.rawQuaternion.x,
                            trackingData.rawQuaternion.y,
                            -trackingData.rawQuaternion.z,
                            trackingData.rawQuaternion.w
                        );

                        targetRotation = rawQuat;
                        currentRotation = Quaternion.Slerp(currentRotation, targetRotation, 1f - rotationSmoothing);
                        vrmInstance.Humanoid.Head.localRotation = _initialHeadRotation * currentRotation;
                        return; // Skip the rest of the method
                }

                // Add user-defined offset
                eulerAngles += headRotationOffset;

                // Set target rotation
                targetRotation = Quaternion.Euler(eulerAngles);

                if (applyInverseRotation)
                {
                    // Inverse rotation approach - may work better for some models
                    targetRotation = Quaternion.Inverse(targetRotation);
                }

                // Smooth rotation
                currentRotation = Quaternion.Slerp(currentRotation, targetRotation, 1f - rotationSmoothing);

                // Apply to VRM head bone with initial offset
                vrmInstance.Humanoid.Head.localRotation = _initialHeadRotation * currentRotation;
            }
        }

        // Debug helper method to visualize what's happening with the rotation
        private void OnGUI()
        {
            if (Debug.isDebugBuild)
            {
                GUILayout.BeginArea(new Rect(10, 10, 350, 150));
                GUILayout.Label("OpenSee VRM Debug:");

                if (vrmInstance != null && vrmInstance.Humanoid != null && vrmInstance.Humanoid.Head != null)
                {
                    GUILayout.Label($"Current Head Rotation: {vrmInstance.Humanoid.Head.localRotation.eulerAngles}");
                    GUILayout.Label($"Initial Head Rotation: {_initialHeadRotation.eulerAngles}");
                    GUILayout.Label($"Target Rotation: {targetRotation.eulerAngles}");
                    GUILayout.Label($"Head Initialized: {_headInitRotationSet}");
                    GUILayout.Label($"Rotation Mode: {rotationMode}");
                }
                else
                {
                    GUILayout.Label("VRM head reference not available.");
                }

                GUILayout.EndArea();
            }
        }
    }
}