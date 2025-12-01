# OpenAI API for Galaxy XR

This Unity project demonstrates how to capture a user's voice and egocentric camera image on Galaxy XR devices, send both inputs to the OpenAI API, and stream back textual responses. It is built on top of the Unity Mixed Reality Template (Unity 6000.2.10f1) configured for OpenXR and Android XR (experimental).

## Key Features
- Captures microphone input on Galaxy XR, forwards it to the OpenAI Responses API, and returns text output.
- Sends the current egocentric camera frame alongside the voice transcript to enrich the OpenAI query context.
- Provides optional visual debugging by rendering the passthrough capture onto a quad via the `DrawPassThroughOnQuad` prefab.

## Unity Environment
- Unity Editor: 6000.2.10f1 (Mixed Reality Template)
- XR configuration: OpenXR + Android XR (experimental)
- Tested device: Galaxy XR (developer mode enabled)

## Package Dependencies
- `com.google.xr.extensions` 1.2.0
- `com.openai.unity` 8.8.7
- `com.unity.xr.interaction.toolkit` 3.2.1
- `com.unity.xr.hands` 1.7.0
- `com.unity.xr.arfoundation` 6.2.0

## OpenAI Integration
The sample uses the OpenAI Unity SDK (`com.openai.unity`) to submit multimodal requests that combine:
- Voice recordings captured via Unity's microphone APIs.
- JPEG-encoded passthrough frames pulled from the Galaxy XR camera pipeline.

The OpenAI Responses API handles both inputs, returning text that the app displays in-world. Replace the placeholder API key in the Unity project settings with your OpenAI key before building.

For detailed package setup, follow the official documentation:

- <https://github.com/RageAgainstThePixel/com.openai.unity>

## OpenAI Configuration
- Create an `OpenAIConfiguration` ScriptableObject via **Assets > Create > OpenAI > Configuration**, as described in the package documentation above.
- Store the asset inside `Assets/Resources` so it can be auto-loaded when the app starts.
- Populate the asset with your API credentials (API key, org, and project IDs as needed).
- Assign this asset to the `configuration` field on the `OpenAIQueryVision` component in your scene inspector.
- Alternatively, you can point the client to a `.openai` config file if you prefer not to embed credentials in the project.

## Passthrough Capture
- The passthrough feed is obtained through a Java plugin located at `Plugins/Android/src/com/galaxyXR/passthrough`.
- Keep the plugin in this folder structure; if you relocate it, update all C# calls that reference the Java package path.
- The plugin exposes camera frames to Unity, where they are converted into textures and attached to OpenAI requests or visualized in the scene.

## Setup Checklist
1. Follow the configuration steps in the Android XR guide: <https://developer.android.com/develop/xr/unity>.
2. Enable developer mode on the Galaxy XR headset.
3. In `AndroidManifest.xml`, grant camera access:
	```xml
	<uses-permission android:name="android.permission.CAMERA" />
	<uses-feature android:name="android.hardware.camera2.any" android:required="false" />
	```
4. Import the required Unity packages listed above via the Package Manager.
5. Configure OpenXR for Android XR (experimental) in **Project Settings > XR Plug-in Management**.
6. Create and assign the `OpenAIConfiguration` asset (see above) so that the `OpenAIQueryVision` component has valid OpenAI credentials before running the scene.

## Visualizing Passthrough
To preview the passthrough capture in a scene, add the `DrawPassThroughOnQuad` prefab. It maps the latest camera texture onto a quad so you can verify the image feed while testing.

## Build & Deployment Tips
- Use `adb logcat` to monitor plugin output and OpenAI request status when debugging device builds.
