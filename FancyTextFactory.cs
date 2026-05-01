// ============================================================================
// FancyTextFactory.cs
// Registers FancyText with LiveSplit's component system.
// ============================================================================

using System;
using System.Reflection;
using LiveSplit.Model;
using LiveSplit.UI.Components;

[assembly: ComponentFactory(typeof(FancyTextFactory))]

namespace LiveSplit.UI.Components
{
    public class FancyTextFactory : IComponentFactory
    {
        public string ComponentName => "Fancy Text";

        public string Description =>
            "Configurable label with gradient text colors, custom outline sizes, " +
            "and custom shadow sizes.";

        public ComponentCategory Category => ComponentCategory.Media;

        public IComponent Create(LiveSplitState state) => new FancyTextComponent(state);

        public string UpdateName => ComponentName;
        public string UpdateURL  => string.Empty;
        public string XMLURL     => string.Empty;

        public Version Version =>
            Assembly.GetExecutingAssembly().GetName().Version;
    }
}
