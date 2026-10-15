using QFramework;
namespace Game
{
    public readonly struct LanguageChangedEvent
    {
        public LanguageChangedEvent(string previousLanguageCode, string currentLanguageCode)
        {
            PreviousLanguageCode = previousLanguageCode;
            CurrentLanguageCode = currentLanguageCode;
        }

        public string PreviousLanguageCode { get; }
        public string CurrentLanguageCode { get; }
    }

    public sealed class SetLanguageCommand : AbstractCommand
    {
        public string LanguageCode;

        protected override void OnExecute()
        {
            var model = this.GetModel<LocalizationModel>();
            var previousLanguageCode = model.CurrentLanguageCode.Value;
            model.SetLanguageCode(LanguageCode);
            var currentLanguageCode = model.CurrentLanguageCode.Value;
            this.SendEvent(new LanguageChangedEvent(previousLanguageCode, currentLanguageCode));
            this.GetSystem<SettingsSystem>().MarkDirty();
        }
    }
}
