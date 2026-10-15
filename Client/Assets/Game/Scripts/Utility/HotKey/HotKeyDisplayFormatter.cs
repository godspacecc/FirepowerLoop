using UnityEngine.InputSystem;

namespace Game
{
    public static class HotKeyDisplayFormatter
    {
        public static string FormatPath(string bindingPath)
        {
            if (string.IsNullOrWhiteSpace(bindingPath)) return string.Empty;
            var display = InputControlPath.ToHumanReadableString(bindingPath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (string.IsNullOrWhiteSpace(display)) display = bindingPath;
            return display == "Escape" ? "Esc" : display;
        }

        public static string FormatAction(HotKeyModel model, string actionId)
        {
            return model != null && model.TryGet(actionId, out var binding)
                ? FormatPath(binding.BindingPath)
                : string.Empty;
        }
    }
}
