using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LiveSplit.UI;

namespace LiveSplit.UI.Components
{
    internal static class FancyTextSimpleLabelHook
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
        private static bool _attemptedInstall;

        public static bool IsInstalled { get; private set; }
        public static string LastError { get; private set; }

        public static void Install()
        {
            if (IsInstalled)
            {
                return;
            }

            lock (Sync)
            {
                if (IsInstalled || _attemptedInstall)
                {
                    return;
                }

                _attemptedInstall = true;

                try
                {
                    MethodInfo target = typeof(SimpleLabel).GetMethod(
                        "Draw",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(Graphics) },
                        null);

                    MethodInfo replacement = typeof(FancyTextSimpleLabelHook).GetMethod(
                        "DrawReplacement",
                        BindingFlags.Static | BindingFlags.NonPublic);

                    if (target == null || replacement == null)
                    {
                        LastError = "Could not find SimpleLabel.Draw or FancyText replacement method.";
                        return;
                    }

                    RedirectMethod(target, replacement);
                    IsInstalled = true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }
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

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void DrawReplacement(SimpleLabel label, Graphics g)
        {
            try
            {
                DrawReplacementCore(label, g);
            }
            catch
            {
                DrawFallback(label, g);
            }
        }

        private static void DrawReplacementCore(SimpleLabel label, Graphics g)
        {
            if (label == null || g == null)
            {
                return;
            }

            using (StringFormat format = CreateFormat(label.HorizontalAlignment, label.VerticalAlignment))
            {
                if (!label.IsMonospaced)
                {
                    string actualText = CalculateAlternateText(label, g, label.Width, format);
                    DrawText(label, actualText, g, label.X, label.Y, label.Width, label.Height, format);
                    return;
                }
            }

            using (StringFormat monoFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = label.VerticalAlignment
            })
            {
                int digitWidth = MeasureGlyphWidth(label, g, "0");
                string cutOffText = CutOff(label, g);
                float offset = label.Width - MeasureActualWidth(label, cutOffText, g);

                if (label.HorizontalAlignment != StringAlignment.Far)
                {
                    offset = 0f;
                }

                for (int charIndex = 0; charIndex < cutOffText.Length; charIndex++)
                {
                    char curChar = cutOffText[charIndex];
                    float curOffset = char.IsDigit(curChar)
                        ? digitWidth
                        : MeasureGlyphWidth(label, g, curChar.ToString());

                    DrawText(label, curChar.ToString(), g, label.X + offset - (curOffset / 2f), label.Y, curOffset * 2f, label.Height, monoFormat);
                    offset += curOffset;
                }
            }
        }

        private static void DrawFallback(SimpleLabel label, Graphics g)
        {
            if (label == null || g == null || label.Font == null || string.IsNullOrEmpty(label.Text))
            {
                return;
            }

            try
            {
                using (StringFormat format = CreateFormat(label.HorizontalAlignment, label.VerticalAlignment))
                using (Brush brush = new SolidBrush(GetBaseTextColor(label)))
                {
                    var rect = new RectangleF(
                        label.X,
                        label.Y,
                        Math.Max(1f, label.Width),
                        Math.Max(1f, label.Height));
                    g.DrawString(label.Text, label.Font, brush, rect, format);
                }
            }
            catch
            {
            }
        }

        private static StringFormat CreateFormat(StringAlignment horizontalAlignment, StringAlignment verticalAlignment)
        {
            return new StringFormat
            {
                Alignment = horizontalAlignment,
                LineAlignment = verticalAlignment,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            };
        }

        private static string CalculateAlternateText(SimpleLabel label, Graphics g, float width, StringFormat format)
        {
            string actualText = label.Text;
            label.ActualWidth = g.MeasureString(label.Text, label.Font, 9999, format).Width;

            IEnumerable<string> alternates = label.AlternateText ?? new string[0];
            foreach (string curText in alternates.OrderByDescending(x => x.Length))
            {
                if (width < label.ActualWidth)
                {
                    actualText = curText;
                    label.ActualWidth = g.MeasureString(actualText, label.Font, 9999, format).Width;
                }
                else
                {
                    break;
                }
            }

            return actualText;
        }

