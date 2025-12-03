using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class StreamPassthrough : MonoBehaviour
{
    // [SerializeField] private Renderer targetRenderer;
    [SerializeField] private RawImage targetRawImage;
    [SerializeField] private int captureWidth = 1280;
    [SerializeField] private int captureHeight = 720;
    [SerializeField] private float startDelaySeconds = 1f;

    private Texture2D cameraTexture;
    // private Material runtimeMaterial;

#if UNITY_ANDROID && !UNITY_EDITOR
    private const string PluginClassName = "com.galaxyXR.passthrough.GalaxyXRPassThroughCapture";
    private AndroidJavaClass pluginClass;
    private AndroidJavaObject unityActivity;
    private bool isInitialized;
#endif

    private void Start()
    {
        // if (targetRenderer == null)
        if (targetRawImage == null)
        {
            Debug.LogError("[GalaxyXRPassThroughRenderer] Target renderer is not assigned.");
            enabled = false;
            return;
        }

        cameraTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        // runtimeMaterial = targetRenderer.material;
        // runtimeMaterial.mainTexture = cameraTexture;
        targetRawImage.texture = cameraTexture;

        StartCoroutine(InitializeAfterDelay());
    }

    private void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        pluginClass?.CallStatic("stop");
        pluginClass = null;
        unityActivity = null;
        isInitialized = false;
#endif
    }

    private IEnumerator InitializeAfterDelay()
    {
        yield return new WaitForSeconds(startDelaySeconds);

#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }

        pluginClass = new AndroidJavaClass(PluginClassName);
        pluginClass.CallStatic("start", unityActivity, captureWidth, captureHeight);
        isInitialized = true;
#endif
    }

    private void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!isInitialized || pluginClass == null)
        {
            return;
        }

        var jpegBytes = pluginClass.CallStatic<byte[]>("getLatestJpeg");
        if (jpegBytes == null || jpegBytes.Length == 0)
        {
            return;
        }

        if (cameraTexture.LoadImage(jpegBytes))
        {
            // runtimeMaterial.mainTexture = cameraTexture;
            targetRawImage.texture = cameraTexture;
        }
#endif
    }
}