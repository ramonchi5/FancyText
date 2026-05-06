using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.UI;

namespace LiveSplit.UI.Components
{
    internal sealed class FancyTextTargetInfo
    {
        public string Key { get; set; }
        public string ComponentName { get; set; }
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public int LayoutIndex { get; set; }
        public IComponent Component { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public sealed class FancyTextResolvedEffects
    {
        public bool OverrideOutline { get; internal set; }
        public Color OutlineColor { get; internal set; }
        public float OutlineSize { get; internal set; }

        public bool OverrideShadow { get; internal set; }
        public bool ShadowEnabled { get; internal set; }
        public bool ShadowNormalEnabled { get; internal set; }
        public bool ShadowOutsideEnabled { get; internal set; }
        public Color ShadowColor { get; internal set; }
        public float ShadowSize { get; internal set; }
        public int ShadowSizePercent { get; internal set; }
        public float ShadowBlur { get; internal set; }
        public int ShadowMultiply { get; internal set; }
        public bool ShadowClipToRow { get; internal set; }

        public bool HasGradient { get; internal set; }
        public bool UseExistingColorMiddle { get; internal set; }
        public Color GradientColor1 { get; internal set; }
        public Color GradientColor2 { get; internal set; }
        public Color GradientColor3 { get; internal set; }
        public FancyTextGradientDirection GradientDirection { get; internal set; }
    }

    internal sealed class FancyTextActiveInstance
    {
        public FancyTextScopeMode ScopeMode { get; set; }
        public HashSet<string> TargetKeys { get; private set; }
        public HashSet<string> LegacyTargetNames { get; private set; }
        public HashSet<IComponent> TargetComponents { get; private set; }
        public FancyTextResolvedEffects Effects { get; set; }

        public FancyTextActiveInstance()
        {
            TargetKeys = new HashSet<string>(StringComparer.Ordinal);
            LegacyTargetNames = new HashSet<string>(StringComparer.Ordinal);
            TargetComponents = new HashSet<IComponent>(ReferenceComponentComparer.Instance);
        }

        public bool AppliesTo(FancyTextTargetInfo target)
        {
            if (target == null)
            {
                return false;
            }

            if (ScopeMode == FancyTextScopeMode.AllComponents)
            {
                return true;
            }

            if (ScopeMode != FancyTextScopeMode.SelectedComponents)
            {
                return false;
            }

            return TargetComponents.Contains(target.Component)
                || TargetKeys.Contains(target.Key)
                || LegacyTargetNames.Contains(target.ComponentName);
        }

        public bool AppliesToComponent(IComponent component)
        {
            if (component == null)
            {
                return false;
            }

            if (ScopeMode == FancyTextScopeMode.AllComponents)
            {
                return true;
            }

            if (ScopeMode != FancyTextScopeMode.SelectedComponents)
            {
                return false;
            }

            return TargetComponents.Contains(component);
        }
    }

    internal sealed class ReferenceComponentComparer : IEqualityComparer<IComponent>
    {
        public static readonly ReferenceComponentComparer Instance = new ReferenceComponentComparer();

        public bool Equals(IComponent x, IComponent y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(IComponent obj)
        {
            return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    public static class FancyTextRuntime
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<FancyTextComponent, FancyTextActiveInstance> ActiveInstances =
            new Dictionary<FancyTextComponent, FancyTextActiveInstance>();
        [ThreadStatic]
        private static Stack<FancyTextDrawContext> DrawStack;
        private static readonly HashSet<FancyTextComponent> PendingReorders = new HashSet<FancyTextComponent>();
        internal static Stack<FancyTextDrawContext> DrawStackForPop { get { return DrawStack; } }

        public static void Publish(FancyTextComponent owner, LiveSplitState state, FancyTextSettings settings)
        {
            if (owner == null || settings == null)
            {
                return;
            }

            lock (Sync)
            {
                PruneInactiveInstances(state);

                if (settings.ScopeMode == FancyTextScopeMode.ThisComponentOnly
                    || (!settings.OverrideTextColors && !settings.OverrideOutline && !settings.OverrideShadow))
                {
                    ActiveInstances.Remove(owner);
                    return;
                }

                ActiveInstances[owner] = CreateActiveInstance(state, settings);
            }
        }

        public static void Unpublish(FancyTextComponent owner)
        {
            if (owner == null)
            {
                return;
            }

            lock (Sync)
            {
                ActiveInstances.Remove(owner);
                PendingReorders.Remove(owner);
            }
        }

        public static FancyTextResolvedEffects GetEffectsForComponent(LiveSplitState state, IComponent component)
        {
            component = Unwrap(component);
            if (component == null)
            {
                return null;
            }

            lock (Sync)
            {
                PruneInactiveInstances(state);

                FancyTextResolvedEffects merged = null;
                foreach (FancyTextActiveInstance instance in ActiveInstances.Values)
                {
                    if (!instance.AppliesToComponent(component))
                    {
                        continue;
                    }

                    if (merged == null)
                    {
                        merged = new FancyTextResolvedEffects();
                    }

                    MergeInto(merged, instance.Effects);
                }

                return merged;
            }
        }

        public static FancyTextResolvedEffects GetCurrentEffects()
        {
            if (DrawStack == null || DrawStack.Count == 0)
            {
                return GetGlobalEffectsFallback();
            }

            FancyTextDrawContext context = DrawStack.Peek();
            return GetEffectsForComponent(context.State, context.Component);
        }

        internal static FancyTextResolvedEffects GetCurrentComponentEffects()
        {
            if (DrawStack == null || DrawStack.Count == 0)
            {
                return null;
            }

            FancyTextDrawContext context = DrawStack.Peek();
            return GetEffectsForComponent(context.State, context.Component);
        }

        internal static LiveSplitState GetCurrentDrawState()
        {
            if (DrawStack == null || DrawStack.Count == 0)
            {
                return null;
            }

            return DrawStack.Peek().State;
        }

        public static IDisposable BeginComponentDraw(LiveSplitState state, IComponent component)
        {
            if (DrawStack == null)
            {
                DrawStack = new Stack<FancyTextDrawContext>();
            }

            DrawStack.Push(new FancyTextDrawContext(state, Unwrap(component)));
            return new DrawContextPopper();
        }

        internal static void InstallHooks(LiveSplitState state)
        {
            FancyTextSimpleLabelHook.Install();
            FancyTextExternalTextHook.Install();
            PruneInactiveInstances(state);

            ILayout layout = state != null ? state.Layout : null;
            if (layout == null || layout.LayoutComponents == null)
            {
                return;
            }

            foreach (ILayoutComponent layoutComponent in layout.LayoutComponents)
            {
                if (layoutComponent == null || layoutComponent.Component == null)
                {
                    continue;
                }

                if (layoutComponent.Component is FancyTextComponent
                    || layoutComponent.Component is FancyTextComponentProxy)
                {
                    continue;
                }

                layoutComponent.Component = new FancyTextComponentProxy(layoutComponent.Component);
            }
        }

        internal static FancyTextComponent RecreateController(LiveSplitState state, FancyTextComponent owner, FancyTextSettings settings)
        {
            if (state == null || owner == null || settings == null
                || state.Layout == null || state.Layout.LayoutComponents == null)
            {
                return owner;
            }

            IList<ILayoutComponent> components = state.Layout.LayoutComponents;
            int ownerIndex = IndexOfComponent(components, owner);
            if (ownerIndex < 0)
            {
                InstallHooks(state);
                Publish(owner, state, settings);
                Invalidate(state);
                return owner;
            }

            ILayoutComponent layoutComponent = components[ownerIndex];
            var refreshedOwner = new FancyTextComponent(state, settings);

            layoutComponent.Component = refreshedOwner;
            components.RemoveAt(ownerIndex);
            components.Add(layoutComponent);

            Unpublish(owner);
            Publish(refreshedOwner, state, settings);
            InstallHooks(state);

            state.Layout.HasChanged = true;
            Invalidate(state);
            return refreshedOwner;
        }

        internal static void ScheduleControllerFirst(LiveSplitState state, FancyTextComponent owner)
        {
            if (state == null || owner == null || state.Layout == null || state.Layout.LayoutComponents == null)
            {
                return;
            }

            IList<ILayoutComponent> components = state.Layout.LayoutComponents;
            int ownerIndex = IndexOfComponent(components, owner);
            if (ownerIndex <= 0)
            {
                return;
            }

            lock (Sync)
            {
                if (!PendingReorders.Add(owner))
                {
                    return;
                }
            }

            Form form = state.Form;
            if (form == null || form.IsDisposed)
            {
                try
                {
                    ReorderControllerFirst(state, owner);
                }
                finally
                {
                    lock (Sync)
                    {
                        PendingReorders.Remove(owner);
                    }
                }
                return;
            }

            try
            {
                form.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        ReorderControllerFirst(state, owner);
                        form.Invalidate();
                    }
                    finally
                    {
                        lock (Sync)
                        {
                            PendingReorders.Remove(owner);
                        }
                    }
                }));
            }
            catch
            {
                lock (Sync)
                {
                    PendingReorders.Remove(owner);
                }
            }
        }

