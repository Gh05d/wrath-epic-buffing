using JetBrains.Annotations;

namespace BuffIt2TheLimit.Config {
    public class NestedSettingGroup : SettingGroup {
        private IDisableableGroup parent;
        public IDisableableGroup Parent { set { parent = value; } }

        public NestedSettingGroup([NotNull] IDisableableGroup parent) {
            this.parent = parent;
        }

        public override bool IsEnabled(string key) {
            return base.IsEnabled(key) && !DisableAll && (!parent?.GroupIsDisabled() ?? true);
        }
        public override bool IsDisabled(string key) {
            return !IsEnabled(key);
        }
    }
}
