using System;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    internal static class DeepCloneExtensions {
        // deep clone an object using YAML (de)serialization.
        public static T DeepCloneYaml<T>(this T obj, Type type) {
            string yaml = YamlHelper.Serializer.Serialize(obj);
            return (T) YamlHelper.Deserializer.Deserialize(yaml, type);
        }
    }
}