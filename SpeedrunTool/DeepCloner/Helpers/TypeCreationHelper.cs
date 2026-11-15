#if !NETCORE
using System.Reflection;
using System.Reflection.Emit;

namespace Force.DeepCloner.Helpers {
    internal static class TypeCreationHelper {
        private static ModuleBuilder _moduleBuilder;

        internal static ModuleBuilder GetModuleBuilder() {
            // todo: think about multithread
            if (_moduleBuilder == null) {
                AssemblyName aName = new AssemblyName("DeepClonerCode");
                AssemblyBuilder ab = AssemblyBuilder.DefineDynamicAssembly(aName, AssemblyBuilderAccess.Run);
                ModuleBuilder mb = ab.DefineDynamicModule(aName.Name);
                _moduleBuilder = mb;
            }

            return _moduleBuilder;
        }
    }
}
#endif