        internal static IList<FancyTextTargetInfo> GetTargets(LiveSplitState state, IComponent owner)
        {
            var targets = new List<FancyTextTargetInfo>();
            ILayout layout = state != null ? state.Layout : null;
            if (layout == null || layout.LayoutComponents == null)
            {
                return targets;
            }

            var occurrenceByBaseKey = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < layout.LayoutComponents.Count; index++)
            {
                ILayoutComponent layoutComponent = layout.LayoutComponents[index];
                IComponent component = layoutComponent != null ? Unwrap(layoutComponent.Component) : null;
                if (component == null || ReferenceEquals(component, owner) || component is FancyTextComponent)
                {
                    continue;
                }

                string path = layoutComponent.Path ?? string.Empty;
                string componentName = SafeComponentName(component);
                string baseKey = !string.IsNullOrEmpty(path)
                    ? path
                    : component.GetType().FullName;

                if (string.IsNullOrEmpty(baseKey))
                {
                    baseKey = componentName;
                }

                int occurrence;
                occurrenceByBaseKey.TryGetValue(baseKey, out occurrence);
                occurrenceByBaseKey[baseKey] = occurrence + 1;

                string key = baseKey + "#" + occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture);
                targets.Add(new FancyTextTargetInfo
                {
                    Key = key,
                    ComponentName = componentName,
                    DisplayName = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " + componentName,
                    Path = path,
                    LayoutIndex = index,
                    Component = component
                });
            }

