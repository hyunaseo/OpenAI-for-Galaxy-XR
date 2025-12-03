using UnityEngine;
using UnityEngine.UI;

public class DrawCapturedImage : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RawImage rawImage;

    [Header("Source")]
    [SerializeField] private OpenAIQueryVision queryVision;

    private Texture2D cameraTexture;

    private void Awake()
    {
        if (rawImage == null)
        {
            Debug.LogError("[NoonChi] [DrawCapturedImage] RawImage reference is missing.");
            enabled = false;
            return;
        }

        if (queryVision == null)
        {
            queryVision = FindObjectOfType<OpenAIQueryVision>();
            if (queryVision == null)
            {
                Debug.LogError("[NoonChi] [DrawCapturedImage] OpenAIQueryVision reference is missing.");
                enabled = false;
                return;
            }
        }

        cameraTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        rawImage.texture = cameraTexture;
    }

    private void OnEnable()
    {
        if (queryVision != null)
        {
            queryVision.OnImageCapturedRaw += HandleImageCaptured;
        }   
    }

    private void OnDisable()
    {
        if (queryVision != null)
        {
            queryVision.OnImageCapturedRaw -= HandleImageCaptured;
        }
    }

    private void OnDestroy()
    {
        if (cameraTexture != null)
        {
            Destroy(cameraTexture);
            cameraTexture = null;
        }
    }

    private void HandleImageCaptured(byte[] imageBytes, string mimeType)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            Debug.LogWarning("[NoonChi] [DrawCapturedImage] Received empty image bytes.");
            return;
        }

        if (cameraTexture == null)
        {
            cameraTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        }

        if (cameraTexture.LoadImage(imageBytes))
        {
            rawImage.texture = cameraTexture;
            rawImage.SetNativeSize();
        }
        else
        {
            Debug.LogWarning("[NoonChi] [DrawCapturedImage] Failed to load image bytes into texture.");
        }
    }

}
