# Custom TAA for URP (Unity 6)

This is a custom TAA bypass that works with stacked cameras (base + overlay). It was implemented for Universal Render Pipeline using a `ScriptableRendererFeature` + full-screen shader. It applies TAA on the base camera while ignoring overlay (UI, crosshairs etc.). Can be applied before or after post processing.
I did this workaround because my project needed the UI elements to be on a separate camera (mostly post processing related reasons). Unity does not allow Anti-Aliasing on stacked cameras (at least not for this version) and the aliasing in my project was too bad to ignore.
I found many people with the same problem while I was searching for a solution, so I am uploading this here.

## Included Files

- `CustomTAA.shader`
- `CustomTaaRendererFeature.cs`

## Requirements

I made this for my project which uses Unity 6.4 (6000.4.1f1) so I will list this as the Unity requirement. I tried my best not to use deprecated or obsolete methods so hopefully it will work on future versions.

- Unity 6.4 (6000.4.1f1). I did not test it in other versions, it may or may not work.
- Universal Render Pipeline project.

## Features

- Temporal accumulation with per-camera history
- Halton jitter projection per frame
- History reprojection with previous/current matrices
- Neighborhood clamping to reduce ghosting
- Base camera only (overlay/UI cameras are skipped)

## Shader Path Contract

The render feature resolves the shader by:

- `Shader.Find("Custom/CustomTAA")`

So the shader declaration must remain:

- `Shader "Custom/CustomTAA"`

You can also use another custom shader and assign it under "TAA Shader" in the render feature (the render feature will fallback to `Shader.Find("Custom/CustomTAA")` if none is assigned).

## Setup

1. Add both files to your project:
   - `CustomTAA.shader`
   - `CustomTaaRendererFeature.cs`
2. Open the active Universal Renderer asset.
3. Add **Custom Taa Renderer Feature** to Renderer Features.
4. Assign shader manually in the "Taa Shader" field, or rely on `Shader.Find`.
5. Tune settings in the feature inspector:
   - `Jitter Spread`
   - `History Weight`
   - `Neighborhood Clamp`
   - `Pass Event`

## Suggested Starting Values

This is what I am using in my project. Looks similar to Unity's native TAA.
- `Jitter Spread`: `0.75`
- `History Weight`: `0.90`
- `Neighborhood Clamp`: `0.85`

## Camera Scope

The feature runs only when:

- `camera.cameraType == CameraType.Game`
- `cameraData.renderType == CameraRenderType.Base`

Overlay cameras are intentionally excluded.

## Troubleshooting

### Shader not found
- Ensure shader name is exactly `Custom/CustomTAA`.
- Ensure the shader compiles with no errors.
- Ensure the feature is added to the renderer that is actually used by your URP asset.

### RenderGraph resource errors
- Do not cast/use `TextureHandle` as a real texture outside render graph pass execution.
- Bind persistent history via the `RTHandle` texture object.

### Inverted or reflection-like smear
- Usually a UV flip mismatch in reprojection/history sampling.
- Verify Y-flip handling only where needed in the shader’s history UV path.

## Performance Notes

Per-frame cost is mainly:

- Full-screen TAA resolve
- History update blit
- Neighborhood sampling in clamp logic

Cost scales with resolution and number of active base cameras.

## License

Use and modify freely for your project.
