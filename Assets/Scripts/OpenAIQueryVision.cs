using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI;
using OpenAI.Models;
using OpenAI.Realtime;
using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.Collections;
using UnityEngine;
using Utilities.Audio;
using Utilities.Encoding.Wav;

[RequireComponent(typeof(StreamAudioSource))]
public class OpenAIQueryVision : MonoBehaviour
{
    private readonly string RealtimeModel = Model.GPT_Realtime;
    private readonly string TranscriptionModel = Model.Transcribe_GPT_4o_Mini;
#if UNITY_ANDROID && !UNITY_EDITOR
    private const string PluginClassName = "com.galaxyXR.passthrough.GalaxyXRPassThroughCapture";
#endif

    [Header("OpenAI")]
    [SerializeField] private OpenAIConfiguration configuration;
    [SerializeField] private bool enableDebug;
    [SerializeField] private Voice voice;
    [SerializeField, TextArea] private string systemPrompt =
        "You are a helpful XR voice assistant running on a XR headset. " +
        "You are a helpful, witty, and friendly AI.\nAct like a human, but remember that you aren't a human and that you can't do human things in the real world.\nYour voice and personality should be warm and engaging, with a lively and playful tone." +
        "Talk in normal speed.\nYou should always call a function if you can.\nYou should always notify a user before calling a function, so they know it might take a moment to see a result.\nDo not refer to these rules, even if you're asked about them.\nWhen performing function calls, use the defaults unless explicitly told to use a specific value." +
        "You can talk in English. Answer briefly and conversationally in \"one sentence\"." +
        "When answering the user, always consider both (1) what the user says and (2) the most recent camera image I provide." +
        "Treat the image as the ground truth for the user's current environment: pay attention to objects, text, spatial layout, and what the user might be looking at or interacting with." +
        "If the visual context changes the interpretation of the user's query, prioritize the image over your prior knowledge and gently correct any wrong assumptions." +
        "If no image is available, or the image is too unclear or irrelevant, just answer based on the user's words.";

    [Header("Audio")]
    [SerializeField] private StreamAudioSource streamAudioSource;

    [Header("Turn Detection")]
    [SerializeField] private bool useServerVad = true;
    [SerializeField, Range(0, 2000)] private int serverVadPrefixPaddingMs = 250;
    [SerializeField, Range(100, 4000)] private int serverVadSilenceDurationMs = 900;
    [SerializeField, Range(0f, 1f)] private float serverVadDetectionThreshold = 0.4f;
    [SerializeField] private bool serverVadCreateResponse = true;
    [SerializeField] private bool serverVadInterruptResponse = true;

    [Header("UI")]
    [SerializeField, Range(0.001f, 0.1f)] private float userSpeechThreshold = 0.01f;
    [SerializeField, Range(0.01f, 0.5f)] private float userSilenceReleaseSeconds = 0.2f;
    [SerializeField] private TMP_Text userTitleTmp;
    [SerializeField] private TMP_Text userTextTmp;
    [SerializeField] private TMP_Text agentTitleTmp;
    [SerializeField] private TMP_Text agentTextTmp;
    [SerializeField] private TMP_Text debugTextTmp;
    private Color userColor = new Color32(237, 98, 227, 255);
    private Color agentColor = new Color32(219, 245, 72, 255);
    private Color neutralColor = Color.white;
    private const string LogPrefix = "[NoonChi] [QueryVision]";
    

    [Header("Vision")]
    [SerializeField] private bool includeImageDataUrlPrefix = true;
    [SerializeField] private Texture2D sampleImageTexture;
    [SerializeField] private bool useSampleImageOnly;
    [SerializeField] private bool logCameraFrameWarnings = true;

    public event Action<string> OnUserUtteranceCompleted;
    public event Action<string> OnAgentResponseCompleted;
    public event Action<string> OnImageCaptured;
    public event Action<byte[], string> OnImageCapturedRaw;

    private OpenAIClient openAI;
    private RealtimeSession session;
    private bool isAudioResponseInProgress;
    private float playbackTimeRemaining;
    private bool agentTextInProgress;
    private bool agentAudioActive;
    private bool agentTextActive;
    private volatile bool userSpeechActive;
    private volatile float userSilenceTimer;
    private volatile bool imageCaptureQueued;
    private volatile bool imageCapturedForCurrentUtterance;
#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaClass cameraPluginClass;
#endif
    private bool loggedPluginUnavailable;
    private bool loggedNoFrame;
    private readonly CancellationTokenSource lifetimeCts = new();
    private CancellationToken DestroyToken => lifetimeCts.Token;
    private bool CanRecord => !isAudioResponseInProgress && playbackTimeRemaining <= 0f;

