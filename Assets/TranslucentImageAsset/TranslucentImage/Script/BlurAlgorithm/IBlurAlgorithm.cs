using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace LeTai.Asset.TranslucentImage
{
public enum BlurAlgorithmType
{
    ScalableBlur
}

public enum BackgroundFillMode
{
    None,
    Color
}

[Serializable]
public class BackgroundFill
{
    public BackgroundFillMode mode  = BackgroundFillMode.None;
    public Color              color = Color.white;
}

public interface IBlurAlgorithm
{
    void Init(BlurConfig config, bool isBirp);

    void Blur(
        CommandBuffer          cmd,
        RenderTargetIdentifier src,
        Rect                   srcCropRegion,
        Rect                   activeRegion,
        BackgroundFill         backgroundFill,
        RenderTexture          target
    );

    int GetScratchesCount(float targetWidth, float targetHeight);

    void GetNextScratchDescriptor(ref RenderTextureDescriptor prevDescriptor);

    void SetScratch(int index, RenderTargetIdentifier value);
}
}
