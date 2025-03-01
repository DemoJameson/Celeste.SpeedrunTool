
using Celeste.Mod.SpeedrunTool.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class ExtendedVariantsUtils {

    internal static void Support() {
        // 静态字段在 InitExtendedVariantsFields() 中处理了

        if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsModule") is not { } moduleType) {
            return;
        }

        // 修复玩家死亡后不会重置设置
        SaveLoadAction.InternalSafeAdd((savedValues, _) => {
            if (Everest.Modules.FirstOrDefault(everestModule => everestModule.Metadata?.Name == "ExtendedVariantMode") is { } module &&
                module.GetFieldValue("TriggerManager") is { } triggerManager) {
                savedValues[moduleType] = new Dictionary<string, object> { { "TriggerManager", triggerManager.DeepCloneShared() } };
            }
        }, (savedValues, _) => {
            if (savedValues.TryGetValue(moduleType, out Dictionary<string, object> dictionary) &&
                dictionary.TryGetValue("TriggerManager", out object savedTriggerManager) &&
                Everest.Modules.FirstOrDefault(everestModule => everestModule.Metadata?.Name == "ExtendedVariantMode") is { } module) {
                if (module.GetFieldValue("TriggerManager") is { } triggerManager) {
                    savedTriggerManager.DeepCloneToShared(triggerManager);
                }
            }
        });

        if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsSettings") is not { } settingsType) {
            return;
        }

        List<PropertyInfo> settingProperties = settingsType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead
                               && property.CanWrite
                               && property.GetCustomAttribute<SettingIgnoreAttribute>() != null
                               && !property.Name.StartsWith("Display")
            ).ToList();

        SaveLoadAction.InternalSafeAdd(
            (savedValues, _) => {
                if (moduleType.GetPropertyValue("Settings") is not { } settingsInstance) {
                    return;
                }

                Dictionary<string, object> dict = new();
                foreach (PropertyInfo property in settingProperties) {
                    dict[property.Name] = property.GetValue(settingsInstance);
                }

                savedValues[settingsType] = dict.DeepCloneShared();
            },
            (savedValues, _) => {
                // 本来打算将关卡中 ExtendedVariantsTrigger 涉及相关的值强制 SL，想想还是算了
                if (!ModSettings.SaveExtendedVariants && !StateManager.Instance.SavedByTas) {
                    return;
                }

                if (moduleType.GetPropertyValue("Settings") is not { } settingsInstance) {
                    return;
                }

                if (savedValues.TryGetValue(settingsType, out Dictionary<string, object> dict)) {
                    dict = dict.DeepCloneShared();
                    foreach (string propertyName in dict.Keys) {
                        settingsInstance.SetPropertyValue(propertyName, dict[propertyName]);
                    }
                }
            });
    }
}