            return targets;
        }

        private static FancyTextTargetInfo FindTarget(LiveSplitState state, IComponent component)
        {
            component = Unwrap(component);
            if (component == null)
            {
                return null;
            }

            foreach (FancyTextTargetInfo target in GetTargets(state, null))
            {
                if (ReferenceEquals(target.Component, component))
                {
                    return target;
                }
            }

            return null;
        }

        private static FancyTextActiveInstance CreateActiveInstance(LiveSplitState state, FancyTextSettings settings)
        {
            var active = new FancyTextActiveInstance
            {
                ScopeMode = settings.ScopeMode,
                Effects = new FancyTextResolvedEffects
                {
                    OverrideOutline = settings.OverrideOutline,
                    OutlineColor = settings.OutlineColor,
                    OutlineSize = settings.OutlineSize,
                    OverrideShadow = settings.OverrideShadow,
                    ShadowEnabled = settings.ShadowEnabled,
                    ShadowNormalEnabled = settings.ShadowNormalEnabled,
                    ShadowOutsideEnabled = settings.ShadowOutsideEnabled,
                    ShadowColor = settings.ShadowColor,
                    ShadowSize = settings.ShadowSize,
                    ShadowSizePercent = settings.ShadowSizePercent,
                    ShadowBlur = settings.ShadowBlur,
                    ShadowMultiply = settings.ShadowMultiply,
                    ShadowClipToRow = settings.ShadowClipToRow,
                    HasGradient = settings.OverrideTextColors,
                    UseExistingColorMiddle = settings.GradientMode == FancyTextGradientMode.ExistingColors,
                    GradientColor1 = settings.TextColor1,
                    GradientColor2 = settings.TextColor2,
                    GradientColor3 = settings.TextColor3,
                    GradientDirection = settings.GradientDirection
                }
            };

            foreach (string target in settings.TargetComponents)
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                if (target.IndexOf("#", StringComparison.Ordinal) >= 0
                    || target.IndexOf("\\", StringComparison.Ordinal) >= 0
                    || target.IndexOf("/", StringComparison.Ordinal) >= 0)
                {
                    active.TargetKeys.Add(target);
                }
                else
                {
                    active.LegacyTargetNames.Add(target);
                }
            }

            if (active.ScopeMode == FancyTextScopeMode.SelectedComponents)
            {
                foreach (FancyTextTargetInfo target in GetTargets(state, null))
                {
                    if (target == null || target.Component == null)
                    {
                        continue;
                    }

                    if (active.TargetKeys.Contains(target.Key)
                        || active.LegacyTargetNames.Contains(target.ComponentName))
                    {
                        active.TargetComponents.Add(target.Component);
                    }
                }
            }

            return active;
        }

        private static void MergeInto(FancyTextResolvedEffects merged, FancyTextResolvedEffects next)
        {
            if (next == null)
            {
                return;
            }

            if (next.OverrideOutline)
            {
                merged.OverrideOutline = true;
                merged.OutlineColor = next.OutlineColor;
                merged.OutlineSize = next.OutlineSize;
            }

            if (next.OverrideShadow)
            {
                merged.OverrideShadow = true;
                merged.ShadowEnabled = next.ShadowEnabled;
                merged.ShadowNormalEnabled = next.ShadowNormalEnabled;
                merged.ShadowOutsideEnabled = next.ShadowOutsideEnabled;
                merged.ShadowColor = next.ShadowColor;
                merged.ShadowSize = next.ShadowSize;
                merged.ShadowSizePercent = next.ShadowSizePercent;
                merged.ShadowBlur = next.ShadowBlur;
                merged.ShadowMultiply = next.ShadowMultiply;
                merged.ShadowClipToRow = next.ShadowClipToRow;
            }

            if (next.HasGradient)
            {
                merged.HasGradient = true;
                merged.UseExistingColorMiddle = next.UseExistingColorMiddle;
                merged.GradientColor1 = next.GradientColor1;
                merged.GradientColor2 = next.GradientColor2;
                merged.GradientColor3 = next.GradientColor3;
                merged.GradientDirection = next.GradientDirection;
            }
        }

        private static FancyTextResolvedEffects GetGlobalEffectsFallback()
        {
            lock (Sync)
            {
                PruneInactiveInstances(null);

                FancyTextResolvedEffects merged = null;
                foreach (FancyTextActiveInstance instance in ActiveInstances.Values)
                {
                    if (instance.ScopeMode != FancyTextScopeMode.AllComponents)
                    {
                        continue;
                    }

                    if (merged == null)
                    {
                        merged = new FancyTextResolvedEffects();
                    }

                    MergeInto(merged, instance.Effects);
                }

                return merged;
            }
        }

        private static void PruneInactiveInstances(LiveSplitState state)
        {
            if (state == null || state.Layout == null || state.Layout.LayoutComponents == null)
            {
                return;
            }

            lock (Sync)
            {
                if (ActiveInstances.Count == 0)
                {
                    return;
                }

                var liveOwners = new HashSet<FancyTextComponent>();
                foreach (ILayoutComponent layoutComponent in state.Layout.LayoutComponents)
                {
                    IComponent component = layoutComponent != null ? Unwrap(layoutComponent.Component) : null;
                    var fancyText = component as FancyTextComponent;
                    if (fancyText != null)
                    {
                        liveOwners.Add(fancyText);
                    }
                }

                var staleOwners = new List<FancyTextComponent>();
                foreach (FancyTextComponent owner in ActiveInstances.Keys)
                {
                    if (!liveOwners.Contains(owner))
                    {
                        staleOwners.Add(owner);
                    }
                }

                foreach (FancyTextComponent owner in staleOwners)
                {
                    ActiveInstances.Remove(owner);
                    PendingReorders.Remove(owner);
                }
            }
        }

        private static string SafeComponentName(IComponent component)
        {
            try
            {
                string name = component.ComponentName;
                return string.IsNullOrWhiteSpace(name) ? component.GetType().Name : name;
            }
            catch
            {
                return component.GetType().Name;
            }
        }

        internal static IComponent Unwrap(IComponent component)
        {
            var proxy = component as FancyTextComponentProxy;
            return proxy != null ? proxy.Inner : component;
        }

        private static void ReorderControllerFirst(LiveSplitState state, FancyTextComponent owner)
        {
            if (state == null || state.Layout == null || state.Layout.LayoutComponents == null)
            {
                return;
            }

            IList<ILayoutComponent> components = state.Layout.LayoutComponents;
            int ownerIndex = IndexOfComponent(components, owner);
            if (ownerIndex <= 0)
            {
                return;
            }

            ILayoutComponent item = components[ownerIndex];
            components.RemoveAt(ownerIndex);
            components.Insert(0, item);
            state.Layout.HasChanged = true;
        }

        private static int IndexOfComponent(IList<ILayoutComponent> components, IComponent component)
        {
            for (int i = 0; i < components.Count; i++)
            {
                IComponent current = components[i] != null ? Unwrap(components[i].Component) : null;
                if (ReferenceEquals(current, component))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void Invalidate(LiveSplitState state)
        {
            Form form = state != null ? state.Form : null;
            if (form == null || form.IsDisposed)
            {
                return;
            }

            try
            {
                form.Invalidate();
            }
            catch
            {
            }
        }
    }

    internal sealed class FancyTextDrawContext
    {
        public LiveSplitState State { get; private set; }
        public IComponent Component { get; private set; }

        public FancyTextDrawContext(LiveSplitState state, IComponent component)
        {
            State = state;
            Component = component;
        }
    }

    internal sealed class DrawContextPopper : IDisposable
    {
        public void Dispose()
        {
            if (FancyTextRuntime.DrawStackForPop != null && FancyTextRuntime.DrawStackForPop.Count > 0)
            {
                FancyTextRuntime.DrawStackForPop.Pop();
            }
        }
    }

    internal sealed class FancyTextComponentProxy : IDeactivatableComponent
    {
        public IComponent Inner { get; private set; }

        public FancyTextComponentProxy(IComponent inner)
        {
            Inner = inner;
        }

        public string ComponentName { get { return Inner.ComponentName; } }
        public float HorizontalWidth { get { return Inner.HorizontalWidth; } }
        public float MinimumHeight { get { return Inner.MinimumHeight; } }
        public float VerticalHeight { get { return Inner.VerticalHeight; } }
        public float MinimumWidth { get { return Inner.MinimumWidth; } }
        public float PaddingTop { get { return Inner.PaddingTop; } }
        public float PaddingBottom { get { return Inner.PaddingBottom; } }
        public float PaddingLeft { get { return Inner.PaddingLeft; } }
        public float PaddingRight { get { return Inner.PaddingRight; } }
        public IDictionary<string, Action> ContextMenuControls { get { return Inner.ContextMenuControls; } }

        public bool Activated
        {
            get
            {
                var deactivatable = Inner as IDeactivatableComponent;
                return deactivatable == null || deactivatable.Activated;
            }
            set
            {
                var deactivatable = Inner as IDeactivatableComponent;
                if (deactivatable != null)
                {
                    deactivatable.Activated = value;
                }
            }
        }

        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
        {
            try
            {
                using (FancyTextRuntime.BeginComponentDraw(state, Inner))
                using (new LayoutSettingsScope(state, FancyTextRuntime.GetEffectsForComponent(state, Inner)))
                {
                    Inner.DrawHorizontal(g, state, height, clipRegion);
                }
            }
            catch
            {
                Inner.DrawHorizontal(g, state, height, clipRegion);
            }
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
        {
            try
            {
                using (FancyTextRuntime.BeginComponentDraw(state, Inner))
                using (new LayoutSettingsScope(state, FancyTextRuntime.GetEffectsForComponent(state, Inner)))
                {
                    Inner.DrawVertical(g, state, width, clipRegion);
                }
            }
            catch
            {
                Inner.DrawVertical(g, state, width, clipRegion);
            }
        }

        public Control GetSettingsControl(LayoutMode mode)
        {
            return Inner.GetSettingsControl(mode);
        }

        public XmlNode GetSettings(XmlDocument document)
        {
            return Inner.GetSettings(document);
        }

        public void SetSettings(XmlNode settings)
        {
            Inner.SetSettings(settings);
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
            Inner.Update(invalidator, state, width, height, mode);
        }

        public void Dispose()
        {
            Inner.Dispose();
        }
    }

    internal sealed class LayoutSettingsScope : IDisposable
    {
        private readonly LiveSplitState _state;
        private readonly bool _hasEffects;
        private readonly bool _dropShadows;
        private readonly Color _shadowsColor;
        private readonly Color _textOutlineColor;

        public LayoutSettingsScope(LiveSplitState state, FancyTextResolvedEffects effects)
        {
            _state = state;
            if (state == null || state.LayoutSettings == null || effects == null)
            {
                return;
            }

            _hasEffects = true;
            _dropShadows = state.LayoutSettings.DropShadows;
            _shadowsColor = state.LayoutSettings.ShadowsColor;
            _textOutlineColor = state.LayoutSettings.TextOutlineColor;

            if (effects.OverrideShadow)
            {
                state.LayoutSettings.DropShadows = effects.ShadowEnabled;
                state.LayoutSettings.ShadowsColor = effects.ShadowColor;
            }

            if (effects.OverrideOutline)
            {
                state.LayoutSettings.TextOutlineColor = effects.OutlineSize > 0f
                    ? effects.OutlineColor
                    : Color.Transparent;
            }
        }

        public void Dispose()
        {
            if (!_hasEffects || _state == null || _state.LayoutSettings == null)
            {
                return;
            }

            _state.LayoutSettings.DropShadows = _dropShadows;
            _state.LayoutSettings.ShadowsColor = _shadowsColor;
            _state.LayoutSettings.TextOutlineColor = _textOutlineColor;
        }
    }
}
