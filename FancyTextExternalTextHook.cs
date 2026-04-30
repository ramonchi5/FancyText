using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LiveSplit.Options;

namespace LiveSplit.UI.Components
{
    internal static class FancyTextExternalTextHook
    {
        private static readonly object Sync = new object();
        private static readonly PointF[] FixedBlurOffsets =
        {
            new PointF(-1f, 0f),
            new PointF(1f, 0f),
            new PointF(0f, -1f),
            new PointF(0f, 1f),
            new PointF(0f, 0f)
        };
        private static readonly float[] FixedBlurWeights = { 0.26f, 0.26f, 0.26f, 0.26f, 1f };
        private static readonly HashSet<string> PatchedMethods = new HashSet<string>(StringComparer.Ordinal);
        private static int LastAssemblyCount = -1;

        public static string LastError { get; private set; }

        public static void Install()
        {
            lock (Sync)
            {
                try
                {
                    Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    if (assemblies.Length == LastAssemblyCount)
                    {
                        return;
                    }

                    LastAssemblyCount = assemblies.Length;
                    foreach (Assembly assembly in assemblies)
                    {
                        TryPatchTotalTimeloss(assembly);
                        TryPatchSplitDetail(assembly);
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }
        }

        private static void TryPatchTotalTimeloss(Assembly assembly)
        {
            Type type = GetType(assembly, "TotalTimeloss.UI.Components.TotalTimeloss");
            if (type == null)
            {
                return;
            }

            MethodInfo target = type.GetMethod(
                "DrawTextWithEffects",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                DrawTextArgs,
                null);
            MethodInfo replacement = typeof(FancyTextExternalTextHook).GetMethod(
                "DrawInstanceTextReplacement",
                BindingFlags.Static | BindingFlags.NonPublic);
            TryPatch(target, replacement);
        }

        private static void TryPatchSplitDetail(Assembly assembly)
        {
            Type type = GetType(assembly, "LiveSplit.UI.Components.SplitDetailComponent");
            if (type == null)
            {
                return;
            }

            MethodInfo target = type.GetMethod(
                "DrawTextWithEffects",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                DrawTextArgs,
                null);
            MethodInfo replacement = typeof(FancyTextExternalTextHook).GetMethod(
                "DrawStaticTextReplacement",
                BindingFlags.Static | BindingFlags.NonPublic);
            TryPatch(target, replacement);
        }

        private static readonly Type[] DrawTextArgs =
        {
            typeof(Graphics),
            typeof(string),
            typeof(Font),
            typeof(Color),
            typeof(RectangleF),
            typeof(StringFormat),
            typeof(LayoutSettings)
        };

        private static Type GetType(Assembly assembly, string fullName)
        {
            try
            {
                return assembly.GetType(fullName, false);
            }
            catch
            {
                return null;
            }
        }

        private static void TryPatch(MethodInfo target, MethodInfo replacement)
        {
            if (target == null || replacement == null)
            {
                return;
            }

            string key = target.Module.ModuleVersionId.ToString("N") + ":" + target.MetadataToken.ToString("X8");
            if (PatchedMethods.Contains(key))
            {
                return;
            }

            RedirectMethod(target, replacement);
            PatchedMethods.Add(key);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void DrawInstanceTextReplacement(object instance, Graphics g, string text, Font font, Color textColor, RectangleF rect, StringFormat format, LayoutSettings settings)
        {
            DrawText(g, text, font, textColor, rect, format, settings);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void DrawStaticTextReplacement(Graphics g, string text, Font font, Color textColor, RectangleF rect, StringFormat format, LayoutSettings settings)
        {
            DrawText(g, text, font, textColor, rect, format, settings);
        }

        private static void DrawText(Graphics g, string text, Font font, Color textColor, RectangleF rect, StringFormat format, LayoutSettings settings)
        {
            if (g == null || string.IsNullOrEmpty(text) || font == null || format == null)
            {
                return;
            }

            FancyTextResolvedEffects effects = FancyTextRuntime.GetCurrentComponentEffects();
            bool overrideOutline = effects != null && effects.OverrideOutline;
            bool overrideShadow = effects != null && effects.OverrideShadow;
            bool hasGradient = effects != null && effects.HasGradient;

            bool hasShadow = overrideShadow
                ? effects.ShadowEnabled
                : settings != null && settings.DropShadows;
            Color shadowColor = overrideShadow
                ? effects.ShadowColor
                : settings != null ? settings.ShadowsColor : Color.Transparent;
            Color outlineColor = overrideOutline
                ? effects.OutlineColor
                : settings != null ? settings.TextOutlineColor : Color.Transparent;

            SizeF measured = g.MeasureString(text, font);
            float x = rect.X;
            if (format.Alignment == StringAlignment.Far)
            {
                x = rect.Right - measured.Width;
            }
            else if (format.Alignment == StringAlignment.Center)
            {
                x = rect.X + (rect.Width - measured.Width) / 2f;
            }

            float y = rect.Y;
            float boxWidth = GetTextBoxWidth(rect, measured);
            float boxHeight = GetTextBoxHeight(rect, measured);
            var textBox = new RectangleF(x, y, boxWidth, boxHeight);

            using (var nearFormat = new StringFormat(format))
            {
                nearFormat.Alignment = StringAlignment.Near;
                nearFormat.Trimming = StringTrimming.None;
                nearFormat.FormatFlags |= StringFormatFlags.NoWrap;

                float fontSize = GetFontSize(g, font);
                float outlineSize = overrideOutline
                    ? Math.Max(0f, effects.OutlineSize)
                    : GetDefaultOutlineSize(fontSize);
                bool usePath = overrideShadow
                    || (g.TextRenderingHint == TextRenderingHint.AntiAlias
                        && (outlineColor.A > 0 || hasGradient || overrideOutline));

                if (usePath)
                {
                    using (var path = new GraphicsPath())
                    {
                        if (hasShadow && shadowColor.A > 0)
                        {
                            if (overrideShadow)
                            {
                                DrawCustomShadow(g, text, font, fontSize, textBox, nearFormat, effects);
                            }
                            else
                            {
                                using (var shadowBrush = new SolidBrush(shadowColor))
                                {
                                    path.AddString(text, font.FontFamily, (int)font.Style, fontSize, new RectangleF(x + 2f, y + 2f, 9999f, 9999f), nearFormat);
                                    g.FillPath(shadowBrush, path);
                                    path.Reset();
                                    path.AddString(text, font.FontFamily, (int)font.Style, fontSize, new RectangleF(x + 1f, y + 1f, 9999f, 9999f), nearFormat);
                                    g.FillPath(shadowBrush, path);
                                    path.Reset();
                                }
                            }
                        }

                        path.AddString(text, font.FontFamily, (int)font.Style, fontSize, new RectangleF(x, y, 9999f, 9999f), nearFormat);

                        if (outlineColor.A > 0 && outlineSize > 0f)
                        {
                            using (var outlinePen = new Pen(outlineColor, outlineSize) { LineJoin = LineJoin.Round })
                            {
                                g.DrawPath(outlinePen, path);
                            }
                        }

                        using (Brush textBrush = CreateFillBrush(effects, textColor, textBox))
                        {
                            g.FillPath(textBrush, path);
                        }
                    }

                    return;
                }

                if (hasShadow && shadowColor.A > 0)
                {
                    float shadowOffset = overrideShadow ? Math.Max(0f, effects.ShadowSize) : 1f;
                    using (var shadowBrush = new SolidBrush(shadowColor))
                    {
                        g.DrawString(text, font, shadowBrush, x + shadowOffset, y + shadowOffset, nearFormat);
                        if (!overrideShadow)
                        {
                            g.DrawString(text, font, shadowBrush, x + 2f, y + 2f, nearFormat);
                        }
                    }
                }

                using (Brush textBrush = CreateFillBrush(effects, textColor, textBox))
                {
                    g.DrawString(text, font, textBrush, x, y, nearFormat);
                }
            }
        }

        private static float GetTextBoxWidth(RectangleF rect, SizeF measured)
        {
            if (rect.Width > 0f && rect.Width < 4096f)
            {
                return rect.Width;
            }

            return Math.Max(1f, measured.Width);
        }

        private static float GetTextBoxHeight(RectangleF rect, SizeF measured)
        {
            if (rect.Height > 0f && rect.Height < 4096f)
            {
                return rect.Height;
            }

            return Math.Max(1f, measured.Height);
        }

        private static void DrawCustomShadow(Graphics g, string text, Font font, float fontSize, RectangleF textBox, StringFormat format, FancyTextResolvedEffects effects)
        {
            if (effects.ShadowNormalEnabled)
                DrawCustomShadowLayer(g, text, font, fontSize, textBox, format, effects, FancyTextShadowMode.Behind);
            else if (effects.ShadowOutsideEnabled)
                DrawCustomShadowLayer(g, text, font, fontSize, textBox, format, effects, FancyTextShadowMode.OutsideOnly);
        }

        private static void DrawCustomShadowLayer(Graphics g, string text, Font font, float fontSize, RectangleF textBox, StringFormat format, FancyTextResolvedEffects effects, FancyTextShadowMode mode)
        {
            float blurRadius = Math.Max(0f, effects.ShadowBlur);
            float offset = Math.Max(0f, effects.ShadowSize);

            if (offset <= 0f && blurRadius <= 0f)
            {
                return;
            }

            int multiply = GetShadowMultiply(effects);
            bool useFixedBlur = blurRadius > 0f;

            if (mode == FancyTextShadowMode.Behind)
            {
                using (GraphicsPath shadowPath = new GraphicsPath())
                using (SolidBrush shadowBrush = new SolidBrush(effects.ShadowColor))
                {
                    shadowPath.AddString(
                        text,
                        font.FontFamily,
                        (int)font.Style,
                        fontSize,
                        new RectangleF(textBox.X + offset, textBox.Y + offset, 9999f, 9999f),
                        format);

                    if (useFixedBlur)
                        DrawFixedBlurShadow(g, shadowPath, null, effects.ShadowColor, mode, multiply);
                    else
                        FillPathRepeated(g, shadowPath, shadowBrush, GetShadowMultiply(effects));
                }
                return;
            }

            using (GraphicsPath textPath = new GraphicsPath())
            {
                textPath.AddString(text, font.FontFamily, (int)font.Style, fontSize, new RectangleF(textBox.X, textBox.Y, 9999f, 9999f), format);

                using (GraphicsPath shadowPath = new GraphicsPath())
                using (SolidBrush shadowBrush = new SolidBrush(effects.ShadowColor))
                {
                    shadowPath.AddString(
                        text,
                        font.FontFamily,
                        (int)font.Style,
                        fontSize,
                        new RectangleF(textBox.X + offset, textBox.Y + offset, 9999f, 9999f),
                        format);

                    if (useFixedBlur)
                        DrawFixedBlurShadow(g, shadowPath, textPath, effects.ShadowColor, mode, multiply);
                    else
                        FillShadowPath(g, shadowPath, textPath, shadowBrush, mode, multiply);
                }
            }
        }

        private static void DrawFixedBlurShadow(Graphics g, GraphicsPath shadowPath, GraphicsPath textPath, Color color, FancyTextShadowMode mode, int multiply)
        {
            for (int i = 0; i < FixedBlurOffsets.Length; i++)
            {
                Color sampleColor = GetStackedShadowColor(color, FixedBlurWeights[i], multiply);
                if (sampleColor.A <= 0)
                    continue;

                using (SolidBrush brush = new SolidBrush(sampleColor))
                {
                    PointF offset = FixedBlurOffsets[i];
                    if (mode == FancyTextShadowMode.Behind)
                    {
                        GraphicsState saved = g.Save();
                        try
                        {
                            g.TranslateTransform(offset.X, offset.Y);
                            g.FillPath(brush, shadowPath);
                        }
                        finally
                        {
                            g.Restore(saved);
                        }
                    }
                    else
                    {
                        using (GraphicsPath shiftedPath = (GraphicsPath)shadowPath.Clone())
                        using (Matrix matrix = new Matrix())
                        {
                            matrix.Translate(offset.X, offset.Y);
                            shiftedPath.Transform(matrix);
                            FillShadowPath(g, shiftedPath, textPath, brush, mode, 1);
                        }
                    }
                }
            }
        }

        private static Color GetStackedShadowColor(Color color, float weight, int multiply)
        {
            double alpha = Math.Max(0d, Math.Min(1d, (color.A / 255d) * weight));
            if (multiply > 1)
                alpha = 1d - Math.Pow(1d - alpha, Math.Max(1, Math.Min(8, multiply)));

            int a = Math.Max(0, Math.Min(255, (int)Math.Round(alpha * 255d)));
            return Color.FromArgb(a, color.R, color.G, color.B);
        }

        private static int GetShadowMultiply(FancyTextResolvedEffects effects)
        {
            if (effects == null)
                return 1;

            return Math.Max(1, Math.Min(8, effects.ShadowMultiply));
        }

        private static void FillShadowPath(Graphics g, GraphicsPath shadowPath, GraphicsPath textPath, Brush brush, FancyTextShadowMode mode, int multiply)
        {
            if (mode == FancyTextShadowMode.Behind)
            {
                FillPathRepeated(g, shadowPath, brush, multiply);
                return;
            }

            using (var region = mode == FancyTextShadowMode.OutsideOnly ? new Region(shadowPath) : new Region(textPath))
            {
                if (mode == FancyTextShadowMode.OutsideOnly)
                    region.Exclude(textPath);
                else
                    region.Intersect(shadowPath);

                for (int i = 0; i < multiply; i++)
                    g.FillRegion(brush, region);
            }
        }

        private static void FillPathRepeated(Graphics g, GraphicsPath path, Brush brush, int multiply)
        {
            for (int i = 0; i < multiply; i++)
                g.FillPath(brush, path);
        }

        private static Brush CreateFillBrush(FancyTextResolvedEffects effects, Color baseColor, RectangleF rect)
        {
            if (effects == null || !effects.HasGradient)
            {
                return new SolidBrush(baseColor);
            }

            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                rect = new RectangleF(rect.X, rect.Y, Math.Max(1f, rect.Width), Math.Max(1f, rect.Height));
            }

            PointF start;
            PointF end;
            switch (effects.GradientDirection)
            {
                case FancyTextGradientDirection.Horizontal:
                    start = new PointF(rect.Left, rect.Top);
                    end = new PointF(rect.Right, rect.Top);
                    break;
                case FancyTextGradientDirection.DiagonalDown:
                    start = new PointF(rect.Left, rect.Top);
                    end = new PointF(rect.Right, rect.Bottom);
                    break;
                case FancyTextGradientDirection.DiagonalUp:
                    start = new PointF(rect.Left, rect.Bottom);
                    end = new PointF(rect.Right, rect.Top);
                    break;
                default:
                    start = new PointF(rect.Left, rect.Top);
                    end = new PointF(rect.Left, rect.Bottom);
                    break;
            }

            Color middle = effects.UseExistingColorMiddle ? baseColor : effects.GradientColor2;
            var brush = new LinearGradientBrush(start, end, effects.GradientColor1, effects.GradientColor3);
            brush.InterpolationColors = new ColorBlend
            {
                Positions = new[] { 0f, 0.5f, 1f },
                Colors = new[] { effects.GradientColor1, middle, effects.GradientColor3 }
            };
            return brush;
        }

        private static float GetDefaultOutlineSize(float fontSize)
        {
            return 2.1f + (fontSize * 0.055f);
        }

        private static float GetFontSize(Graphics g, Font font)
        {
            return font.Unit == GraphicsUnit.Point
                ? font.Size * g.DpiY / 72f
                : font.Size;
        }

        private static void RedirectMethod(MethodInfo target, MethodInfo replacement)
        {
            RuntimeHelpers.PrepareMethod(target.MethodHandle);
            RuntimeHelpers.PrepareMethod(replacement.MethodHandle);

            IntPtr targetAddress = target.MethodHandle.GetFunctionPointer();
            IntPtr replacementAddress = replacement.MethodHandle.GetFunctionPointer();
            byte[] patch = CreateJumpPatch(targetAddress, replacementAddress);

            uint oldProtect;
            if (!VirtualProtect(targetAddress, (UIntPtr)patch.Length, PageExecuteReadWrite, out oldProtect))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                Marshal.Copy(patch, 0, targetAddress, patch.Length);
                FlushInstructionCache(GetCurrentProcess(), targetAddress, (UIntPtr)patch.Length);
            }
            finally
            {
                uint ignored;
                VirtualProtect(targetAddress, (UIntPtr)patch.Length, oldProtect, out ignored);
            }
        }

        private static byte[] CreateJumpPatch(IntPtr targetAddress, IntPtr replacementAddress)
        {
            if (IntPtr.Size == 8)
            {
                byte[] patch = new byte[12];
                patch[0] = 0x48;
                patch[1] = 0xB8;
                BitConverter.GetBytes(replacementAddress.ToInt64()).CopyTo(patch, 2);
                patch[10] = 0xFF;
                patch[11] = 0xE0;
                return patch;
            }

            int relative = replacementAddress.ToInt32() - targetAddress.ToInt32() - 5;
            byte[] x86Patch = new byte[5];
            x86Patch[0] = 0xE9;
            BitConverter.GetBytes(relative).CopyTo(x86Patch, 1);
            return x86Patch;
        }

        private const uint PageExecuteReadWrite = 0x40;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll")]
        private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
    }
}
