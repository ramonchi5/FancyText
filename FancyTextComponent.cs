using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.UI;

namespace LiveSplit.UI.Components
{
    public enum FancyTextColorMode
    {
        Solid,
        Gradient
    }

    public enum FancyTextGradientMode
    {
        ExistingColors,
        NewGradient
    }

    public enum FancyTextGradientDirection
    {
        Horizontal,
        Vertical,
        DiagonalDown,
        DiagonalUp
    }

    public enum FancyTextAlignment
    {
        Left,
        Center,
        Right
    }

    public enum FancyTextScopeMode
    {
        ThisComponentOnly,
        AllComponents,
        SelectedComponents
    }

    public enum FancyTextShadowMode
    {
        Behind,
        OutsideOnly
    }

    public class FancyTextComponent : IComponent
    {
        private readonly FancyTextSettings _settings;

        public FancyTextComponent(LiveSplitState state)
        {
            _settings = new FancyTextSettings(state, this);
            FancyTextRuntime.InstallHooks(state);
        }

        internal FancyTextComponent(LiveSplitState state, FancyTextSettings settings)
        {
            _settings = settings ?? new FancyTextSettings(state, this);
            _settings.AttachOwner(this);
            FancyTextRuntime.InstallHooks(state);
        }

        public string ComponentName
        {
            get
            {
                string name = _settings.InstanceName;
                return string.IsNullOrWhiteSpace(name)
                    ? "Fancy Text"
                    : "Fancy Text - " + name;
            }
        }

        // FancyText is a controller. It should not consume layout space.
        public float VerticalHeight { get { return 0f; } }
        public float MinimumHeight { get { return 0f; } }
        public float HorizontalWidth { get { return 0f; } }
        public float MinimumWidth { get { return 0f; } }
        public float PaddingTop { get { return 0f; } }
        public float PaddingBottom { get { return 0f; } }
        public float PaddingLeft { get { return 0f; } }
        public float PaddingRight { get { return 0f; } }

        public IDictionary<string, Action> ContextMenuControls { get { return null; } }

        public Control GetSettingsControl(LayoutMode mode)
        {
            _settings.NotifyLayoutMode(mode);
            return _settings;
        }

        public XmlNode GetSettings(XmlDocument document)
        {
            return _settings.GetSettings(document);
        }

        public void SetSettings(XmlNode settings)
        {
            _settings.SetSettings(settings);
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
            FancyTextRuntime.InstallHooks(state);
            FancyTextRuntime.Publish(this, state, _settings);
            invalidator?.Invalidate(0, 0, width, height);
        }

        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
        {
            DrawControllerLayer(g, state);
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
        {
            DrawControllerLayer(g, state);
        }

        public void Dispose()
        {
            FancyTextRuntime.Unpublish(this);
        }

        private void DrawControllerLayer(Graphics g, LiveSplitState state)
        {
            FancyTextRuntime.InstallHooks(state);
            FancyTextRuntime.Publish(this, state, _settings);
        }
    }
}
