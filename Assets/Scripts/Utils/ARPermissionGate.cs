using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class ARPermissionGate : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private PermissionsManager permissionsManager;
    [SerializeField] private ARSession arSession;
    [SerializeField] private ARCameraManager cameraManager;
    [SerializeField] private ARCameraBackground cameraBackground;

    [Header("Options")]
    [SerializeField] private bool requestOnAwake = true;
    [SerializeField, Min(0f)] private float permissionTimeoutSeconds = 15f;

    private bool arComponentsEnabled;

    private void Awake()
    {
        if (arSession == null || cameraManager == null)
        {
            Debug.LogError("[CameraTest] ARPermissionGate missing AR references. Assign ARSession and ARCameraManager.", this);
            enabled = false;
            return;
        }

        if (requestOnAwake)
        {
            PrepareArComponents(false);
            StartCoroutine(EnsureCameraPermissionCoroutine());
        }
    }

    public void RequestPermissionsManually()
    {
        StartCoroutine(EnsureCameraPermissionCoroutine());
    }

    private IEnumerator EnsureCameraPermissionCoroutine()
    {
#if UNITY_ANDROID
        if (cameraManager.permissionGranted)
        {
            EnableArComponents();
            yield break;
        }

        if (permissionsManager != null && permissionsManager.isActiveAndEnabled)
        {
            permissionsManager.ProcessPermissions();
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
        }

        var elapsed = 0f;
        while (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            if (permissionTimeoutSeconds > 0f && elapsed >= permissionTimeoutSeconds)
            {
                Debug.LogWarning("[CameraTest] Camera permission request timed out or was denied. AR session remains disabled.", this);
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Allow the subsystem a frame to refresh its state
        yield return null;
#endif

        EnableArComponents();

#if UNITY_ANDROID
        const int framesToWait = 5;
        var frames = 0;
        while (!cameraManager.permissionGranted && frames < framesToWait)
        {
            frames++;
            yield return null;
        }

        if (!cameraManager.permissionGranted)
        {
            Debug.Log("[CameraTest] Resetting ARSession to refresh camera permission state.", this);
            arSession.Reset();
        }
#endif
    }

    private void PrepareArComponents(bool enabledState)
    {
        arSession.enabled = enabledState;
        cameraManager.enabled = enabledState;

        if (cameraBackground != null)
        {
            cameraBackground.enabled = enabledState;
        }

        arComponentsEnabled = enabledState;
    }

    private void EnableArComponents()
    {
        if (arComponentsEnabled)
        {
            return;
        }

        PrepareArComponents(true);
        Debug.Log("[CameraTest] Camera permission granted; AR session enabled.", this);
    }
}