using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AutoMapper;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public static class AutoMapperUtils {
        private static readonly Dictionary<Type, IMapper> Mappers = new Dictionary<Type, IMapper>();

        private static List<Type> AllTypes(Type baseType) {
            return typeof(Celeste).Assembly.GetExportedTypes()
                .Where(type => type.IsSameOrSubclassOf(baseType) && !type.IsAbstract).ToList();
        }

        private static List<Type> AllTypes<T>() {
            return AllTypes(typeof(T));
        }

        private static bool ShouldMap(Type type) {
            return type.IsPrimitive || type.IsValueType || type.IsEnum || type == typeof(string)
                || type.IsSameOrSubclassOf(typeof(Component))
                || type.IsSameOrSubclassOf(typeof(Collider))
                || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && type.GetGenericArguments()[0] == typeof(Vector2)
                // || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                // || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollection<>)
                || type == typeof(ComponentList);
        }

        public static IMapper GetMapper(Type type) {
            if (Mappers.ContainsKey(type)) {
                return Mappers[type];
            }

            MapperConfiguration mapperConfiguration = new MapperConfiguration(cfg => {
                cfg.ShouldMapField = field => ShouldMap(field.FieldType);
                cfg.ShouldMapProperty = property => ShouldMap(property.PropertyType);
                cfg.ShouldMapMethod = method => false;
                
                cfg.CreateMap(type, type);

                cfg.CreateMap(typeof(ComponentList), typeof(ComponentList));

                foreach (Type t in AllTypes<Component>()) {
                    cfg.CreateMap(t, t);
                }

                foreach (Type t in AllTypes<Collider>()) {
                    cfg.CreateMap(t, t);
                }

                // Dont know hot to member name case senstive.
                cfg.ForAllMaps((typeMap, mappingExpr) => {
                    foreach (PropertyMap map in typeMap.PropertyMaps) {
                        if (map.SourceType != map.DestinationType) {
                            Logger.Log("SpeedrunTool",
                                $"AutoMapper Failed: {typeMap.SourceType}; name {map.SourceMember.Name} -> {map.DestinationName}; type {map.SourceType} -> {map.DestinationType}");
                            map.Ignored = true;
                        }
                    }
                });
            });
            // mapperConfiguration.AssertConfigurationIsValid();
            IMapper mapper = mapperConfiguration.CreateMapper();

            Mappers[type] = mapper;
            return mapper;
        }
    }

    public class CaseSensitiveNamingConvention : INamingConvention {
        public Regex SplittingExpression { get; } = new Regex(".+");
        public string SeparatorCharacter => "";
        public string ReplaceValue(Match match) => "_" + match.Value + "hahah";
    }
}