using UnityEngine;
using UnityEngine.Serialization;

namespace LeTai.Asset.TranslucentImage
{
[CreateAssetMenu(fileName = "New Scalable Blur Config",
                 menuName = "Translucent Image/ Scalable Blur Config",
                 order = 100)]
public class ScalableBlurConfig : BlurConfig
{
    public enum BlurMode
    {
        Performance,
        Balanced,
    }

    [SerializeField]
    [Tooltip("Use Balanced for light to medium blur or detailed background," +
             " Performance for strong blur, smooth background or very low end hardware")]
    BlurMode mode = BlurMode.Balanced;

    [SerializeField]
    [Tooltip("Blurriness. Does NOT affect performance")]
    float radius = 4;
    [SerializeField]
    [Tooltip("The number of times to run the algorithm to increase the smoothness of the effect. Can affect performance when increase")]
    [Range(0, 8)]
    int iteration = 4;
    [SerializeField]
    [Tooltip("How strong the blur is")]
    float strength;

    [SerializeField]
    [Tooltip("Resolution the blur strength is designed for. If the camera resolution is larger, the blur will be stronger, and if it's smaller, the blur will be weaker.")]
    Vector2 referenceResolution = new(1920, 1080);

    [SerializeField]
    [Tooltip("0 = Match width, 1 = Match height, choose depend on how your camera viewport change with resolution. By default, vertical viewport is constant so we should match width")]
    [Range(0, 1)]
    float matchWidthOrHeight = 0;

    [SerializeField]
    bool useStrength = true;


    public BlurMode Mode
    {
        get => mode;
        set => mode = value;
    }

    /// <summary>
    /// Distance between the base texel and the texel to be sampled.
    /// </summary>
    public float Radius
    {
        get => radius;
        set
        {
            UseStrength = false;
            radius      = Mathf.Max(0, value);
        }
    }

    /// <summary>
    /// Half the number of time to process the image. It is half because the real number of iteration must alway be even. Using half also make calculation simpler
    /// </summary>
    /// <value>
    /// Must be non-negative
    /// </value>
    public int Iteration
    {
        get => iteration;
        set
        {
            UseStrength = false;
            iteration   = Mathf.Max(0, value);
        }
    }

    /// <summary>
    /// User friendly property to control the amount of blur
    /// </summary>
    ///<value>
    /// Must be non-negative
    /// </value>
    public override float Strength
    {
        get => strength; // = Radius * Mathf.Pow(2, Iteration);
        set
        {
            UseStrength         = true;
            strength            = Mathf.Clamp(value, 0, (1 << 14) * (1 << 14));
            (Radius, Iteration) = FromStrength(strength);
        }
    }

    public bool UseStrength
    {
        get => useStrength;
        set => useStrength = value;
    }

    public Vector2 ReferenceResolution
    {
        get => referenceResolution;
        set => referenceResolution = value;
    }

    public float MatchWidthOrHeight
    {
        get => matchWidthOrHeight;
        set => matchWidthOrHeight = value;
    }

    internal float GetResolutionScaleFactor(float width, float height)
    {
        if (referenceResolution.x == 0 || referenceResolution.y == 0)
            return 1;

        float meanLog = Mathf.Lerp(
            Mathf.Log(width / referenceResolution.x,  2),
            Mathf.Log(height / referenceResolution.y, 2),
            matchWidthOrHeight
        );
        return Mathf.Pow(2, meanLog);
    }

    internal static (float calcRadius, int calcIteration) FromStrength(float targetStrength)
    {
        var maxIterContribution = Mathf.Pow(targetStrength, .6f); // Make configurable?

        // Bit fiddling would be faster, but need unsafe or .NET Core 3.0+
        // for BitOperations, and BitConverter that doesn't creates garbages :(
        var calcIteration = 0;
        while ((1 << calcIteration) < maxIterContribution)
            calcIteration++;
        var calcRadius = targetStrength / (1 << calcIteration);

        return (calcRadius, calcIteration);
    }
}
}
