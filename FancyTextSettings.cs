// ============================================================================
// FancyTextSettings.cs
// Settings panel for the FancyText component.
//
// All controls built in code; no .Designer.cs / .resx needed.
//
// Color pickers use SettingsHelper.ColorButtonClick (FlatStyle.Popup),
// matching the style used by standard LiveSplit components (same as SplitDetail).
//
// XML version history:
//   Version 1 - initial release. All properties below.
//
// Targeting note:
//   ScopeMode and TargetComponents are stored in XML and used by the runtime
//   hook to decide which other components receive FancyText effects.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.UI;

namespace LiveSplit.UI.Components
{
    public class FancyTextSettings : UserControl
    {
        // =====================================================================
        // Public properties  (read by FancyTextComponent every draw)
        // =====================================================================

        // Display
        public string              InstanceName        { get; private set; } = string.Empty;
        public string              DisplayText         { get { return InstanceName; } }
        public FancyTextAlignment  Alignment           { get; private set; } = FancyTextAlignment.Left;

        // Text color
        public bool                     OverrideTextColors  { get; private set; } = false;
        public FancyTextColorMode       ColorMode           { get; private set; } = FancyTextColorMode.Solid;
        public FancyTextGradientMode    GradientMode        { get; private set; } = FancyTextGradientMode.ExistingColors;
        public Color                    TextColor1          { get; private set; } = Color.White;
        public Color                    TextColor2          { get; private set; } = Color.FromArgb(255, 180, 180, 180);
        public Color                    TextColor3          { get; private set; } = Color.FromArgb(255, 40, 160, 200);
        public FancyTextGradientDirection GradientDirection { get; private set; } = FancyTextGradientDirection.Vertical;

        // Outline
        // When OverrideOutline is false, the component uses the layout's
        // TextOutlineColor and the standard size formula.
        public bool  OverrideOutline    { get; private set; } = false;
        public Color OutlineColor       { get; private set; } = Color.Black;
        // Absolute size in pixels.  0 = no outline.
        // Standard formula for reference: 2.1 + fontSize * 0.055, about 3-5px at typical sizes.
        public float OutlineSize        { get; private set; } = 3.0f;

        // Shadow
        // When OverrideShadow is false, the component uses the layout's
        // DropShadows flag and ShadowsColor.
        public bool  OverrideShadow     { get; private set; } = false;
        public bool  ShadowNormalEnabled { get; private set; } = true;
        public bool  ShadowOutsideEnabled { get; private set; } = false;
        public bool  ShadowEnabled      { get { return ShadowNormalEnabled || ShadowOutsideEnabled; } }
        public Color ShadowColor        { get; private set; } = Color.Black;
        // How many pixels the shadow shifts from the text.
        public float ShadowSize         { get; private set; } = 2.0f;
        // Percent expansion for the shadow glyphs. 100 = same size as the text.
        public int   ShadowSizePercent  { get; private set; } = 100;
        // Fixed low-cost blur. Stored as 0 or 1 for layout compatibility.
        public float ShadowBlur         { get; private set; } = 0f;
        public int   ShadowMultiply     { get; private set; } = 1;
        public bool  ShadowClipToRow    { get; private set; } = false;
        // Retained for old layouts. Blend was removed from the UI because it multiplies blur work.
        public int   ShadowBlend        { get; private set; } = 1;

        // Scope / targeting
        public FancyTextScopeMode    ScopeMode         { get; private set; } = FancyTextScopeMode.AllComponents;
        // Names of components selected when ScopeMode == SelectedComponents.
        // Populated from state.Layout.Components in the settings panel.
        public HashSet<string>       TargetComponents  { get; private set; } = new HashSet<string>();

        // =====================================================================
        // Private
        // =====================================================================
        private readonly LiveSplitState _state;
        private IComponent              _owner;
        private bool                    _refreshingComponentList;
        private Panel                   _scrollPanel;
        private Panel                   _contentPanel;
        private bool                    _resetScrollPosition;

        // Controls we need to reference after construction
        private TextBox       _textBox;
        private GroupBox      _targetsSection;
        private GroupBox      _textGradientSection;
        private GroupBox      _outlineSection;
        private GroupBox      _textShadowSection;
        private CheckBox      _overrideTextColorsChk;
        private ComboBox      _gradientModeCombo;
        private Button        _color1Btn;
        private Button        _color2Btn;
        private Button        _color3Btn;
        private Label         _color2Label;
        private Label         _color3Label;
        private Label         _existingColorLabel;
        private ComboBox      _gradDirCombo;
        private Label         _gradDirLabel;
        private CheckBox      _overrideOutlineChk;
        private Button        _outlineColorBtn;
        private Label         _outlineColorLabel;
        private NumericUpDown _outlineSizeNum;
        private CheckBox      _overrideShadowChk;
        private RadioButton   _shadowEnabledChk;
        private RadioButton   _shadowOutsideEnabledChk;
        private Button        _shadowColorBtn;
        private Label         _shadowColorLabel;
        private NumericUpDown _shadowSizeNum;
        private NumericUpDown _shadowSizePercentNum;
        private CheckBox      _shadowBlurChk;
        private NumericUpDown _shadowMultiplyNum;
        private CheckBox      _shadowClipToRowChk;

