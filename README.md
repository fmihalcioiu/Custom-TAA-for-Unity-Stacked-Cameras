# Custom TAA for URP (Unity 6)

This is a custom TAA bypass that works with stacked cameras (base + overlay). It was implemented for Universal Render Pipeline using a `ScriptableRendererFeature` + full-screen shader. 
It applies TAA on the base camera while ignoring overlay (UI, crosshairs etc.). Can be applied before or after post processing.

## Included Files

- `CustomTAA.shader`
- `CustomTaaRendererFeature.cs`

## Requirements

- Unity 6
- URP (RenderGraph path)
- Universal Renderer asset in use
- Depth texture available (pass requests depth input)

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
4. Optionally assign shader manually, or rely on `Shader.Find`.
5. Tune settings in the feature inspector:
   - `Jitter Spread`
   - `History Weight`
   - `Neighborhood Clamp`
   - `Pass Event`

## Suggested Starting Values

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