        private static string CutOff(SimpleLabel label, Graphics g)
        {
            label.ActualWidth = MeasureActualWidth(label, label.Text, g);
            if (label.ActualWidth < label.Width)
            {
                return label.Text;
            }

            string cutOffText = label.Text;
            while (label.ActualWidth >= label.Width && !string.IsNullOrEmpty(cutOffText))
            {
                cutOffText = cutOffText.Remove(cutOffText.Length - 1, 1);
                label.ActualWidth = MeasureActualWidth(label, cutOffText + "...", g);
            }

            return label.ActualWidth >= label.Width ? string.Empty : cutOffText + "...";
        }

        private static float MeasureActualWidth(SimpleLabel label, string text, Graphics g)
        {
            int digitWidth = MeasureGlyphWidth(label, g, "0");
            int offset = 0;

            foreach (char curChar in text)
            {
                offset += char.IsDigit(curChar)
                    ? digitWidth
                    : MeasureGlyphWidth(label, g, curChar.ToString());
            }

            return offset;
        }

        private static int MeasureGlyphWidth(SimpleLabel label, Graphics g, string text)
        {
            return TextRenderer.MeasureText(
                g,
                text,
                label.Font,
                new Size((int)(label.Width + 0.5f), (int)(label.Height + 0.5f)),
                TextFormatFlags.NoPadding).Width;
        }

        private static void DrawText(SimpleLabel label, string text, Graphics g, float x, float y, float width, float height, StringFormat format)
        {
            if (text == null)
            {
                return;
            }

            FancyTextResolvedEffects effects = FancyTextRuntime.GetCurrentEffects();
            bool overrideOutline = effects != null && effects.OverrideOutline;
            bool overrideShadow = effects != null && effects.OverrideShadow;
            bool hasGradient = effects != null && effects.HasGradient && !(label.Brush is LinearGradientBrush);

            bool hasShadow = overrideShadow ? effects.ShadowEnabled : label.HasShadow;
            Color shadowColor = overrideShadow ? effects.ShadowColor : label.ShadowColor;
            Color outlineColor = overrideOutline ? effects.OutlineColor : label.OutlineColor;
            float fontSize = GetFontSize(label, g);
            float outlineSize = overrideOutline
                ? Math.Max(0f, effects.OutlineSize)
                : GetDefaultOutlineSize(fontSize);

            bool usePath = overrideShadow
                || hasGradient
                || (g.TextRenderingHint == TextRenderingHint.AntiAlias && (outlineColor.A > 0 || overrideOutline));

            if (usePath)
            {
                using (GraphicsPath path = new GraphicsPath())
                {
                    if (hasShadow && shadowColor.A > 0)
                    {
                        if (overrideShadow)
                        {
                            DrawCustomShadowWithClip(label, text, g, fontSize, x, y, width, height, format, effects);
                        }
                        else
                        {
                            DrawDefaultPathShadow(label, text, g, fontSize, x, y, width, height, format, shadowColor);
                        }
                    }

                    path.AddString(text, label.Font.FontFamily, (int)label.Font.Style, fontSize, new RectangleF(x, y, width, height), format);

                    if (outlineColor.A > 0 && outlineSize > 0f)
                    {
                        using (Pen outline = new Pen(outlineColor, outlineSize) { LineJoin = LineJoin.Round })
                        {
                            g.DrawPath(outline, path);
                        }
                    }

                    using (Brush fill = CreateFillBrush(label, effects, x, y, width, height))
                    {
                        g.FillPath(fill, path);
                    }
                }

                return;
            }

            if (hasShadow && shadowColor.A > 0)
            {
                using (SolidBrush shadowBrush = new SolidBrush(shadowColor))
                {
                    g.DrawString(text, label.Font, shadowBrush, new RectangleF(x + 1f, y + 1f, width, height), format);
                    g.DrawString(text, label.Font, shadowBrush, new RectangleF(x + 2f, y + 2f, width, height), format);
                }
            }

            using (Brush fill = CreateFillBrush(label, effects, x, y, width, height))
            {
                g.DrawString(text, label.Font, fill, new RectangleF(x, y, width, height), format);
            }
        }