        // Target controls
        private RadioButton _rdoScopeThis;
        private RadioButton _rdoScopeAll;
        private RadioButton _rdoScopeSelected;
        private Button _refreshComponentsBtn;
        private CheckedListBox _componentList;

        // Constructor
        public FancyTextSettings(LiveSplitState state, IComponent owner)
        {
            _state = state;
            _owner = owner;
            BuildUI();
        }

        public void NotifyLayoutMode(LayoutMode mode)
        {
            RefreshRuntimeHooks();
            PopulateComponentList();
        }

        internal void AttachOwner(IComponent owner)
        {
            _owner = owner;
        }

        // =====================================================================
        // UI construction
        // =====================================================================

        private void BuildUI()
        {
            FancyTextSimpleLabelHook.Install();
            SuspendLayout();
            Controls.Clear();

            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = false;
            Dock = DockStyle.Fill;
            Padding = Padding.Empty;
            Size = new Size(476, 640);

            _scrollPanel = new Panel
            {
                AutoScroll = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(7),
            };

            _contentPanel = new Panel
            {
                Location = Point.Empty,
                Margin = Padding.Empty,
                AutoSize = false,
                Padding = Padding.Empty,
            };

            _scrollPanel.Controls.Add(_contentPanel);
            Controls.Add(_scrollPanel);

            AddSettingsSection(MakeSection("Instance", BuildInstanceSection()));
            _targetsSection = MakeSection("Targets", BuildScopeSection());
            _textGradientSection = MakeSection("Text Gradient", BuildColorSection());
            _outlineSection = MakeSection("Outline", BuildOutlineSection());
            _textShadowSection = MakeSection("Shadow", BuildShadowSection());
            AddSettingsSection(_targetsSection);
            AddSettingsSection(_textGradientSection);
            AddSettingsSection(_outlineSection);
            AddSettingsSection(_textShadowSection);

            Resize += (sender, args) => ResizeSections();
            _scrollPanel.Resize += (sender, args) => ResizeSections();
            _resetScrollPosition = true;
            ResizeSections();
            ResumeLayout(false);
            PerformLayout();
        }

        private Control BuildInstanceSection()
        {
            var row = MakeCompactRow();
            row.Controls.Add(MakeTinyLbl("Name"));
            _textBox = new TextBox { Text = InstanceName, MaxLength = 80, Width = 240, Margin = new Padding(0, 0, 0, 0) };
            _textBox.TextChanged += (s, e) => InstanceName = _textBox.Text;
            row.Controls.Add(_textBox);

            return row;
        }

        // Text color
        private Control BuildColorSection()
        {
            var panel = MakeCompactPanel();
            var row1 = MakeCompactRow();

            _overrideTextColorsChk = MakeCompactCheck("Activate", OverrideTextColors);
            _overrideTextColorsChk.CheckedChanged += (s, e) =>
            {
                OverrideTextColors = _overrideTextColorsChk.Checked;
                ColorMode = OverrideTextColors ? FancyTextColorMode.Gradient : FancyTextColorMode.Solid;
                UpdateGradientControlStates();
            };
            row1.Controls.Add(_overrideTextColorsChk);

            _gradientModeCombo = MakeCompactCombo("Gradient with existing colours", "New Gradient");
            _gradientModeCombo.Width = 205;
            _gradientModeCombo.SelectedIndex = (int)GradientMode;
            _gradientModeCombo.SelectedIndexChanged += (s, e) =>
            {
                GradientMode = (FancyTextGradientMode)_gradientModeCombo.SelectedIndex;
                UpdateGradientControlStates();
            };
            AddCompactControl(row1, "Mode", _gradientModeCombo);
            panel.Controls.Add(row1);

            var row2 = MakeCompactRow();
            _color1Btn = MakeColorBtn(TextColor1);
            _color1Btn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_color1Btn, this);
                TextColor1 = _color1Btn.BackColor;
            };
            AddCompactControl(row2, "1", _color1Btn);

