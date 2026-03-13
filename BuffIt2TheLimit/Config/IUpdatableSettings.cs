namespace BuffIt2TheLimit.Config {
    public interface IUpdatableSettings {
        void OverrideSettings(IUpdatableSettings userSettings);
    }
}