        private static void DrawDefaultPathShadow(SimpleLabel label, string text, Graphics g, float fontSize, float x, float y, float width, float height, StringFormat format, Color shadowColor)
        {
            using (SolidBrush shadowBrush = new SolidBrush(shadowColor))
            using (GraphicsPath shadowPath = new GraphicsPath())
            {
                shadowPath.AddString(text, label.Font.FontFamily, (int)label.Font.Style, fontSize, new RectangleF(x + 1f, y + 1f, width, height), format);
                g.FillPath(shadowBrush, shadowPath);
                shadowPath.Reset();
                shadowPath.AddString(text, label.Font.FontFamily, (int)label.Font.Style, fontSize, new RectangleF(x + 2f, y + 2f, width, height), format);
                g.FillPath(shadowBrush, shadowPath);
            }
        }

        private static void DrawCustomShadowWithClip(SimpleLabel label, string text, Graphics g, float fontSize, float x, float y, float width, float height, StringFormat format, FancyTextResolvedEffects effects)
        {
            GraphicsState saved = g.Save();
            try
            {
                g.ResetClip();
                if (effects.ShadowClipToRow)
                {
                    RectangleF rowClip = new RectangleF(-100000f, y, 200000f, Math.Max(1f, height));
                    g.SetClip(rowClip, CombineMode.Replace);
                }

                DrawCustomShadow(label, text, g, fontSize, x, y, width, height, format, effects);
            }
            finally
            {
                g.Restore(saved);
            }
        }

        private static void DrawCustomShadow(SimpleLabel label, string text, Graphics g, float fontSize, float x, float y, float width, float height, StringFormat format, FancyTextResolvedEffects effects)
        {
            if (effects.ShadowNormalEnabled)
                DrawCustomShadowLayer(label, text, g, fontSize, x, y, width, height, format, effects, FancyTextShadowMode.Behind);
            else if (effects.ShadowOutsideEnabled)
                DrawCustomShadowLayer(label, text, g, fontSize, x, y, width, height, format, effects, FancyTextShadowMode.OutsideOnly);
        }

