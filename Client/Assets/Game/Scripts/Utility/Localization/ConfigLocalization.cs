namespace Game
{
    /// <summary>
    /// 配置表文本本地化入口，每次调用都实时读取当前语言，确保切换语言后立即生效。
    /// </summary>
    public static class ConfigLocalization
    {
        public static string GetItemName(global::Item item, string fallbackId = "")
        {
            if (item == null)
            {
                return string.IsNullOrWhiteSpace(fallbackId) ? string.Empty : fallbackId;
            }

            return Resolve(item.NameKey, item.Name, fallbackId);
        }

        public static string GetItemDesc(global::Item item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            return Resolve(item.DescKey, item.Desc, string.Empty);
        }

        private static string Resolve(string key, string legacyText, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                var text = Loc.T(key);
                if (!string.IsNullOrWhiteSpace(text) && text != key)
                {
                    return text;
                }
            }

            if (!string.IsNullOrWhiteSpace(legacyText))
            {
                return legacyText;
            }

            return fallback ?? string.Empty;
        }
    }
}
