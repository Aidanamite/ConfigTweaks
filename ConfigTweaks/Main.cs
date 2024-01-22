using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using System.IO;

namespace ConfigTweaks
{
    [BepInPlugin("com.aidanamite.ConfigTweaks", "Config Tweaks", "1.1.0")]
    public class Main : BaseUnityPlugin
    {
        internal static bool saving = false;
        internal static Dictionary<BaseUnityPlugin, List<(ConfigEntryBase, FieldInfo)>> configs = new Dictionary<BaseUnityPlugin, List<(ConfigEntryBase, FieldInfo)>>();
        public void Awake()
        {
            new Harmony("com.aidanamite.ConfigTweaks").PatchAll();
            var listener = new FileSystemWatcher(Paths.ConfigPath, "*.cfg");
            void OnChange(object sender, FileSystemEventArgs args)
            {
                if (saving)
                    return;
                foreach (var p in configs)
                    if (p.Key.Config.ConfigFilePath == args.FullPath)
                    {
                        p.Key.Config.Reload();
                        break;
                    }
            }
            listener.Changed += OnChange;
            listener.Created += OnChange;
            listener.Renamed += OnChange;
            listener.EnableRaisingEvents = true;
            Logger.LogInfo("Loaded");
        }
        public static void BindFields(BaseUnityPlugin plugin)
        {
            var bind = typeof(ConfigFile).GetMethods().First(x => x.Name == "Bind" && x.GetParameters().Length == 4 && x.GetParameters()[0].ParameterType == typeof(string) && x.GetParameters()[1].ParameterType == typeof(string) && x.GetParameters()[3].ParameterType == typeof(ConfigDescription));
            foreach (var f in plugin.GetType().GetFields(~BindingFlags.Default))
            {
                var a = f.GetCustomAttribute<ConfigFieldAttribute>();
                if (a != null && FieldConfigEntry.TryCreate(plugin.Config, new ConfigDefinition(a.Section, a.Key ?? f.Name), plugin, f, string.IsNullOrEmpty(a.Description) ? null : new ConfigDescription(a.Description), out var e))
                    configs.GetOrCreate(plugin).Add((e, f));
            }
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class ConfigFieldAttribute : Attribute
    {
        public readonly string Key;
        public readonly string Section;
        public string Description { get; set; }
        public ConfigFieldAttribute(string Key = null,string Section = "Config")
        {
            this.Key = Key;
            this.Section = Section;
        }
    }

    public class FieldConfigEntry : ConfigEntryBase
    {
        FieldInfo Field;
        object Target;
        FieldConfigEntry(ConfigFile file,ConfigDefinition definition,object target,FieldInfo field,ConfigDescription description) : base(file,definition,field.FieldType,field.GetValue(target),description)
        {
            Field = field;
            Target = target;
        }
        public override object BoxedValue
        {
            get => Field?.GetValue(Target);
            set => Field?.SetValue(Target, value);
        }

        internal static bool TryCreate(ConfigFile file, ConfigDefinition definition, object target, FieldInfo field, ConfigDescription description, out FieldConfigEntry entry)
        {
            lock (file._ioLock)
            {
                if (file.Entries.TryGetValue(definition, out var v))
                {
                    entry = null;
                    return false;
                }
                file.Entries[definition] = entry = new FieldConfigEntry(file, definition, target, field, description);
                if (file.OrphanedEntries.TryGetValue(definition, out var old))
                {
                    entry.SetSerializedValue(old);
                    file.OrphanedEntries.Remove(definition);
                }
                if (file.SaveOnConfigSet)
                    file.Save();
                return true;
            }
        }
    }

    public static class ExtentionMethods
    {
        public static Y GetOrCreate<X,Y>(this IDictionary<X,Y> d, X key) where Y : new()
        {
            if (d.TryGetValue(key, out var v))
                return v;
            return d[key] = new Y();
        }
    }

    [HarmonyPatch(typeof(BaseUnityPlugin),MethodType.Constructor,new Type[0])]
    static class Patch_CreatePluginObj
    {
        static void Postfix(BaseUnityPlugin __instance)
        {
            Main.BindFields(__instance);
        }
    }

    [HarmonyPatch(typeof(ConfigFile), "Save")]
    static class Patch_ConfigSaving
    {
        static void Prefix() => Main.saving = true;
        static void Finalizer() => Main.saving = false;
    }
}