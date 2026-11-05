using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace McBatch.TestSupport;

/// <summary>测试用点位描述: 地址字符串 + CLR 属性类型。</summary>
public sealed record PointSpec(string Address, Type ClrType);

/// <summary>
/// 运行时生成带 [McPoint] 标注的 POCO 类型。
/// 用于 property 测试的随机点位(0~300 个/轮)以及超过协议单批上限的大批量输入,
/// 避免在源码里手写上千个属性。
/// </summary>
public static class DynamicModelBuilder
{
    public static Type Create(IReadOnlyList<PointSpec> points)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("McBatchTestModels_" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Models");
        var typeBuilder = module.DefineType(
            "McBatchTestModel_" + Guid.NewGuid().ToString("N"),
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);

        var pointCtor = typeof(McPointAttribute).GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException("McPointAttribute(string) 构造器不存在。");

        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            string name = "P" + i.ToString(CultureInfo.InvariantCulture);
            var field = typeBuilder.DefineField("_" + name, point.ClrType, FieldAttributes.Private);
            var property = typeBuilder.DefineProperty(name, PropertyAttributes.None, point.ClrType, null);

            var getter = typeBuilder.DefineMethod(
                "get_" + name,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                point.ClrType,
                Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);

            var setter = typeBuilder.DefineMethod(
                "set_" + name,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                null,
                [point.ClrType]);
            il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);

            property.SetGetMethod(getter);
            property.SetSetMethod(setter);
            property.SetCustomAttribute(new CustomAttributeBuilder(pointCtor, [point.Address]));
        }

        return typeBuilder.CreateType()!;
    }

    public static object CreateInstance(IReadOnlyList<PointSpec> points) =>
        Activator.CreateInstance(Create(points))!;
}
