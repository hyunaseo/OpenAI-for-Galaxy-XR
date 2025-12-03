using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class LogOpenAIChat : MonoBehaviour
{
    [SerializeField] private string serverUrl = "http://192.168.0.240:8080/log";
    private OpenAIQueryVision queryVision;
    private string sessionId; 
    private string latestUserText = "";
    private string latestAgentText = "";

    private byte[] pendingImageBytes;
    private string pendingImageMimeType;
    private bool hasPendingImage;

    private void Awake()
    {
        sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

    #if UNITY_2023_1_OR_NEWER
        queryVision = FindFirstObjectByType<OpenAIQueryVision>();
    #else
        queryVision = FindObjectOfType<OpenAIQueryVision>();
    #endif
    }

    private void OnEnable()
    {
        if (queryVision != null)
        {
            queryVision.OnUserUtteranceCompleted += HandleUserUtteranceCompleted;
            queryVision.OnAgentResponseCompleted += HandleAgentResponseCompleted;
            queryVision.OnImageCapturedRaw += HandleImageCaptured;
        }
    }

    private void OnDisable()
    {
        if (queryVision != null)
        {
            queryVision.OnUserUtteranceCompleted -= HandleUserUtteranceCompleted;
            queryVision.OnAgentResponseCompleted -= HandleAgentResponseCompleted;
            queryVision.OnImageCapturedRaw -= HandleImageCaptured;
        }
    }

    private void HandleUserUtteranceCompleted(string text)
    {
        latestUserText = text ?? "";
        Debug.Log($"[NoonChi] [LogOpenAIChat] User utterance completed: {latestUserText}");
    }

    private void HandleAgentResponseCompleted(string text)
    {
        latestAgentText = text ?? "";
        Debug.Log($"[NoonChi] [LogOpenAIChat] Agent response completed: {latestAgentText}");

        try
        {
            // ✅ 이미지가 있으면: 이미지 + 메타 업로드
            if (hasPendingImage && pendingImageBytes != null && pendingImageBytes.Length > 0)
            {
                var mime = string.IsNullOrWhiteSpace(pendingImageMimeType)
                    ? "application/octet-stream"
                    : pendingImageMimeType;

                BeginUpload(pendingImageBytes, mime);
            }
            else
            {
                // ✅ 이미지가 없으면: 텍스트만 업로드
                Debug.Log("[NoonChi] [LogOpenAIChat] No pending image for this turn; uploading text-only log.");
                BeginUploadTextOnly();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NoonChi] [LogOpenAIChat] Failed to initiate upload: {e}");
        }
        finally
        {
            // 어떤 경우든 이 턴의 pending 상태는 정리
            pendingImageBytes = null;
            pendingImageMimeType = null;
            hasPendingImage = false;
        }
    }

    private void HandleImageCaptured(byte[] imageBytes, string mimeType)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            Debug.LogWarning("[NoonChi] [LogOpenAIChat] Skipping upload because image bytes are empty.");
            return;
        }

        pendingImageBytes = imageBytes;
        pendingImageMimeType = mimeType;
        hasPendingImage = true;

        Debug.Log("[NoonChi] [LogOpenAIChat] Image captured and stored for upload after agent response.");
    }

    private void BeginUpload(byte[] bytes, string mimeType)
    {
        Debug.Log("[NoonChi] [LogOpenAIChat] Uploading image...");

        var meta = new LogMeta
        {
            sessionId = sessionId,
            userText = latestUserText ?? "",
            agentText = latestAgentText ?? "",
            timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            mimeType = mimeType
        };

        string metaJson = JsonUtility.ToJson(meta);
        Debug.Log("[NoonChi] [LogOpenAIChat] metaJson = " + metaJson);

        var form = new WWWForm();
        form.AddBinaryData("file", bytes, "frame.jpg", mimeType);
        form.AddField("meta", metaJson);

        var request = UnityWebRequest.Post(serverUrl, form);
        request.timeout = 10;
        request.disposeDownloadHandlerOnDispose = true;
        request.disposeUploadHandlerOnDispose = true;

        var ownerRef = new WeakReference<LogOpenAIChat>(this);
        var asyncOperation = request.SendWebRequest();
        asyncOperation.completed += _ => OnUploadCompleted(request, ownerRef);
    }

    private void BeginUploadTextOnly()
    {
        var meta = new LogMeta
        {
            sessionId = sessionId,
            userText = latestUserText ?? "",
            agentText = latestAgentText ?? "",
            timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            mimeType = ""   // 이미지가 없으니 빈 문자열
        };

        string metaJson = JsonUtility.ToJson(meta);
        Debug.Log("[NoonChi] [LogOpenAIChat] (TextOnly) metaJson = " + metaJson);

        var form = new WWWForm();
        // 🔹 파일은 안 보내고 meta만 보냄
        form.AddField("meta", metaJson);

        var request = UnityWebRequest.Post(serverUrl, form);
        request.timeout = 10;
        request.disposeDownloadHandlerOnDispose = true;
        request.disposeUploadHandlerOnDispose = true;

        var ownerRef = new WeakReference<LogOpenAIChat>(this);
        var asyncOperation = request.SendWebRequest();
        asyncOperation.completed += _ => OnUploadCompleted(request, ownerRef);
    }

    private static void OnUploadCompleted(UnityWebRequest request, WeakReference<LogOpenAIChat> ownerRef)
    {
        try
        {
            if (!ownerRef.TryGetTarget(out var owner) || owner == null)
            {
                Debug.LogWarning($"[NoonChi] [LogOpenAIChat] Upload finished after component was destroyed. result={request.result}, code={request.responseCode}, error={request.error}");
                return;
            }

            owner.LogUploadOutcome(request);
        }
        finally
        {
            request.Dispose();
        }
    }

    private void LogUploadOutcome(UnityWebRequest request)
    {
        Debug.Log($"[NoonChi] [LogOpenAIChat] result={request.result}, code={request.responseCode}, error={request.error}");

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[NoonChi] [LogOpenAIChat] Upload failed: " + request.error);
        }
        else
        {
            var responseText = request.downloadHandler?.text;
            Debug.Log("[NoonChi] [LogOpenAIChat] Upload successful: " + responseText);
        }
    }

    [Serializable] private class LogMeta
    {
        public string sessionId;
        public string userText;
        public string agentText;
        public string timestamp;
        public string mimeType;
    }
}
