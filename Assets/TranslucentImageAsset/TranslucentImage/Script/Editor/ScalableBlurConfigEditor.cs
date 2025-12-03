using System;
using LeTai.Common.Editor;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;

namespace LeTai.Asset.TranslucentImage.Editor
{
[CustomEditor(typeof(ScalableBlurConfig))]
[CanEditMultipleObjects]
public class ScalableBlurConfigEditor : UnityEditor.Editor
{
    static readonly EditorPrefValue<bool> SHOW_RESOLUTION_MATCHING = new("TranslucentImage_ShowResolutionMatching", false);

    readonly AnimBool useAdvancedControl = new AnimBool(false);

    int tab, previousTab;

    EditorProperty     radius;
    EditorProperty     iteration;
    EditorProperty     strength;
    EditorProperty     mode;
    EditorProperty     referenceResolution;
    EditorProperty     matchWidthOrHeight;
    SerializedProperty useStrength;

    public void Awake()
    {
        LoadTabSelection();
        useAdvancedControl.value = tab > 0;
    }

    public void OnEnable()
    {
        radius              = new EditorProperty(serializedObject, nameof(ScalableBlurConfig.Radius));
        iteration           = new EditorProperty(serializedObject, nameof(ScalableBlurConfig.Iteration));
        strength            = new EditorProperty(serializedObject, nameof(ScalableBlurConfig.Strength));
        mode                = new EditorProperty(serializedObject, nameof(ScalableBlurConfig.Mode));
        referenceResolution = new EditorProperty(serializedObject, nameof(ScalableBlurConfig.ReferenceResolution));
        matchWidthOrHeight  = new EditorProperty(serializedObject, nameof(ScalableBlurConfig.MatchWidthOrHeight));
        useStrength         = serializedObject.FindProperty("useStrength");

        // Without this the editor will not Repaint automatically when animating
        useAdvancedControl.valueChanged.AddListener(Repaint);
    }

    public override void OnInspectorGUI()
    {
        Draw();
    }

    public void Draw()
    {
        using var changes = new EditorGUI.ChangeCheckScope();
        using var _       = new EditorGUILayout.VerticalScope();

        mode.Draw();

        EditorGUILayout.Space();

        DrawTabBar();
        serializedObject.Update();
        DrawTabsContent();

        EditorGUILayout.Space();

        SHOW_RESOLUTION_MATCHING.Value = EditorGUILayout.BeginFoldoutHeaderGroup(SHOW_RESOLUTION_MATCHING,
                                                                                 "Resolution matching",
                                                                                 EditorStyles.foldout);
        if (SHOW_RESOLUTION_MATCHING)
        {
            referenceResolution.Draw();
            matchWidthOrHeight.Draw();
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        if (changes.changed)
            serializedObject.ApplyModifiedProperties();
    }

    void DrawTabBar()
    {
        using (var h = new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();

            tab = GUILayout.Toolbar(
                tab,
                new[] { "Simple", "Advanced" },
                GUILayout.MinWidth(0),
                GUILayout.MaxWidth(EditorGUIUtility.pixelsPerPoint * 192)
            );

            GUILayout.FlexibleSpace();
        }

        if (tab != previousTab)
        {
            GUI.FocusControl(""); // Defocus
            SaveTabSelection();
            previousTab = tab;
        }

        useAdvancedControl.target = tab == 1;
    }

    void DrawTabsContent()
    {
        if (EditorGUILayout.BeginFadeGroup(1 - useAdvancedControl.faded))
        {
            // EditorProperty dooesn't invoke getter. Not needed anywhere else.
            _ = ((ScalableBlurConfig)target).Strength;
            using var changes = new EditorGUI.ChangeCheckScope();
            strength.Draw();
            if (changes.changed)
                useStrength.boolValue = true;
        }
        EditorGUILayout.EndFadeGroup();

        if (EditorGUILayout.BeginFadeGroup(useAdvancedControl.faded))
        {
            using var changes = new EditorGUI.ChangeCheckScope();
            radius.Draw();
            iteration.Draw();
            if (changes.changed)
                useStrength.boolValue = false;
        }
        EditorGUILayout.EndFadeGroup();
    }

    //Persist selected tab between sessions and instances
    void SaveTabSelection()
    {
        EditorPrefs.SetInt("LETAI_TRANSLUCENTIMAGE_TIS_TAB", tab);
    }

    void LoadTabSelection()
    {
        if (EditorPrefs.HasKey("LETAI_TRANSLUCENTIMAGE_TIS_TAB"))
            tab = EditorPrefs.GetInt("LETAI_TRANSLUCENTIMAGE_TIS_TAB");
    }
}
}
