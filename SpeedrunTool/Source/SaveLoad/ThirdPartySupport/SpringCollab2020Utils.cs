
using Celeste.Mod.SpeedrunTool.Utils;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class SpringCollab2020Utils {
    internal static void Support() {
        const string key1 = "RainbowSpinnerColorController";
        const string key2 = "RainbowSpinnerColorAreaController";
        const string key3 = "SpikeHooked";
        const string b_hooked1 = "rainbowSpinnerHueHooked";
        const string b_hooked2 = "rainbowSpinnerHueHooked";
        const string b_hooked3 = "SpikeHooked";

        // 2026.04.17:
        // 这个的 hook 添加 / 移除写的比其他 mod 麻烦
        // 因为 SC2020 的 hook 并不是只要对应实体被 removed 就 unhook
        // 里面的逻辑有点奇怪. 我怀疑应该确实有情形会导致 removed 但不 unhook. 尤其在有 SL 的情况下这非常有可能
        // 于是我们这里的写法是, 记录下是否 hooked, 然后读档根据 hooked 去更新 hook 状态

        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController") is { } type1
            && type1.GetFieldInfo(b_hooked1) != null
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    type1.GetMethodInfo("getRainbowSpinnerHue")) is On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue

            && ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorAreaController") is { } type2
            && type2.GetFieldInfo(b_hooked2) != null
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    type2.GetMethodInfo("getRainbowSpinnerHue")) is On.Celeste.CrystalStaticSpinner.hook_GetHue hookSpinnerGetHue

            && ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.SpikeJumpThroughController") is { } type3
            && type3.GetFieldInfo(b_hooked3) != null
            && Delegate.CreateDelegate(typeof(On.Celeste.Spikes.hook_OnCollide),
                    type3.GetMethodInfo("OnCollideHook")) is On.Celeste.Spikes.hook_OnCollide onCollideHook
           ) {

            SaveLoadAction.InternalSafeAdd(
                saveState: (savedValues, _) => {
                    Dictionary<string, object> dict = new() {
                        [key1] = type1.GetFieldValue<bool>(b_hooked1),
                        [key2] = type2.GetFieldValue<bool>(b_hooked2),
                        [key3] = type3.GetFieldValue<bool>(b_hooked3),
                    };

                    savedValues[typeof(SpringCollab2020Utils)] = dict;
                },
                loadState: (savedValues, _) => {
                    if (!savedValues.TryGetValue(typeof(SpringCollab2020Utils), out Dictionary<string, object> dict)) {
                        return;
                    }
                    if (dict.TryGetValue(key1, out object o1)) {
                        bool b1 = (bool)o1;
                        // 我们不检测 type1.GetFieldValue<bool>(b_hooked1) 的值是否与 b1 相同 (不知道为什么, 这个值好像不准)
                        if (b1) {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                            On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                        }
                        else {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        }
                        type1.SetFieldValue(b_hooked1, b1);
                    }
                    if (dict.TryGetValue(key2, out object o2)) {
                        bool b2 = (bool)o2;
                        if (b2) {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                            On.Celeste.CrystalStaticSpinner.GetHue += hookSpinnerGetHue;
                        }
                        else {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                        }
                        type2.SetFieldValue(b_hooked2, b2);
                    }
                    if (dict.TryGetValue(key3, out object o3)) {
                        bool b3 = (bool)o3;
                        if (b3) {
                            On.Celeste.Spikes.OnCollide -= onCollideHook;
                            On.Celeste.Spikes.OnCollide += onCollideHook;
                        }
                        else {
                            On.Celeste.Spikes.OnCollide -= onCollideHook;
                        }
                        type3.SetFieldValue(b_hooked3, b3);
                    }
                }
            );
        }
    }
}
