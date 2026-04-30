# FancyText - LiveSplit Component

FancyText is a controller component for LiveSplit. It does not draw text of its own; it targets other components in the current layout and applies richer text effects to their labels/times.

The current focus is text only. Layout background, border, and rounded-window work belongs in the separate FancyBackground component.

## What It Does

- Targets all components or selected components from the current layout.
- Supports multiple FancyText instances in the same layout.
- Adds configurable text outlines with custom color and size.
- Adds configurable text shadows with custom color, offset, fixed low-cost blur, and multiply strength.
- Adds 3-color gradients.
- Can use each label's existing color as the middle gradient color, so multi-color components keep their color identity.
- Works without replacing or patching `LiveSplit.Core.dll`.

## How It Works

FancyText installs in-memory hooks when the component loads:

- `LiveSplit.UI.SimpleLabel.Draw` for stock LiveSplit text.
- A small external text hook for known components that draw text manually, including SplitDetail and TotalTimeloss.

FancyText also wraps layout components with lightweight draw-context proxies. This lets each FancyText instance know which layout component is currently drawing, so selected-component targeting can work.

## Targets

- **Disabled**: this FancyText instance does not affect anything.
- **All Components**: applies to supported text in every component.
- **Selected Components**: applies only to checked components in the target list.

The target list refreshes when layout settings open. The **Refresh** button recreates the current FancyText instance with the same settings, which helps when components were added while the Layout Editor was already open.

## Text Gradient

FancyText has two gradient modes:

- **Gradient with existing colours**: color 1 -> each label's own color -> color 3.
- **New Gradient**: color 1 -> color 2 -> color 3.

Gradient direction can be horizontal, vertical, diagonal down, or diagonal up.

## Outline

The outline section can override LiveSplit's normal outline color/size. Size is absolute pixels; `0` disables the outline.

## Shadow

The shadow section can override LiveSplit's normal text shadow.

- **Normal** draws the shadow behind the text.
- **Outside Only** clips the shadow out of the filled text shape.
- **Blur** is a fixed, low-cost blur approximation designed to avoid per-frame bitmap blur work.
- **Multiply** strengthens the shadow without recalculating blur.

For performance, blur is intentionally a checkbox instead of a free numeric blur radius. True bitmap blur is expensive on rapidly changing timer text.

## Installation

1. Build the project in **Release** mode.
2. Copy the compiled DLL:

```text
bin\Release\LiveSplit.FancyText.dll
```

into your LiveSplit `Components` folder.

3. Restart LiveSplit.
4. Open **Layout Editor**.
5. Add `FancyText` from `Media`.

## Building From Source

### Requirements

- Visual Studio 2022 or MSBuild/dotnet capable of building .NET Framework projects.
- .NET Framework 4.8 Developer Pack.
- `LiveSplit.Core.dll` and `UpdateManager.dll` in `packages\`, or references supplied from a LiveSplit checkout/install.

### Build

```bat
dotnet build .\FancyText.sln -c Release
```

Output:

```text
bin\Release\LiveSplit.FancyText.dll
```

When this repo is cloned next to a `LiveSplit` checkout, the build also copies the DLL and PDB to:

```text
..\LiveSplit\artifacts\bin\FancyText\release\
```

Set `CopyToLiveSplitArtifacts=false` to skip that local copy:

```bat
dotnet build .\FancyText.sln -c Release -p:CopyToLiveSplitArtifacts=false
```

## GitHub Actions

The `build` workflow restores and builds `FancyText.sln` on `windows-latest`, then uploads:

```text
bin\Release\LiveSplit.FancyText.dll
```

## Development Notes

Important files:

| File | Purpose |
|---|---|
| `FancyTextComponent.cs` | LiveSplit component entry point and component identity. |
| `FancyTextSettings.cs` | Compact settings UI and XML persistence. |
| `FancyTextRuntime.cs` | Target detection, component wrapping, and effect resolution. |
| `FancyTextSimpleLabelHook.cs` | Hook path for stock LiveSplit `SimpleLabel` text. |
| `FancyTextExternalTextHook.cs` | Hook path for supported manual text drawing components. |

Before publishing a new build, test:

- All Components targeting.
- Selected Components targeting.
- Adding a component after FancyText already exists, then using Refresh.
- Multiple FancyText instances with different targets.
- Existing-colour gradients on components with multiple label colors.
- New Gradient mode.
- Outline size `0`, small sizes, and large sizes.
- Normal shadow and Outside Only shadow.
- Blur enabled on static labels and on the main timer.
- Multiply values `1`, `2`, and `3`.
- SplitDetail and TotalTimeloss targets.

## Credits / Third-Party Notices

FancyText is a custom component for [LiveSplit](https://github.com/LiveSplit/LiveSplit).

LiveSplit is licensed under the MIT License. Copyright (c) 2013 Christopher Serr and Sergey Papushin.

Some implementation patterns, including component structure, layout settings integration, rendering behavior, and component factory registration, were developed with reference to LiveSplit and existing LiveSplit components.

For more details, see [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

## License

FancyText is licensed under the MIT License. See [`LICENSE.txt`](LICENSE.txt).

LiveSplit and related LiveSplit assemblies are licensed separately. See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
