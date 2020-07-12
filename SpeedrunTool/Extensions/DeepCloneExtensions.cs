using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class DeepCloneExtensions {
        public static T DeepClone<T>(this T obj) {
            using (MemoryStream ms = new MemoryStream()) {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(ms, obj);
                ms.Position = 0;

                return (T) formatter.Deserialize(ms);
            }
        }

        // deep clone an object using YAML (de)serialization.
        public static T DeepCloneYaml<T>(this object obj, Type type) {
            string yaml = YamlHelper.Serializer.Serialize(obj);
            return (T) YamlHelper.Deserializer.Deserialize(yaml, type);
        }
    }
}