            _color2Label = MakeTinyLbl("2");
            row2.Controls.Add(_color2Label);
            _color2Btn = MakeColorBtn(TextColor2);
            _color2Btn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_color2Btn, this);
                TextColor2 = _color2Btn.BackColor;
            };
            _existingColorLabel = new Label
            {
                Text = "Existing color",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0),
            };
            var middleColorPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            middleColorPanel.Controls.Add(_color2Btn);
            middleColorPanel.Controls.Add(_existingColorLabel);
            row2.Controls.Add(middleColorPanel);

            _color3Label = MakeTinyLbl("3");
            row2.Controls.Add(_color3Label);
            _color3Btn = MakeColorBtn(TextColor3);
            _color3Btn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_color3Btn, this);
                TextColor3 = _color3Btn.BackColor;
            };
            row2.Controls.Add(_color3Btn);

            _gradDirLabel = MakeTinyLbl("Dir");
            row2.Controls.Add(_gradDirLabel);
            _gradDirCombo = MakeCompactCombo("Horizontal", "Vertical", "Diagonal Down", "Diagonal Up");
            _gradDirCombo.SelectedIndex = (int)GradientDirection;
            _gradDirCombo.SelectedIndexChanged += (s, e) =>
                GradientDirection = (FancyTextGradientDirection)_gradDirCombo.SelectedIndex;
            row2.Controls.Add(_gradDirCombo);
            panel.Controls.Add(row2);

            UpdateGradientControlStates();
            return panel;
        }

        private void UpdateGradientControlStates()
        {
            bool on = OverrideTextColors;
            bool newGradient = GradientMode == FancyTextGradientMode.NewGradient;
            if (_gradientModeCombo != null) _gradientModeCombo.Enabled = on;
            if (_color1Btn != null) _color1Btn.Enabled = on;
            if (_color2Btn != null)
            {
                _color2Btn.Enabled = on && newGradient;
                _color2Btn.Visible = newGradient;
            }
            if (_existingColorLabel != null)
            {
                _existingColorLabel.Enabled = on;
                _existingColorLabel.Visible = !newGradient;
            }
            if (_color3Btn != null) _color3Btn.Enabled = on;
            if (_color2Label != null) _color2Label.Enabled = on;
            if (_color3Label != null) _color3Label.Enabled = on;
            if (_gradDirCombo != null) _gradDirCombo.Enabled = on;
            if (_gradDirLabel != null) _gradDirLabel.Enabled = on;
        }

        // Outline
        private Control BuildOutlineSection()
        {
            var row = MakeCompactRow();

            _overrideOutlineChk = MakeCompactCheck("Activate", OverrideOutline);
            _overrideOutlineChk.CheckedChanged += (s, e) =>
            {
                OverrideOutline = _overrideOutlineChk.Checked;
                UpdateOutlineControlStates();
            };
            row.Controls.Add(_overrideOutlineChk);

            _outlineColorLabel = MakeTinyLbl("Color");
            row.Controls.Add(_outlineColorLabel);
            _outlineColorBtn = MakeColorBtn(OutlineColor);
            _outlineColorBtn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_outlineColorBtn, this);
                OutlineColor = _outlineColorBtn.BackColor;
            };
            row.Controls.Add(_outlineColorBtn);

            _outlineSizeNum = MakeCompactNumeric(OutlineSize, 0f, 20f, 1, 0.5m);
            _outlineSizeNum.ValueChanged += (s, e) => OutlineSize = (float)_outlineSizeNum.Value;
            AddCompactControl(row, "Size", _outlineSizeNum);

            UpdateOutlineControlStates();
            return row;
        }

        private void UpdateOutlineControlStates()
        {
            bool ov = OverrideOutline;
            if (_outlineColorBtn   != null) _outlineColorBtn.Enabled   = ov;
            if (_outlineColorLabel != null) _outlineColorLabel.Enabled = ov;
            if (_outlineSizeNum    != null) _outlineSizeNum.Enabled    = ov;
        }

        // Shadow
        private Control BuildShadowSection()
        {
            var panel = MakeCompactPanel();

            var topRow = MakeCompactRow();
            _overrideShadowChk = MakeCompactCheck("Custom shadow", OverrideShadow);
            _overrideShadowChk.CheckedChanged += (s, e) =>
            {
                OverrideShadow = _overrideShadowChk.Checked;
                UpdateShadowControlStates();
            };
            topRow.Controls.Add(_overrideShadowChk);

            _shadowColorLabel = MakeTinyLbl("Color");
            topRow.Controls.Add(_shadowColorLabel);
            _shadowColorBtn = MakeColorBtn(ShadowColor);
            _shadowColorBtn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_shadowColorBtn, this);
                ShadowColor = _shadowColorBtn.BackColor;
            };
            topRow.Controls.Add(_shadowColorBtn);

            topRow.Controls.Add(MakeTinyLbl("Mode"));
            _shadowEnabledChk = MakeCompactRadio("Normal", ShadowNormalEnabled);
            _shadowEnabledChk.CheckedChanged += (s, e) =>
            {
                if (_shadowEnabledChk.Checked)
                {
                    ShadowNormalEnabled = true;
                    ShadowOutsideEnabled = false;
                    UpdateShadowControlStates();
                }
            };
            topRow.Controls.Add(_shadowEnabledChk);

            _shadowOutsideEnabledChk = MakeCompactRadio("Outside Only", ShadowOutsideEnabled);
            _shadowOutsideEnabledChk.CheckedChanged += (s, e) =>
            {
                if (_shadowOutsideEnabledChk.Checked)
                {
                    ShadowNormalEnabled = false;
                    ShadowOutsideEnabled = true;
                    UpdateShadowControlStates();
                }
            };
            topRow.Controls.Add(_shadowOutsideEnabledChk);

            _shadowBlurChk = MakeCompactCheck("Blur", ShadowBlur > 0f);
            _shadowBlurChk.CheckedChanged += (s, e) =>
                ShadowBlur = _shadowBlurChk.Checked ? 1f : 0f;
            topRow.Controls.Add(_shadowBlurChk);
            panel.Controls.Add(topRow);

            var valueRow = MakeCompactRow();
            _shadowSizeNum = MakeCompactNumeric(ShadowSize, 0f, 20f, 1, 0.5m);
            _shadowSizeNum.ValueChanged += (s, e) => ShadowSize = (float)_shadowSizeNum.Value;
            AddCompactControl(valueRow, "Offset", _shadowSizeNum);

            _shadowSizePercentNum = MakeCompactNumeric(ShadowSizePercent, 100f, 500f, 0, 1m);
            _shadowSizePercentNum.ValueChanged += (s, e) => ShadowSizePercent = (int)_shadowSizePercentNum.Value;
            AddCompactControl(valueRow, "Size", _shadowSizePercentNum);

            _shadowMultiplyNum = MakeCompactNumeric(ShadowMultiply, 1f, 1000f, 0, 1m);
            _shadowMultiplyNum.ValueChanged += (s, e) => ShadowMultiply = (int)_shadowMultiplyNum.Value;
            AddCompactControl(valueRow, "Strength", _shadowMultiplyNum);

            _shadowClipToRowChk = MakeCompactCheck("Clip row", ShadowClipToRow);
            _shadowClipToRowChk.CheckedChanged += (s, e) => ShadowClipToRow = _shadowClipToRowChk.Checked;
            valueRow.Controls.Add(_shadowClipToRowChk);

            panel.Controls.Add(valueRow);

            UpdateShadowControlStates();
            return panel;
        }

        private void UpdateShadowControlStates()
        {
            bool ov = OverrideShadow;
            bool any = ov && ShadowEnabled;
            if (_shadowEnabledChk  != null) _shadowEnabledChk.Enabled  = ov;
            if (_shadowOutsideEnabledChk != null) _shadowOutsideEnabledChk.Enabled = ov;
            if (_shadowColorBtn    != null) _shadowColorBtn.Enabled    = ov;
            if (_shadowColorLabel  != null) _shadowColorLabel.Enabled  = ov;
            if (_shadowSizeNum     != null) _shadowSizeNum.Enabled     = any;
            if (_shadowSizePercentNum != null) _shadowSizePercentNum.Enabled = any;
            if (_shadowBlurChk    != null) _shadowBlurChk.Enabled    = any;
            if (_shadowMultiplyNum != null) _shadowMultiplyNum.Enabled = any;
            if (_shadowClipToRowChk != null) _shadowClipToRowChk.Enabled = any;
        }

        // Scope / targeting
        private Control BuildScopeSection()
        {
            var panel = MakeCompactPanel();

            _rdoScopeThis     = new RadioButton { Text = "Disabled",            AutoSize = true, Checked = true };
            _rdoScopeAll      = new RadioButton { Text = "All Components",      AutoSize = true };
            _rdoScopeSelected = new RadioButton { Text = "Selected Components", AutoSize = true };

            // Restore persisted scope mode
            switch (ScopeMode)
            {
                case FancyTextScopeMode.AllComponents:
                    _rdoScopeAll.Checked  = true; break;
                case FancyTextScopeMode.SelectedComponents:
                    _rdoScopeSelected.Checked = true; break;
                default:
                    _rdoScopeThis.Checked = true; break;
            }

            _rdoScopeThis.CheckedChanged     += (s, e) => { if (_rdoScopeThis.Checked)     UpdateScopeMode(); };
            _rdoScopeAll.CheckedChanged      += (s, e) => { if (_rdoScopeAll.Checked)      UpdateScopeMode(); };
            _rdoScopeSelected.CheckedChanged += (s, e) => { if (_rdoScopeSelected.Checked) UpdateScopeMode(); };

            var modeRow = MakeCompactRow();
            modeRow.Controls.Add(_rdoScopeThis);
            modeRow.Controls.Add(_rdoScopeAll);
            modeRow.Controls.Add(_rdoScopeSelected);

            _refreshComponentsBtn = new Button
            {
                Text = "Refresh",
                AutoSize = true,
                Margin = new Padding(10, 0, 0, 0),
            };
            _refreshComponentsBtn.Click += (s, e) => RefreshComponentTargets();
            modeRow.Controls.Add(_refreshComponentsBtn);
            panel.Controls.Add(modeRow);

            // Component checklist (visible when SelectedComponents)
            _componentList = new CheckedListBox
            {
                Height       = 182,
                Width        = 420,
                CheckOnClick = true,
                Enabled      = true,
                Visible      = true,
                IntegralHeight = false,
                Margin = new Padding(0, 2, 0, 0),
            };
            _componentList.ItemCheck += ComponentList_ItemCheck;
            _componentList.MouseEnter += (s, e) => _componentList.Focus();
            _componentList.MouseDown += (s, e) => _componentList.Focus();
            _componentList.MouseWheel += ComponentList_MouseWheel;
            PopulateComponentList();
            panel.Controls.Add(_componentList);

            UpdateScopeControlStates();
            return panel;
        }

        private void UpdateScopeMode()
        {
            if (_rdoScopeThis.Checked)          ScopeMode = FancyTextScopeMode.ThisComponentOnly;
            else if (_rdoScopeAll.Checked)      ScopeMode = FancyTextScopeMode.AllComponents;
            else if (_rdoScopeSelected.Checked) ScopeMode = FancyTextScopeMode.SelectedComponents;
            RefreshRuntimeHooks();
            UpdateScopeControlStates();
        }

        private void UpdateScopeControlStates()
        {
            if (_componentList != null)
                _componentList.Enabled = true;
        }

        /// <summary>
        /// Fills the component checklist from state.Layout.Components.
        /// Called once at construction, whenever settings opens, and when refreshed.
        /// </summary>
        private void PopulateComponentList()
        {
            if (_componentList == null) return;
            if (_refreshingComponentList) return;
            _refreshingComponentList = true;
            _componentList.Items.Clear();

            try
            {
                foreach (FancyTextTargetInfo target in FancyTextRuntime.GetTargets(_state, _owner))
                {
                    bool isChecked = TargetComponents.Contains(target.Key)
                        || TargetComponents.Contains(target.ComponentName);
                    _componentList.Items.Add(target, isChecked);
                }

            }
            catch
            {
                // Layout or components not yet available.
            }
            finally
            {
                _refreshingComponentList = false;
            }
        }

        private void ComponentList_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_refreshingComponentList || e.Index < 0 || e.Index >= _componentList.Items.Count)
                return;

            ScopeMode = FancyTextScopeMode.SelectedComponents;
            if (_rdoScopeSelected != null)
                _rdoScopeSelected.Checked = true;

            var target = _componentList.Items[e.Index] as FancyTextTargetInfo;
            if (target == null)
                return;

            TargetComponents.Remove(target.ComponentName);
            if (e.NewValue == CheckState.Checked)
                TargetComponents.Add(target.Key);
            else
                TargetComponents.Remove(target.Key);

            RefreshRuntimeHooks();
        }

        private void RefreshComponentTargets()
        {
            var owner = _owner as FancyTextComponent;
            if (owner != null)
            {
                FancyTextComponent refreshedOwner = FancyTextRuntime.RecreateController(_state, owner, this);
                if (refreshedOwner != null)
                {
                    _owner = refreshedOwner;
                }
            }
            else
            {
                RefreshRuntimeHooks();
            }

            PopulateComponentList();
            UpdateScopeControlStates();
        }

        private void RefreshRuntimeHooks()
        {
            FancyTextRuntime.InstallHooks(_state);

            var owner = _owner as FancyTextComponent;
            if (owner != null)
                FancyTextRuntime.Publish(owner, _state, this);

            Form form = _state != null ? _state.Form : null;
            if (form != null && !form.IsDisposed)
                form.Invalidate();
        }

        private void ComponentList_MouseWheel(object sender, MouseEventArgs e)
        {
            if (_componentList == null || _componentList.Items.Count == 0)
                return;

            int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            int direction = e.Delta > 0 ? -1 : 1;
            int next = _componentList.TopIndex + (direction * lines);
            next = Math.Max(0, Math.Min(_componentList.Items.Count - 1, next));
            _componentList.TopIndex = next;
        }

        // =====================================================================
        // XML persistence
        // =====================================================================

        public XmlNode GetSettings(XmlDocument document)
        {
            XmlElement root = document.CreateElement("Settings");
            W(document, root, "Version",            "1");
            W(document, root, "InstanceName",       InstanceName);
            W(document, root, "DisplayText",        InstanceName);
            W(document, root, "Alignment",          Alignment.ToString());
            W(document, root, "ColorMode",          ColorMode.ToString());
            W(document, root, "OverrideTextColors", OverrideTextColors.ToString());
            W(document, root, "GradientMode",       GradientMode.ToString());
            W(document, root, "TextColor1",         ColorToHex(TextColor1));
            W(document, root, "TextColor2",         ColorToHex(TextColor2));
            W(document, root, "TextColor3",         ColorToHex(TextColor3));
            W(document, root, "GradientDirection",  GradientDirection.ToString());
            W(document, root, "OverrideOutline",    OverrideOutline.ToString());
            W(document, root, "OutlineColor",       ColorToHex(OutlineColor));
            W(document, root, "OutlineSize",        OutlineSize.ToString(CultureInfo.InvariantCulture));
            W(document, root, "OverrideShadow",     OverrideShadow.ToString());
            W(document, root, "ShadowEnabled",      ShadowEnabled.ToString());
            W(document, root, "ShadowNormalEnabled", ShadowNormalEnabled.ToString());
            W(document, root, "ShadowOutsideEnabled", ShadowOutsideEnabled.ToString());
            W(document, root, "ShadowColor",        ColorToHex(ShadowColor));
            W(document, root, "ShadowSize",         ShadowSize.ToString(CultureInfo.InvariantCulture));
            W(document, root, "ShadowSizePercent",  ShadowSizePercent.ToString(CultureInfo.InvariantCulture));
            W(document, root, "ShadowBlur",         ShadowBlur.ToString(CultureInfo.InvariantCulture));
            W(document, root, "ShadowMultiply",     ShadowMultiply.ToString(CultureInfo.InvariantCulture));
            W(document, root, "ShadowClipToRow",    ShadowClipToRow.ToString());
            W(document, root, "ShadowBlend",        "1");
            W(document, root, "ScopeMode",          ScopeMode.ToString());
            W(document, root, "TargetComponents",   string.Join("|", TargetComponents));
            return root;
        }

        public void SetSettings(XmlNode node)
        {
            if (node == null) return;

            string t = R(node, "InstanceName") ?? R(node, "DisplayText");
            if (t != null) { InstanceName = t; if (_textBox != null) _textBox.Text = t; }

            if (TryParseEnum(R(node, "Alignment"), out FancyTextAlignment align))
                Alignment = align;

            if (TryParseEnum(R(node, "ColorMode"), out FancyTextColorMode cm))
                ColorMode = cm;
            string overrideTextColors = R(node, "OverrideTextColors");
            if (overrideTextColors != null)
                ParseBool(overrideTextColors, b => OverrideTextColors = b);
            else
                OverrideTextColors = ColorMode == FancyTextColorMode.Gradient;
            ColorMode = OverrideTextColors ? FancyTextColorMode.Gradient : FancyTextColorMode.Solid;

            if (TryParseEnum(R(node, "GradientMode"), out FancyTextGradientMode gm))
                GradientMode = gm;
            if (overrideTextColors == null && ColorMode == FancyTextColorMode.Gradient)
                GradientMode = FancyTextGradientMode.NewGradient;

            if (_overrideTextColorsChk != null) _overrideTextColorsChk.Checked = OverrideTextColors;
            if (_gradientModeCombo != null) _gradientModeCombo.SelectedIndex = (int)GradientMode;

            SetColorBtn(R(node, "TextColor1"),  ref _color1Btn,        c => TextColor1 = c);
            SetColorBtn(R(node, "TextColor2"),  ref _color2Btn,        c => TextColor2 = c);
            SetColorBtn(R(node, "TextColor3"),  ref _color3Btn,        c => TextColor3 = c);

            if (TryParseEnum(R(node, "GradientDirection"), out FancyTextGradientDirection gd))
                GradientDirection = gd;
            if (_gradDirCombo != null) _gradDirCombo.SelectedIndex = (int)GradientDirection;
            UpdateGradientControlStates();

            ParseBool(R(node, "OverrideOutline"),  b => { OverrideOutline = b; if (_overrideOutlineChk != null) _overrideOutlineChk.Checked = b; });
            SetColorBtn(R(node, "OutlineColor"),  ref _outlineColorBtn, c => OutlineColor = c);
            ParseFloat(R(node, "OutlineSize"),    f => { OutlineSize = f;    if (_outlineSizeNum    != null) _outlineSizeNum.Value    = (decimal)Math.Min(20f, f); });
            UpdateOutlineControlStates();

            ParseBool(R(node, "OverrideShadow"),   b => { OverrideShadow  = b; if (_overrideShadowChk  != null) _overrideShadowChk.Checked  = b; });
            string legacyShadowEnabled = R(node, "ShadowEnabled");
            if (R(node, "ShadowNormalEnabled") != null)
                ParseBool(R(node, "ShadowNormalEnabled"), b => ShadowNormalEnabled = b);
            else
                ParseBool(legacyShadowEnabled, b => ShadowNormalEnabled = b);
            ParseBool(R(node, "ShadowOutsideEnabled"), b => ShadowOutsideEnabled = b);
            if (ShadowOutsideEnabled)
                ShadowNormalEnabled = false;
            if (!ShadowNormalEnabled && !ShadowOutsideEnabled)
                ShadowNormalEnabled = true;
            if (_shadowEnabledChk != null) _shadowEnabledChk.Checked = ShadowNormalEnabled;
            if (_shadowOutsideEnabledChk != null) _shadowOutsideEnabledChk.Checked = ShadowOutsideEnabled;
            SetColorBtn(R(node, "ShadowColor"),   ref _shadowColorBtn,  c => ShadowColor = c);
            ParseFloat(R(node, "ShadowSize"),     f => { ShadowSize     = f;   if (_shadowSizeNum     != null) _shadowSizeNum.Value     = (decimal)Math.Min(20f, f); });
            ParseInt(R(node, "ShadowSizePercent"), i =>
            {
                ShadowSizePercent = Math.Max(100, Math.Min(500, i));
                SetNumericValue(_shadowSizePercentNum, ShadowSizePercent);
            });
            string shadowBlur = R(node, "ShadowBlur") ?? R(node, "ShadowSoftness");
            ParseFloat(shadowBlur, f =>
            {
                ShadowBlur = f > 0f ? 1f : 0f;
                if (_shadowBlurChk != null) _shadowBlurChk.Checked = ShadowBlur > 0f;
            });
            string shadowMultiply = R(node, "ShadowMultiply") ?? R(node, "ShadowStrength");
            ParseInt(shadowMultiply, i => { ShadowMultiply = Math.Max(1, Math.Min(1000, i)); SetNumericValue(_shadowMultiplyNum, ShadowMultiply); });
            ParseBool(R(node, "ShadowClipToRow"), b => { ShadowClipToRow = b; if (_shadowClipToRowChk != null) _shadowClipToRowChk.Checked = b; });
            ShadowBlend = 1;
            UpdateShadowControlStates();

            if (TryParseEnum(R(node, "ScopeMode"), out FancyTextScopeMode scope))
                ScopeMode = scope;
            if (_rdoScopeThis     != null) _rdoScopeThis.Checked     = ScopeMode == FancyTextScopeMode.ThisComponentOnly;
            if (_rdoScopeAll      != null) _rdoScopeAll.Checked      = ScopeMode == FancyTextScopeMode.AllComponents;
            if (_rdoScopeSelected != null) _rdoScopeSelected.Checked = ScopeMode == FancyTextScopeMode.SelectedComponents;

            string targets = R(node, "TargetComponents");
            TargetComponents.Clear();
            if (!string.IsNullOrEmpty(targets))
                foreach (string name in targets.Split('|'))
                    if (!string.IsNullOrWhiteSpace(name))
                        TargetComponents.Add(name);

            PopulateComponentList();
            UpdateScopeControlStates();
            ResizeSections();
        }

        // =====================================================================
        // UI helpers
        // =====================================================================

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);

            if (Parent != null)
                QueueScrollReset();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            if (Visible)
                QueueScrollReset();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (Visible)
                QueueScrollReset();
        }

        public void PrepareForDisplay()
        {
            QueueScrollReset();
        }

        private void QueueScrollReset()
        {
            _resetScrollPosition = true;

            if (!IsHandleCreated || IsDisposed)
                return;

            BeginInvoke(new Action(() =>
            {
                if (IsDisposed)
                    return;

                _resetScrollPosition = true;
                ResizeSections();
            }));
        }

        private void AddSettingsSection(Control section)
        {
            section.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            section.TabIndex = _contentPanel.Controls.Count;
            _contentPanel.Controls.Add(section);
        }

        private void ResizeSections()
        {
            if (_scrollPanel == null || _contentPanel == null)
                return;

            int previousScrollY = _resetScrollPosition ? 0 : -_scrollPanel.AutoScrollPosition.Y;

            _scrollPanel.SuspendLayout();
            _contentPanel.SuspendLayout();

            _scrollPanel.AutoScrollPosition = Point.Empty;

            int width = Math.Max(320, _scrollPanel.ClientSize.Width - _scrollPanel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
            int y = 0;
            _contentPanel.Location = new Point(_scrollPanel.Padding.Left, _scrollPanel.Padding.Top);
            _contentPanel.Width = width;

            foreach (Control control in _contentPanel.Controls)
            {
                if (!control.Visible)
                {
                    control.Location = new Point(0, y);
                    continue;
                }

                control.Location = new Point(0, y);
                control.Width = width;
                if (control is GroupBox group && group.Controls.Count > 0)
                {
                    Control content = group.Controls[0];
                    int contentWidth = Math.Max(240, width - 18);
                    Size preferred = content.GetPreferredSize(new Size(contentWidth, 0));
                    content.Width = contentWidth;
                    content.Height = preferred.Height;
                    group.Height = Math.Max(48, content.Height + 30);
                }

                y = control.Bottom + control.Margin.Bottom;
            }

            _contentPanel.Height = y;
            _scrollPanel.AutoScrollMinSize = new Size(0, y + _scrollPanel.Padding.Vertical);

            int maxScrollY = Math.Max(0, y + _scrollPanel.Padding.Vertical - _scrollPanel.ClientSize.Height);
            if (previousScrollY > maxScrollY)
                previousScrollY = maxScrollY;
            if (previousScrollY > 0)
                _scrollPanel.AutoScrollPosition = new Point(0, previousScrollY);
            else
                _scrollPanel.AutoScrollPosition = Point.Empty;

            _resetScrollPosition = false;

            _contentPanel.ResumeLayout(true);
            _scrollPanel.ResumeLayout(true);
        }

        private static GroupBox MakeSection(string title, Control content)
        {
            int sectionWidth = 440;
            int contentWidth = sectionWidth - 18;
            Size preferred = content.GetPreferredSize(new Size(contentWidth, 0));
            content.Location = new Point(8, 19);
            content.Size = new Size(contentWidth, preferred.Height);

            var gb = new GroupBox
            {
                Text     = title,
                Margin   = new Padding(0, 0, 0, 6),
                Padding  = new Padding(6),
                Size     = new Size(sectionWidth, Math.Max(48, preferred.Height + 30)),
            };
            gb.Controls.Add(content);
            return gb;
        }

        private static FlowLayoutPanel MakeCompactPanel()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
        }

        private static FlowLayoutPanel MakeCompactRow()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 2),
                Padding = Padding.Empty,
            };
        }

        private static CheckBox MakeCompactCheck(string text, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                Checked = isChecked,
                AutoSize = true,
                Margin = new Padding(0, 3, 8, 0),
            };
        }

        private static RadioButton MakeCompactRadio(string text, bool isChecked)
        {
            return new RadioButton
            {
                Text = text,
                Checked = isChecked,
                AutoSize = true,
                Margin = new Padding(0, 3, 8, 0),
            };
        }

        private static Label MakeTinyLbl(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 4, 2, 0),
            };
        }

        private static NumericUpDown MakeCompactNumeric(float value, float min, float max, int decimals, decimal increment)
        {
            NumericUpDown control = MakeNumeric(value, min, max, decimals, increment);
            control.Width = 54;
            control.Margin = new Padding(0, 0, 6, 0);
            return control;
        }

        private static ComboBox MakeCompactCombo(params string[] items)
        {
            ComboBox control = MakeCombo(items);
            control.Dock = DockStyle.None;
            control.Width = 118;
            control.Margin = new Padding(0, 0, 6, 0);
            return control;
        }

        private static void AddCompactControl(FlowLayoutPanel row, string label, Control control)
        {
            row.Controls.Add(MakeTinyLbl(label));
            control.Margin = control is Button
                ? new Padding(0, 1, 8, 0)
                : new Padding(0, 0, 8, 0);
            row.Controls.Add(control);
        }

        private static TableLayoutPanel MakeGrid(int rows)
        {
            var t = new TableLayoutPanel
            {
                AutoSize    = true,
                ColumnCount = 2,
                RowCount    = rows,
                Padding     = Padding.Empty,
                Margin      = Padding.Empty,
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  100f));
            return t;
        }

        private static Label MakeLbl(string text) => new Label
        {
            Text      = text,
            AutoSize  = true,
            Anchor    = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        private static ComboBox MakeCombo(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            c.Items.AddRange(items);
            return c;
        }

        private static Button MakeColorBtn(Color initial) => new Button
        {
            BackColor               = initial,
            FlatStyle               = FlatStyle.Popup,
            UseVisualStyleBackColor = false,
            Width  = 23,
            Height = 23,
        };

        private static NumericUpDown MakeNumeric(float value, float min, float max,
                                                  int decimals, decimal increment)
        {
            return new NumericUpDown
            {
                Minimum       = (decimal)min,
                Maximum       = (decimal)max,
                DecimalPlaces = decimals,
                Increment     = increment,
                Value         = (decimal)Math.Max(min, Math.Min(max, value)),
                Width         = 70,
            };
        }

        private static void SetNumericValue(NumericUpDown control, float value)
        {
            if (control == null)
                return;

            decimal next = (decimal)value;
            if (next < control.Minimum)
                next = control.Minimum;
            if (next > control.Maximum)
                next = control.Maximum;

            if (control.Value != next)
                control.Value = next;
        }

        // =====================================================================
        // XML helpers
        // =====================================================================

        private static void W(XmlDocument doc, XmlElement parent, string name, string value)
        {
            var el = doc.CreateElement(name);
            el.InnerText = value ?? string.Empty;
            parent.AppendChild(el);
        }

        private static string R(XmlNode parent, string childName)
        {
            string s = parent[childName]?.InnerText;
            return string.IsNullOrEmpty(s) ? null : s;
        }

        private static string ColorToHex(Color c)  => c.ToArgb().ToString("X8");
        private static Color  HexToColor(string s)
        {
            try   { return Color.FromArgb(Convert.ToInt32(s, 16)); }
            catch { return Color.White; }
        }

        private static void ParseBool(string s, Action<bool> apply)
        {
            if (bool.TryParse(s, out bool b)) apply(b);
        }

        private static void ParseFloat(string s, Action<float> apply)
        {
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                apply(f);
        }

        private static void ParseInt(string s, Action<int> apply)
        {
            if (int.TryParse(s, out int i)) apply(i);
        }

        private static void ParseEnum<T>(string s, out T result) where T : struct
        {
            result = default(T);
            if (!string.IsNullOrEmpty(s))
                Enum.TryParse(s, out result);
        }

        private static bool TryParseEnum<T>(string s, out T result) where T : struct
        {
            result = default(T);
            return !string.IsNullOrEmpty(s) && Enum.TryParse(s, out result);
        }

        private void SetColorBtn(string hex, ref Button btn, Action<Color> setProperty)
        {
            if (string.IsNullOrEmpty(hex)) return;
            Color c = HexToColor(hex);
            setProperty(c);
            if (btn != null) btn.BackColor = c;
        }
    }
}