    private void OnValidate()
    {
        if (streamAudioSource == null)
        {
            streamAudioSource = GetComponent<StreamAudioSource>();
        }
    }

    private async void Awake()
    {
        OnValidate();
        openAI = new OpenAIClient(configuration) { EnableDebug = enableDebug };
        RecordingManager.EnableDebug = enableDebug;
        InitializeUiColors();
        if (enableDebug)
        {
            var message = $"{LogPrefix} Awake: initializing OpenAI realtime session.";
            Debug.Log(message);
            WriteDebugText(message);
        }

        try
        {
            var sessionConfig = BuildSessionConfiguration();
            session = await openAI.RealtimeEndpoint.CreateSessionAsync(sessionConfig, DestroyToken);
            
            RecordInputAudio(DestroyToken);
            _ = session.ReceiveUpdatesAsync<IServerEvent>(ServerResponseEvent, DestroyToken);
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
        {
            var message = $"{LogPrefix} Awake cancelled: {e.Message}";
            Debug.LogWarning(message);
            WriteDebugText(message);
        }
        catch (Exception e)
        {
            var message = $"{LogPrefix} Awake encountered exception: {e}";
            Debug.LogError(message);
            WriteDebugText(message);
        }
    }


    private void OnDestroy()
    {
        lifetimeCts.Cancel();
        session?.Dispose();
    }

    private void Update()
    {
        if (playbackTimeRemaining > 0f)
        {
            playbackTimeRemaining = Mathf.Max(0f, playbackTimeRemaining - Time.deltaTime);
        }

        if (!CanRecord)
        {
            userSpeechActive = false;
            userSilenceTimer = 0f;
        }
        else if (userSpeechActive)
        {
            userSilenceTimer -= Time.deltaTime;
            if (userSilenceTimer <= 0f)
            {
                userSpeechActive = false;
            }
        }

        if (imageCaptureQueued)
        {
            imageCaptureQueued = false;
            _ = CaptureAndSendImageAsync();
        }

        if (!userSpeechActive && userSilenceTimer <= 0f)
        {
            imageCapturedForCurrentUtterance = false;
        }

        if (userTitleTmp != null)
        {
            userTitleTmp.color = userSpeechActive ? userColor : neutralColor;
        }

        var agentSpeaking = agentAudioActive || agentTextActive || isAudioResponseInProgress || playbackTimeRemaining > 0f;
        if (agentTitleTmp != null)
        {
            agentTitleTmp.color = agentSpeaking ? agentColor : neutralColor;
        }
    }

    private SessionConfiguration BuildSessionConfiguration()
    {
        IVoiceActivityDetectionSettings turnDetectionSettings;
        if (useServerVad)
        {
            int? prefixPadding = serverVadPrefixPaddingMs > 0 ? serverVadPrefixPaddingMs : null;
            int? silenceDuration = serverVadSilenceDurationMs > 0 ? serverVadSilenceDurationMs : null;
            float? detectionThreshold = serverVadDetectionThreshold > 0f ? Mathf.Clamp01(serverVadDetectionThreshold) : null;

            turnDetectionSettings = new ServerVAD(
                createResponse: serverVadCreateResponse,
                interruptResponse: serverVadInterruptResponse,
                prefixPadding: prefixPadding,
                silenceDuration: silenceDuration,
                detectionThreshold: detectionThreshold);
        }
        else
        {
            turnDetectionSettings = new DisabledVAD();
        }

        return new SessionConfiguration(
            model: RealtimeModel,
            voice: voice,
            inputAudioTranscriptionSettings: new InputAudioTranscriptionSettings(TranscriptionModel),
            instructions: systemPrompt,
            turnDetectionSettings: turnDetectionSettings);
    }

    private void InitializeUiColors()
    {
        if (userTitleTmp != null)
        {
            userTitleTmp.color = neutralColor;
        }

        if (agentTitleTmp != null)
        {
            agentTitleTmp.color = neutralColor;
        }
    }

    private async void RecordInputAudio(CancellationToken cancellationToken)
    {
        var memoryStream = new MemoryStream();
        var semaphore = new SemaphoreSlim(1, 1);

        try
        {
            if (enableDebug)
            {
                var message = $"{LogPrefix} Input audio recording started.";
                Debug.Log(message);
                WriteDebugText(message);
            }
            RecordingManager.StartRecordingStream<WavEncoder>(BufferCallback, 24000, cancellationToken);

            async Task BufferCallback(NativeArray<byte> audioChunk)
            {
                if (!CanRecord)
                {
                    userSpeechActive = false;
                    userSilenceTimer = 0f;
                    return;
                }

                DetectUserSpeech(audioChunk);

                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var length = audioChunk.Length;
                    for (var i = 0; i < length; i++)
                    {
                        memoryStream.WriteByte(audioChunk[i]);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
                try
                {
                    int bytesRead;

                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        memoryStream.Position = 0;
                        bytesRead = await memoryStream.ReadAsync(
                            buffer,
                            0,
                            (int)Math.Min(buffer.Length, memoryStream.Length),
                            cancellationToken).ConfigureAwait(false);
                        memoryStream.SetLength(0);
                    }
                    finally
                    {
                        semaphore.Release();
                    }

                    if (bytesRead > 0)
                    {
                        await session.SendAsync(
                            new InputAudioBufferAppendRequest(buffer.AsMemory(0, bytesRead)),
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Yield();
                    }
                }
                catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    var message = $"{LogPrefix} Unexpected error while appending audio buffer: {e}";
                    Debug.LogError(message);
                    WriteDebugText(message);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            var message = $"{LogPrefix} Input audio recording failed: {e}";
            Debug.LogError(message);
            WriteDebugText(message);
        }
        finally
        {
            if (enableDebug)
            {
                var message = $"{LogPrefix} Input audio recording terminated.";
                Debug.Log(message);
                WriteDebugText(message);
            }
            semaphore.Dispose();
            memoryStream.Dispose();
        }
    }

    private void DetectUserSpeech(NativeArray<byte> audioBuffer)
    {
        var threshold = Mathf.Max(0.0001f, userSpeechThreshold);
        if (audioBuffer.Length < 2)
        {
            return;
        }

        const float normalizer = 1f / short.MaxValue;
        float maxAmplitude = 0f;
        var upperBound = audioBuffer.Length - 1;
        var wasSpeaking = userSpeechActive;

        for (var i = 0; i < upperBound; i += 2)
        {
            var sample = (short)(audioBuffer[i] | (audioBuffer[i + 1] << 8));
            var amplitude = Mathf.Abs(sample) * normalizer;
            if (amplitude > maxAmplitude)
            {
                maxAmplitude = amplitude;
                if (maxAmplitude >= threshold)
                {
                    break;
                }
            }
        }

        if (maxAmplitude < threshold)
        {
            return;
        }

        if (!wasSpeaking && !imageCapturedForCurrentUtterance)
        {
            if (enableDebug)
            {
                var message = $"{LogPrefix} Speech detected; amplitude {maxAmplitude:F3} exceeded threshold {threshold:F3}. Queuing image capture.";
                Debug.Log(message);
                WriteDebugText(message);
            }
            imageCaptureQueued = true;
        }

        userSpeechActive = true;
        userSilenceTimer = userSilenceReleaseSeconds;
    }

    private void ServerResponseEvent(IServerEvent serverEvent)
    {
        switch (serverEvent)
        {
            case ResponseAudioResponse audioResponse:
                HandleAudioResponse(audioResponse);
                break;
            case ResponseAudioTranscriptResponse transcriptResponse:
                HandleTranscriptResponse(transcriptResponse);
                break;
            case ConversationItemInputAudioTranscriptionResponse transcriptionResponse:
                HandleUserTranscription(transcriptionResponse);
                break;
            case ConversationItemCreatedResponse conversationItemCreated:
                if (enableDebug)
                {
                    var message = $"{LogPrefix} Conversation item created: {conversationItemCreated.Item.Role}";
                    Debug.Log(message);
                    WriteDebugText(message);
                }
                break;
            case ResponseFunctionCallArgumentsResponse:
                if (enableDebug)
                {
                    const string message = "[HyunA] [QueryVision] Function call arguments received";
                    Debug.Log(message);
                    WriteDebugText(message);
                }
                break;
        }
    }

    private void HandleAudioResponse(ResponseAudioResponse audioResponse)
    {
        if (audioResponse.IsDelta)
        {
            agentAudioActive = true;
            userSpeechActive = false;
            userSilenceTimer = 0f;
            isAudioResponseInProgress = true;
            streamAudioSource.SampleCallback(audioResponse.AudioSamples);
            playbackTimeRemaining += audioResponse.Length;
        }
        else if (audioResponse.IsDone)
        {
            playbackTimeRemaining += 0.25f;
            isAudioResponseInProgress = false;
            agentAudioActive = false;
        }
    }

    private void HandleTranscriptResponse(ResponseAudioTranscriptResponse transcriptResponse)
    {
        if (transcriptResponse.IsDelta)
        {
            agentTextActive = true;
            if (agentTextTmp != null)
            {
                if (!agentTextInProgress)
                {
                    agentTextTmp.text = string.Empty;
                    agentTextInProgress = true;
                }

                agentTextTmp.text += transcriptResponse.Delta;
            }
        }

        if (!transcriptResponse.IsDone)
        {
            return;
        }

        agentTextInProgress = false;
        agentTextActive = false;

        if (agentTextTmp != null && !string.IsNullOrEmpty(agentTextTmp.text))
        {
            OnAgentResponseCompleted?.Invoke(agentTextTmp.text);
        }
    }

    private void HandleUserTranscription(ConversationItemInputAudioTranscriptionResponse transcriptionResponse)
    {
        userSpeechActive = false;
        userSilenceTimer = 0f;

        if (userTextTmp != null)
        {
            userTextTmp.text = transcriptionResponse.Transcript;
        }

        if (!string.IsNullOrEmpty(transcriptionResponse.Transcript))
        {
            OnUserUtteranceCompleted?.Invoke(transcriptionResponse.Transcript);
        }
    }

    private sealed class ImageConversationItemCreateRequest : IClientEvent
    {
        private readonly string payload;

        public ImageConversationItemCreateRequest(string imageUrl, string previousItemId = null)
        {
            payload = BuildPayload(imageUrl, previousItemId);
        }

        public string EventId => null;
        public string Type => "conversation.item.create";
        public string ToJsonString() => payload;

        private static string BuildPayload(string imageUrl, string previousItemId)
        {
            var item = new JObject
            {
                ["type"] = "message",
                ["role"] = "user",
                ["content"] = new JArray(new JObject
                {
                    ["type"] = "input_image",
                    ["image_url"] = imageUrl
                })
            };

            var root = new JObject
            {
                ["type"] = "conversation.item.create",
                ["item"] = item
            };

            if (!string.IsNullOrEmpty(previousItemId))
            {
                root["previous_item_id"] = previousItemId;
            }

            return root.ToString(Formatting.None);
        }
    }

    private async Task CaptureAndSendImageAsync()
    {
        if (session == null || DestroyToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (!TryAcquireImage(out var imageData, out var mimeType))
            {
                return;
            }

            if (imageData == null || imageData.Length == 0)
            {
                throw new InvalidOperationException("Image data is empty after capture.");
            }

            OnImageCapturedRaw?.Invoke(imageData, mimeType);

            var imageUrl = BuildImageUrl(imageData, mimeType);
            OnImageCaptured?.Invoke(imageUrl);

            var request = new ImageConversationItemCreateRequest(imageUrl);

            await session.SendAsync(request, DestroyToken).ConfigureAwait(true);

            imageCapturedForCurrentUtterance = true;
            if (enableDebug)
            {
                var message = useSampleImageOnly
                    ? $"{LogPrefix} Sample image attached to query."
                    : $"{LogPrefix} Captured camera frame via GalaxyXRPassThroughCapture and attached to query.";
                Debug.Log(message);
                WriteDebugText(message);
            }
        }
        catch (Exception ex)
        {
            var message = $"{LogPrefix} Failed to capture or send scene image: {ex.Message}";
            Debug.LogError(message);
            WriteDebugText(message);
            imageCapturedForCurrentUtterance = false;
        }
    }

    private bool TryAcquireImage(out byte[] imageData, out string mimeType)
    {
        if (useSampleImageOnly)
        {
            return TryEncodeSample(out imageData, out mimeType);
        }

        return TryGetLatestCameraImage(out imageData, out mimeType);
    }

    private bool TryEncodeSample(out byte[] imageData, out string mimeType)
    {
        imageData = null;
        mimeType = null;

        if (sampleImageTexture == null)
        {
            if (enableDebug)
            {
                var message = $"{LogPrefix} Sample image texture is not assigned; cannot send image.";
                Debug.LogWarning(message);
                WriteDebugText(message);
            }
            return false;
        }

        if (!TryEncodeTextureToPng(sampleImageTexture, out imageData))
        {
            if (enableDebug)
            {
                var message = $"{LogPrefix} Failed to encode sample texture to PNG.";
                Debug.LogError(message);
                WriteDebugText(message);
            }
            return false;
        }

        mimeType = "image/png";
        return true;
    }

    private bool TryGetLatestCameraImage(out byte[] imageData, out string mimeType)
    {
        imageData = null;
        mimeType = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (cameraPluginClass == null)
            {
                try
                {
                    cameraPluginClass = new AndroidJavaClass(PluginClassName);
                    loggedPluginUnavailable = false;
                }
                catch (Exception ex)
                {
                    if (logCameraFrameWarnings && !loggedPluginUnavailable)
                    {
                        loggedPluginUnavailable = true;
                        if (enableDebug)
                        {
                            var message = $"{LogPrefix} Unable to load GalaxyXRPassThroughCapture plugin: {ex.Message}";
                            Debug.LogError(message);
                            WriteDebugText(message);
                        }
                    }

                    imageCapturedForCurrentUtterance = false;
                    return false;
                }
            }

            var jpegBytes = cameraPluginClass.CallStatic<byte[]>("getLatestJpeg");
            if (jpegBytes == null || jpegBytes.Length == 0)
            {
                if (logCameraFrameWarnings && !loggedNoFrame)
                {
                    loggedNoFrame = true;
                    if (enableDebug)
                    {
                        const string message = "[HyunA] [QueryVision] GalaxyXRPassThroughCapture has not produced a frame yet.";
                        Debug.LogWarning(message);
                        WriteDebugText(message);
                    }
                }

                imageCapturedForCurrentUtterance = false;
                return false;
            }

            loggedNoFrame = false;
            imageData = jpegBytes;
            mimeType = "image/jpeg";
            return true;
        }
        catch (Exception ex)
        {
            var message = $"{LogPrefix} Failed to fetch camera frame from GalaxyXRPassThroughCapture: {ex.Message}";
            Debug.LogError(message);
            WriteDebugText(message);
            imageCapturedForCurrentUtterance = false;
            return false;
        }
#else
        if (logCameraFrameWarnings && !loggedPluginUnavailable)
        {
            loggedPluginUnavailable = true;
            if (enableDebug)
            {
                const string message = "[HyunA] [QueryVision] GalaxyXRPassThroughCapture capture is only supported on Android devices; provide a sample image instead.";
                Debug.LogWarning(message);
                WriteDebugText(message);
            }
        }

        imageCapturedForCurrentUtterance = false;
        return false;
#endif
    }

    private string BuildImageUrl(byte[] imageData, string mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
        {
            mimeType = "image/png";
        }

        var base64 = Convert.ToBase64String(imageData);
        var imageUrl = includeImageDataUrlPrefix ? $"data:{mimeType};base64,{base64}" : base64;

        if (!imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
            !imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            imageUrl = $"data:{mimeType};base64,{imageUrl}";
        }

        return imageUrl;
    }

    private void WriteDebugText(string message)
    {
        if (debugTextTmp != null)
        {
            debugTextTmp.text = message;
        }
    }

    private bool TryEncodeTextureToPng(Texture texture, out byte[] pngData)
    {
        pngData = null;

        if (texture == null)
        {
            return false;
        }

        if (texture is Texture2D tex2D)
        {
            try
            {
                if (tex2D.isReadable)
                {
                    pngData = tex2D.EncodeToPNG();
                    if (pngData != null && pngData.Length > 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (enableDebug)
                {
                    var message = $"{LogPrefix} Direct PNG encode failed, attempting fallback. Reason: {ex.Message}";
                    Debug.LogWarning(message);
                    WriteDebugText(message);
                }
            }
        }

        RenderTexture temporary = null;
        Texture2D scratchTexture = null;
        var previousActive = RenderTexture.active;

        try
        {
            temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(texture, temporary);
            RenderTexture.active = temporary;

            scratchTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            scratchTexture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
            scratchTexture.Apply(false);

            pngData = scratchTexture.EncodeToPNG();
            return pngData != null && pngData.Length > 0;
        }
        catch (Exception ex)
        {
            if (enableDebug)
            {
                var message = $"{LogPrefix} Fallback PNG encode failed: {ex.Message}";
                Debug.LogError(message);
                WriteDebugText(message);
            }
            pngData = null;
            return false;
        }
        finally
        {
            if (scratchTexture != null)
            {
                Destroy(scratchTexture);
            }

            if (temporary != null)
            {
                RenderTexture.ReleaseTemporary(temporary);
            }

            RenderTexture.active = previousActive;
        }
    }

}