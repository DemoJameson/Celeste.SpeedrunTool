using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// 将 ComVisible 设置为 false 会使此程序集中的类型
//对 COM 组件不可见。如果需要从 COM 访问此程序集中的类型
//请将此类型的 ComVisible 特性设置为 true。
[assembly: ComVisible(false)]

// 如果此项目向 COM 公开，则下列 GUID 用于类型库的 ID
[assembly: Guid("c7dfdd37-907c-4a75-96da-1c5828c97f96")]

// 配合 NStrip -p 处理的 dll，直接调用 private 的类/字段/属性/方法
[assembly: IgnoresAccessChecksTo("Celeste")] 
[assembly: IgnoresAccessChecksTo("MonoMod.Utils")] 