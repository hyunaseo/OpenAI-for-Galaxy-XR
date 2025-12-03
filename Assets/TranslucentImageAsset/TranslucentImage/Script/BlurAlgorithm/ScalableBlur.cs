using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace LeTai.Asset.TranslucentImage
{
public class ScalableBlur : IBlurAlgorithm
{
    readonly RenderTargetIdentifier[] scratches = new RenderTargetIdentifier[14];

    bool                  isBirp;
    Material              material;
    ScalableBlurConfig    config;
    MaterialPropertyBlock propertyBlock;

    LocalKeyword kwBackgroundFillNone;
    LocalKeyword kwBackgroundFillColor;
    LocalKeyword kwUseExtraSample;

    Material Material
    {
        get
        {
            if (material == null)
            {
                Material = new Material(Shader.Find(isBirp
                                                        ? "Hidden/EfficientBlur"
                                                        : "Hidden/EfficientBlur_UniversalRP"));
            }

            return material;
        }
        set => material = value;
    }

    public void Init(BlurConfig config, bool isBirp)
    {
        this.isBirp   = isBirp;
        this.config   = (ScalableBlurConfig)config;
        propertyBlock = propertyBlock ?? new MaterialPropertyBlock();
    }

    public void Blur(
        CommandBuffer          cmd,
        RenderTargetIdentifier src,
        Rect                   srcCropRegion,
        Rect                   activeRegion,
        BackgroundFill         backgroundFill,
        RenderTexture          target
    )
    {
        (float strength, float radius, int iterations) = GetEffectiveConfig(target.width / srcCropRegion.width,
                                                                            target.height / srcCropRegion.height);
        var offsetDistanceDown = (radius / 2f) * Vector2.one;
        var offsetDistanceUp   = (radius / 2f /*- .5f*/) * Vector2.one;

        int   stepCount = Mathf.Clamp(iterations * 2, 1, scratches.Length * 2 - 1);
        float extent    = strength;

        var activeRegionRelative = RectUtils.Intersect(activeRegion, srcCropRegion);
        activeRegionRelative.x      = (activeRegionRelative.x - srcCropRegion.x) / srcCropRegion.width;
        activeRegionRelative.y      = (activeRegionRelative.y - srcCropRegion.y) / srcCropRegion.height;
        activeRegionRelative.width  = activeRegionRelative.width / srcCropRegion.width;
        activeRegionRelative.height = activeRegionRelative.height / srcCropRegion.height;

        if (activeRegionRelative.width == 0 || activeRegionRelative.height == 0)
            return;

        ConfigMaterial(backgroundFill);
        propertyBlock.Clear();
        propertyBlock.SetInteger("_IsLast", 0);

        if (stepCount > 1)
        {
            CropViewport(target.width >> 1, target.height >> 1, extent, out var viewportFirst, out var activeRegionFirst);
            propertyBlock.SetVector(ShaderID.CROP_REGION, RectUtils.ToMinMaxVector(RectUtils.Crop(srcCropRegion, activeRegionFirst)));

            var targetSize1 = new Vector2(target.width >> 1, target.height >> 1);
            propertyBlock.SetVector(ShaderID.TARGET_SIZE, targetSize1);
            propertyBlock.SetVector(ShaderID.OFFSET,      offsetDistanceDown / targetSize1);

            Blitter.Blit(cmd, src, scratches[0], Material, 0, propertyBlock, viewportFirst);
        }

        var maxDepth = Mathf.Min(iterations - 1, scratches.Length - 1);
        for (var i = 1; i < stepCount - 1; i++)
        {
            var fromIdx     = SimplePingPong(i - 1, maxDepth);
            var toIdx       = SimplePingPong(i,     maxDepth);
            var targetDepth = toIdx + 1;

            CropViewport(target.width >> targetDepth, target.height >> targetDepth, extent, out var viewportStep, out var activeRegionStep);
            propertyBlock.SetVector(ShaderID.CROP_REGION, RectUtils.ToMinMaxVector(activeRegionStep));

            var targetSizeStep = new Vector2(target.width >> targetDepth, target.height >> targetDepth);
            propertyBlock.SetVector(ShaderID.TARGET_SIZE, targetSizeStep);
            propertyBlock.SetVector(ShaderID.OFFSET, i < maxDepth
                                        ? offsetDistanceDown / targetSizeStep
                                        : offsetDistanceUp / targetSizeStep);

            Blitter.Blit(cmd, scratches[fromIdx], scratches[toIdx], Material, 0, propertyBlock, viewportStep);
        }

        CropViewport(target.width, target.height, 0, out var viewportLast, out var activeRegionLast);
        activeRegionLast = stepCount > 1 ? activeRegionLast : RectUtils.Crop(srcCropRegion, activeRegionLast);
        propertyBlock.SetVector(ShaderID.CROP_REGION, RectUtils.ToMinMaxVector(activeRegionLast));

        var targetSizeFinal = new Vector2(target.width, target.height);
        propertyBlock.SetVector(ShaderID.TARGET_SIZE, targetSizeFinal);
        propertyBlock.SetVector(ShaderID.OFFSET, stepCount == 1
                                    ? offsetDistanceDown / targetSizeFinal
                                    : offsetDistanceUp / targetSizeFinal);
        propertyBlock.SetInteger(ShaderID.IS_LAST, 1);

        Blitter.Blit(cmd,
                     stepCount > 1 ? scratches[0] : src,
                     target,
                     Material,
                     0,
                     propertyBlock,
                     viewportLast);
        return;

        void CropViewport(int targetWidth, int targetHeight, float padding, out Rect viewport, out Rect activeRegionSnapped)
        {
            var x  = activeRegionRelative.x * targetWidth;
            var y  = activeRegionRelative.y * targetHeight;
            var xf = Mathf.Floor(x - padding);
            var yf = Mathf.Floor(y - padding);
            viewport = new Rect(xf,
                                yf,
                                Mathf.Ceil(x + activeRegionRelative.width * targetWidth + padding) - xf,
                                Mathf.Ceil(y + activeRegionRelative.height * targetHeight + padding) - yf);

            viewport.x      = Mathf.Max(viewport.x, 0);
            viewport.y      = Mathf.Max(viewport.y, 0);
            viewport.width  = Mathf.Min(viewport.width,  targetWidth);
            viewport.height = Mathf.Min(viewport.height, targetHeight);

            activeRegionSnapped = new Rect(
                viewport.x / targetWidth,
                viewport.y / targetHeight,
                viewport.width / targetWidth,
                viewport.height / targetHeight
            );
        }
    }

    public int GetScratchesCount(float targetWidth, float targetHeight)
    {
        var (_, _, iterations) = GetEffectiveConfig(targetWidth, targetHeight);
        return Mathf.Min(iterations, scratches.Length);
    }

    public void GetNextScratchDescriptor(ref RenderTextureDescriptor prevDescriptor)
    {
        prevDescriptor.width  >>= 1;
        prevDescriptor.height >>= 1;
        if (prevDescriptor.width <= 0) prevDescriptor.width   = 1;
        if (prevDescriptor.height <= 0) prevDescriptor.height = 1;
    }

    public void SetScratch(int index, RenderTargetIdentifier value)
    {
        scratches[index] = value;
    }

    protected void ConfigMaterial(BackgroundFill backgroundFill)
    {
        var mat = Material;

        if (kwUseExtraSample == default || !kwUseExtraSample.isValid)
            InitKeywords();

        switch (backgroundFill.mode)
        {
        case BackgroundFillMode.None:
            mat.SetKeyword(kwBackgroundFillNone,  true);
            mat.SetKeyword(kwBackgroundFillNone,  true);
            mat.SetKeyword(kwBackgroundFillColor, false);
            break;
        case BackgroundFillMode.Color:
            mat.SetKeyword(kwBackgroundFillColor, true);
            mat.SetKeyword(kwBackgroundFillNone,  false);
            mat.SetColor(ShaderID.BACKGROUND_COLOR, backgroundFill.color);
            break;
        }
        mat.SetKeyword(kwUseExtraSample, config.Mode == ScalableBlurConfig.BlurMode.Balanced);
    }

    (float strength, float radius, int iteration) GetEffectiveConfig(float targetWidth, float targetHeight)
    {
        float scaleFactor = config.GetResolutionScaleFactor(targetWidth, targetHeight);

        float strength = config.Strength * scaleFactor;
        float radius;
        int   iteration;

        if (!config.UseStrength)
        {
            radius    = config.Radius * scaleFactor;
            iteration = config.Iteration;
        }
        else
        {
            (radius, iteration) = ScalableBlurConfig.FromStrength(strength);
        }

        return (strength, radius, iteration);
    }

    static int SimplePingPong(int t, int max)
    {
        if (t > max)
            return 2 * max - t;
        return t;
    }

    void InitKeywords()
    {
        var materialShader = Material.shader;
        kwBackgroundFillNone  = new LocalKeyword(materialShader, "BACKGROUND_FILL_NONE");
        kwBackgroundFillColor = new LocalKeyword(materialShader, "BACKGROUND_FILL_COLOR");
        kwUseExtraSample      = new LocalKeyword(materialShader, "USE_EXTRA_SAMPLE");
    }
}
}