        private static void DrawCustomShadowLayer(SimpleLabel label, string text, Graphics g, float fontSize, float x, float y, float width, float height, StringFormat format, FancyTextResolvedEffects effects, FancyTextShadowMode mode)
        {
            float offset = Math.Max(0f, effects.ShadowSize);
            float expansion = GetShadowExpansion(effects, fontSize);

            if (offset <= 0f && expansion <= 0.001f && effects.ShadowBlur <= 0f)
            {
                return;
            }

            int multiply = GetShadowMultiply(effects);
            bool useFixedBlur = effects.ShadowBlur > 0f;

            if (mode == FancyTextShadowMode.Behind)
            {
                using (GraphicsPath textPath = new GraphicsPath())
                using (GraphicsPath shadowPath = new GraphicsPath())
                using (SolidBrush shadowBrush = new SolidBrush(GetStackedShadowColor(effects.ShadowColor, 1f, multiply)))
                {
                    textPath.AddString(
                        text,
                        label.Font.FontFamily,
                        (int)label.Font.Style,
                        fontSize,
                        new RectangleF(x, y, width, height),
                        format);
                    BuildShadowPath(textPath, shadowPath, expansion, offset);

                    if (useFixedBlur)
                        DrawFixedBlurShadow(g, shadowPath, null, effects.ShadowColor, mode, multiply);
                    else
                        g.FillPath(shadowBrush, shadowPath);
                }
                return;
            }

            using (GraphicsPath textPath = new GraphicsPath())
            {
                textPath.AddString(text, label.Font.FontFamily, (int)label.Font.Style, fontSize, new RectangleF(x, y, width, height), format);

                using (GraphicsPath shadowPath = new GraphicsPath())
                using (SolidBrush shadowBrush = new SolidBrush(GetStackedShadowColor(effects.ShadowColor, 1f, multiply)))
                {
                    BuildShadowPath(textPath, shadowPath, expansion, offset);

                    if (useFixedBlur)
                        DrawFixedBlurShadow(g, shadowPath, textPath, effects.ShadowColor, mode, multiply);
                    else
                        FillShadowPath(g, shadowPath, textPath, shadowBrush, mode);
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
                            FillShadowPath(g, shiftedPath, textPath, brush, mode);
                        }
                    }
                }
            }
        }

        private static Color GetStackedShadowColor(Color color, float weight, int multiply)
        {
            double alpha = Math.Max(0d, Math.Min(1d, (color.A / 255d) * weight));
            if (multiply > 1)
                alpha = 1d - Math.Pow(1d - alpha, Math.Max(1, Math.Min(1000, multiply)));

            int a = Math.Max(0, Math.Min(255, (int)Math.Round(alpha * 255d)));
            return Color.FromArgb(a, color.R, color.G, color.B);
        }

        private static int GetShadowMultiply(FancyTextResolvedEffects effects)
        {
            if (effects == null)
                return 1;

            return Math.Max(1, Math.Min(1000, effects.ShadowMultiply));
        }

        private static float GetShadowExpansion(FancyTextResolvedEffects effects, float fontSize)
        {
            if (effects == null)
                return 0f;

            float percent = Math.Max(100f, Math.Min(500f, effects.ShadowSizePercent));
            return fontSize * ((percent - 100f) / 100f) * 0.3f;
        }

        private static void BuildShadowPath(GraphicsPath textPath, GraphicsPath shadowPath, float expansion, float offset)
        {
            shadowPath.Reset();
            shadowPath.FillMode = FillMode.Winding;
            shadowPath.AddPath(textPath, false);

            if (expansion > 0.001f)
            {
                try
                {
                    using (GraphicsPath expandedPath = (GraphicsPath)textPath.Clone())
                    using (Pen expansionPen = new Pen(Color.Black, expansion * 2f))
                    {
                        expandedPath.FillMode = FillMode.Winding;
                        expansionPen.LineJoin = LineJoin.Round;
                        expansionPen.StartCap = LineCap.Round;
                        expansionPen.EndCap = LineCap.Round;
                        expandedPath.Widen(expansionPen);
                        shadowPath.AddPath(expandedPath, false);
                        shadowPath.AddPath(textPath, false);
                    }
                }
                catch
                {
                    // Some symbol fonts can produce paths that Widen cannot process.
                    // In that case, keep the normal shadow rather than risking a draw crash.
                }
            }

            if (Math.Abs(offset) > 0.001f)
            {
                using (Matrix matrix = new Matrix())
                {
                    matrix.Translate(offset, offset);
                    shadowPath.Transform(matrix);
                }
            }
        }

        private static void FillShadowPath(Graphics g, GraphicsPath shadowPath, GraphicsPath textPath, Brush brush, FancyTextShadowMode mode)
        {
            if (mode == FancyTextShadowMode.Behind)
            {
                g.FillPath(brush, shadowPath);
                return;
            }

            using (var region = mode == FancyTextShadowMode.OutsideOnly ? new Region(shadowPath) : new Region(textPath))
            {
                if (mode == FancyTextShadowMode.OutsideOnly)
                    region.Exclude(textPath);
                else
                    region.Intersect(shadowPath);

                g.FillRegion(brush, region);
            }
        }

        private static Brush CreateFillBrush(SimpleLabel label, FancyTextResolvedEffects effects, float x, float y, float width, float height)
        {
            if (effects == null || !effects.HasGradient || label.Brush is LinearGradientBrush)
            {
                SolidBrush solid = label.Brush as SolidBrush;
                return solid != null ? new SolidBrush(solid.Color) : (Brush)label.Brush.Clone();
            }

            RectangleF rect = new RectangleF(x, y, Math.Max(1f, width), Math.Max(1f, height));
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

            Color middleColor = effects.UseExistingColorMiddle
                ? GetBaseTextColor(label)
                : effects.GradientColor2;

            var brush = new LinearGradientBrush(start, end, effects.GradientColor1, effects.GradientColor3);
            brush.InterpolationColors = new ColorBlend
            {
                Positions = new[] { 0f, 0.5f, 1f },
                Colors = new[] { effects.GradientColor1, middleColor, effects.GradientColor3 }
            };
            return brush;
        }

        private static Color GetBaseTextColor(SimpleLabel label)
        {
            SolidBrush solid = label.Brush as SolidBrush;
            return solid != null ? solid.Color : Color.White;
        }

        private static float GetDefaultOutlineSize(float fontSize)
        {
            return 2.1f + (fontSize * 0.055f);
        }

        private static float GetFontSize(SimpleLabel label, Graphics g)
        {
            return label.Font.Unit == GraphicsUnit.Point
                ? label.Font.Size * g.DpiY / 72f
                : label.Font.Size;
        }
    }
}
