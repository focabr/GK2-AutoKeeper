// STUB só para compilar a ponte (assinaturas copiadas das referências da ponte 0.3.12). Nunca distribuir.
using System;
using BepInEx.Configuration;
namespace GK2.Framework
{
    public static class FrameworkPlugin { public const string PluginGuid = "superman4eg.gk2.framework"; }
    public interface IGk2Mod { Gk2ModMetadata Metadata { get; } void OnRegister(Gk2ModContext context); }
    public abstract class Gk2ModBase : IGk2Mod { public abstract Gk2ModMetadata Metadata { get; } public virtual void OnRegister(Gk2ModContext context) { } }
    public sealed class Gk2ModMetadata { public Gk2ModMetadata(string id, string name, string author, string version, string description, bool supportsRuntimeToggle, bool requiresKnownBuild, bool frameworkManagesEnabledState) { } }
    public sealed class RegisteredMod { }
    public sealed class Gk2ModContext { public Gk2Settings Settings => null; }
    public static class FrameworkApi { public static RegisteredMod RegisterMod(IGk2Mod mod, ConfigFile config) => null; }
    public static class FrameworkLocalization { public static string CurrentLanguage => null; }
    public sealed class Gk2Settings
    {
        public void AddButton(string section, string key, string name, string help, Func<string> label, Action action, int order = 0) { }
        public void AddReadOnly(string section, string key, string name, string help, Func<string> value, int order = 0) { }
        public void SetEnabledCondition(string section, string key, Func<bool> condition) { }
        public ConfigEntry<bool> AddToggle(string section, string key, bool def, string name, string help, int order = 0) => null;
        public ConfigEntry<float> AddFloatSlider(string section, string key, float def, float min, float max, string name, string help, float step, int order = 0) => null;
        public ConfigEntry<int> AddIntSlider(string section, string key, int def, int min, int max, string name, string help, int step, int order = 0) => null;
        public ConfigEntry<T> AddEnum<T>(string section, string key, T def, string name, string help, int order = 0) => null;
        public ConfigEntry<KeyboardShortcut> AddKeybind(string section, string key, KeyboardShortcut def, string name, string help, int order = 0) => null;
        public ConfigEntry<string> AddText(string section, string key, string def, string name, string help, int order = 0) => null;
    }
}
