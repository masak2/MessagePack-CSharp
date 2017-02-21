﻿//using System;
//using System.Linq;
//using MessagePack.Formatters;
//using MessagePack.Internal;
//using System.Reflection;
//using System.Reflection.Emit;
//using System.Collections.Generic;

//namespace MessagePack.Resolvers
//{
//    /// <summary>
//    /// ObjectResolver by dynamic code generation.
//    /// </summary>
//    public class DynamicObjectResolver : IFormatterResolver
//    {
//        public static IFormatterResolver Instance = new DynamicObjectResolver();

//        const string ModuleName = "MessagePack.Resolvers.DynamicObjectResolver";

//        static readonly DynamicAssembly assembly;

//        DynamicObjectResolver()
//        {

//        }

//        static DynamicObjectResolver()
//        {
//            assembly = new DynamicAssembly(ModuleName);
//        }

//        IMessagePackFormatter<T> IFormatterResolver.GetFormatter<T>()
//        {
//            return FormatterCache<T>.formatter;
//        }

//        static class FormatterCache<T>
//        {
//            public static readonly IMessagePackFormatter<T> formatter;

//            static FormatterCache()
//            {
//                var ti = typeof(T).GetTypeInfo();
//                if (ti.IsNullable())
//                {
//                    ti = ti.GenericTypeArguments[0].GetTypeInfo();

//                    var innerFormatter = DynamicObjectResolver.Instance.GetFormatterDynamic(ti.AsType());
//                    if (innerFormatter == null)
//                    {
//                        return;
//                    }
//                    formatter = (IMessagePackFormatter<T>)Activator.CreateInstance(typeof(StaticNullableFormatter<>).MakeGenericType(ti.AsType()), new object[] { innerFormatter });
//                    return;
//                }

//                var formatterTypeInfo = BuildType(typeof(T));
//                if (formatterTypeInfo == null) return;

//                formatter = (IMessagePackFormatter<T>)Activator.CreateInstance(formatterTypeInfo.AsType());
//            }
//        }

//        static TypeInfo BuildType(Type type)
//        {
//            var serializationInfo = MessagePack.Internal.ObjectSerializationInfo.CreateOrNull(type);
//            if (serializationInfo == null) return null;

//            var ti = type.GetTypeInfo();

//            var formatterType = typeof(IMessagePackFormatter<>).MakeGenericType(type);
//            var typeBuilder = assembly.ModuleBuilder.DefineType("MessagePack.Formatters." + type.FullName.Replace(".", "_") + "Formatter", TypeAttributes.Public, null, new[] { formatterType });

//            FieldBuilder dictionaryField = null;

//            // string key needs string->int mapper for deserialize switch statement
//            if (serializationInfo.IsStringKey)
//            {
//                var method = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
//                dictionaryField = typeBuilder.DefineField("keyMapping", typeof(Dictionary<string, int>), FieldAttributes.Private | FieldAttributes.InitOnly);

//                var il = method.GetILGenerator();
//                BuildConstructor(type, serializationInfo, method, dictionaryField, il);
//            }
//            {
//                var method = typeBuilder.DefineMethod("Serialize", MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual,
//                    typeof(int),
//                    new Type[] { typeof(byte[]).MakeByRefType(), typeof(int), type, typeof(IFormatterResolver) });

//                var il = method.GetILGenerator();
//                BuildSerialize(type, serializationInfo, method, il);
//            }

//            {
//                var method = typeBuilder.DefineMethod("Deserialize", MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual,
//                    type,
//                    new Type[] { typeof(byte[]), typeof(int), typeof(IFormatterResolver), typeof(int).MakeByRefType() });

//                var il = method.GetILGenerator();
//                BuildDeserialize(type, serializationInfo, method, dictionaryField, il);
//            }

//            return typeBuilder.CreateTypeInfo();
//        }

//        static void BuildConstructor(Type type, ObjectSerializationInfo info, ConstructorInfo method, FieldBuilder dictionaryField, ILGenerator il)
//        {
//            il.EmitLdarg(0);
//            il.EmitLdc_I4(info.Members.Length);
//            il.Emit(OpCodes.Newobj, dictionaryConstructor);

//            foreach (var item in info.Members)
//            {
//                il.Emit(OpCodes.Dup);
//                il.Emit(OpCodes.Ldstr, item.StringKey);
//                il.EmitLdc_I4(item.IntKey);
//                il.EmitCall(dictionaryAdd);
//            }

//            il.Emit(OpCodes.Stfld, dictionaryField);
//            il.Emit(OpCodes.Ret);
//        }

//        // int Serialize([arg:1]ref byte[] bytes, [arg:2]int offset, [arg:3]T value, [arg:4]IFormatterResolver formatterResolver);
//        static void BuildSerialize(Type type, ObjectSerializationInfo info, MethodBuilder method, ILGenerator il)
//        {
//            // if(value == null) return WriteNil
//            if (type.GetTypeInfo().IsClass)
//            {
//                var elseBody = il.DefineLabel();

//                il.EmitLdarg(3);
//                il.Emit(OpCodes.Brtrue_S, elseBody);
//                il.EmitLdarg(1);
//                il.EmitLdarg(2);
//                il.EmitCall(MessagePackBinaryTypeInfo.WriteNil);
//                il.Emit(OpCodes.Ret);

//                il.MarkLabel(elseBody);
//            }

//            var writeCount = info.Members.Count(x => x.IsReadable);

//            // var startOffset = offset;
//            var startOffsetLocal = il.DeclareLocal(typeof(int)); // [loc:0]
//            il.EmitLdarg(2);
//            il.EmitStloc(startOffsetLocal);

//            // offset += writeHeader
//            EmitOffsetPlusEqual(il, null, () =>
//             {
//                 il.EmitLdc_I4(writeCount);
//                 if (writeCount <= MessagePackRange.MaxFixMapCount)
//                 {
//                     il.EmitCall(MessagePackBinaryTypeInfo.WriteFixedMapHeaderUnsafe);
//                 }
//                 else
//                 {
//                     il.EmitCall(MessagePackBinaryTypeInfo.WriteMapHeader);
//                 }
//             });

//            foreach (var item in info.Members)
//            {
//                // offset += writekey
//                EmitOffsetPlusEqual(il, null, () =>
//                 {
//                     if (info.IsIntKey)
//                     {
//                         il.EmitLdc_I4(item.IntKey);
//                         if (0 <= item.IntKey && item.IntKey <= MessagePackRange.MaxFixPositiveInt)
//                         {
//                             il.EmitCall(MessagePackBinaryTypeInfo.WritePositiveFixedIntUnsafe);
//                         }
//                         else
//                         {
//                             il.EmitCall(MessagePackBinaryTypeInfo.WriteInt32);
//                         }
//                     }
//                     else
//                     {
//                         // embed string and bytesize
//                         il.Emit(OpCodes.Ldstr, item.StringKey);
//                         il.EmitLdc_I4(StringEncoding.UTF8.GetByteCount(item.StringKey));
//                         il.EmitCall(MessagePackBinaryTypeInfo.WriteStringUnsafe);
//                     }
//                 });

//                // offset += serializeValue
//                EmitSerializeValue(il, type.GetTypeInfo(), item);
//            }

//            // return startOffset- offset;
//            il.EmitLdarg(2);
//            il.EmitLdloc(startOffsetLocal);
//            il.Emit(OpCodes.Sub);
//            il.Emit(OpCodes.Ret);
//        }

//        // offset += ***(ref bytes, offset....
//        static void EmitOffsetPlusEqual(ILGenerator il, Action loadEmit, Action emit)
//        {
//            il.EmitLdarg(2);

//            if (loadEmit != null) loadEmit();

//            il.EmitLdarg(1);
//            il.EmitLdarg(2);

//            emit();

//            il.Emit(OpCodes.Add);
//            il.EmitStarg(2);
//        }

//        static void EmitSerializeValue(ILGenerator il, TypeInfo type, ObjectSerializationInfo.EmittableMember member)
//        {
//            var t = member.Type;
//            if (MessagePackBinary.IsMessagePackPrimitive(t))
//            {
//                EmitOffsetPlusEqual(il, null, () =>
//                {
//                    il.EmitLoadArg(type, 3);
//                    member.EmitLoadValue(il);
//                    if (t == typeof(byte[]))
//                    {
//                        il.EmitCall(MessagePackBinaryTypeInfo.WriteBytes);
//                    }
//                    else
//                    {
//                        il.EmitCall(MessagePackBinaryTypeInfo.TypeInfo.GetDeclaredMethod("Write" + t.Name));
//                    }
//                });
//            }
//            else
//            {
//                EmitOffsetPlusEqual(il, () =>
//                {
//                    il.EmitLdarg(4);
//                    il.EmitCallvirt(rawGetFormatter.MakeGenericMethod(t));
//                }, () =>
//                {
//                    il.EmitLoadArg(type, 3);
//                    member.EmitLoadValue(il);
//                    il.EmitLdarg(4);
//                    il.EmitCallvirt(getSerialize(t));
//                });
//            }
//        }

//        // T Deserialize([arg:1]byte[] bytes, [arg:2]int offset, [arg:3]IFormatterResolver formatterResolver, [arg:4]out int readSize);
//        static void BuildDeserialize(Type type, ObjectSerializationInfo info, MethodBuilder method, FieldBuilder dictionaryField, ILGenerator il)
//        {
//            // if(value == null) readSize = 1, return null;
//            var falseLabel = il.DefineLabel();
//            il.EmitLdarg(1);
//            il.EmitLdarg(2);
//            il.EmitCall(MessagePackBinaryTypeInfo.IsNil);
//            il.Emit(OpCodes.Brfalse_S, falseLabel);
//            if (type.GetTypeInfo().IsClass)
//            {
//                il.EmitLdarg(4);
//                il.EmitLdc_I4(1);
//                il.Emit(OpCodes.Stind_I4);
//                il.Emit(OpCodes.Ldnull);
//                il.Emit(OpCodes.Ret);
//            }
//            else
//            {
//                typeof(System.InvalidOperationException).GetTypeInfo().DeclaredConstructors.First(x => { var p = x.GetParameters(); return p.Length == 1 && p[0].ParameterType == typeof(string); });

//                il.Emit(OpCodes.Ldstr, "typecode is null, struct not supported");
//                il.Emit(OpCodes.Newobj, invalidOperationExceptionConstructor);
//                il.Emit(OpCodes.Throw);
//            }

//            // var startOffset = offset;
//            il.MarkLabel(falseLabel);
//            var startOffsetLocal = il.DeclareLocal(typeof(int)); // [loc:0]
//            il.EmitLdarg(2);
//            il.EmitStloc(startOffsetLocal);

//            // var length = ReadMapHeader
//            var length = il.DeclareLocal(typeof(int)); // [loc:1]
//            il.EmitLdarg(1);
//            il.EmitLdarg(2);
//            il.EmitLdarg(4);
//            il.EmitCall(MessagePackBinaryTypeInfo.ReadMapHeader);
//            il.EmitStloc(length);
//            EmitOffsetPlusReadSize(il);

//            // make local fields
//            DeserializeInfo[] intList = null;
//            var temp = new List<DeserializeInfo>();
//            foreach (var item in info.Members)
//            {
//                temp.Add(new DeserializeInfo
//                {
//                    MemberInfo = item,
//                    LocalField = il.DeclareLocal(item.Type),
//                    SwitchLabel = il.DefineLabel()
//                });
//            }
//            intList = temp.ToArray();

//            // Read Loop(for var i = 0; i< length; i++)
//            {
//                var key = il.DeclareLocal(typeof(int));
//                var loopEnd = il.DefineLabel();
//                var switchDefault = il.DefineLabel();
//                var stringKeyTrue = il.DefineLabel();
//                il.EmitIncrementFor(length, forILocal =>
//                {
//                    if (info.IsIntKey)
//                    {
//                        // key = Deserialize, offset += readSize;
//                        il.EmitLdarg(1);
//                        il.EmitLdarg(2);
//                        il.EmitLdarg(4);
//                        il.EmitCall(MessagePackBinaryTypeInfo.ReadInt32);
//                        il.EmitStloc(key);
//                        EmitOffsetPlusReadSize(il);
//                    }
//                    else
//                    {
//                        // get string key -> dictionary lookup
//                        il.EmitLdarg(0);
//                        il.Emit(OpCodes.Ldfld, dictionaryField);
//                        il.EmitLdarg(1);
//                        il.EmitLdarg(2);
//                        il.EmitLdarg(4);
//                        il.EmitCall(MessagePackBinaryTypeInfo.ReadString);
//                        il.EmitLdloca(key);
//                        il.EmitCall(dictionaryTryGetValue);
//                        EmitOffsetPlusReadSize(il);
//                        il.Emit(OpCodes.Brtrue_S, stringKeyTrue);

//                        il.EmitLdarg(4);
//                        il.EmitLdarg(1);
//                        il.EmitLdarg(2);
//                        il.EmitCall(MessagePackBinaryTypeInfo.ReadNext);
//                        il.Emit(OpCodes.Stind_I4);
//                        il.Emit(OpCodes.Br, loopEnd);

//                        il.MarkLabel(stringKeyTrue);
//                    }

//                    // switch... local = Deserialize
//                    il.EmitLdloc(key);
//                    il.Emit(OpCodes.Switch, intList.Select(x => x.SwitchLabel).ToArray());
//                    foreach (var item in intList)
//                    {
//                        il.MarkLabel(item.SwitchLabel);
//                        EmitDeserializeValue(il, item);
//                        il.Emit(OpCodes.Br, loopEnd);
//                    }
//                    il.MarkLabel(switchDefault);
//                    // default, only read. readSize = MessagePackBinary.ReadNext(bytes, offset);
//                    il.EmitLdarg(4);
//                    il.EmitLdarg(1);
//                    il.EmitLdarg(2);
//                    il.EmitCall(MessagePackBinaryTypeInfo.ReadNext);
//                    il.Emit(OpCodes.Stind_I4);
//                    il.Emit(OpCodes.Br, loopEnd);

//                    // offset += readSize
//                    il.MarkLabel(loopEnd);
//                    EmitOffsetPlusReadSize(il);
//                });
//            }

//            // finish readSize: readSize = offset - startOffset;
//            il.EmitLdarg(4);
//            il.EmitLdarg(2);
//            il.EmitLdloc(startOffsetLocal);
//            il.Emit(OpCodes.Sub);
//            il.Emit(OpCodes.Stind_I4);

//            // create result object
//            EmitNewObject(il, type, info, intList);

//            il.Emit(OpCodes.Ret);
//        }

//        static void EmitOffsetPlusReadSize(ILGenerator il)
//        {
//            il.EmitLdarg(2);
//            il.EmitLdarg(4);
//            il.Emit(OpCodes.Ldind_I4);
//            il.Emit(OpCodes.Add);
//            il.EmitStarg(2);
//        }

//        static void EmitDeserializeValue(ILGenerator il, DeserializeInfo info)
//        {
//            var member = info.MemberInfo;
//            var t = member.Type;
//            if (MessagePackBinary.IsMessagePackPrimitive(t))
//            {
//                il.EmitLdarg(1);
//                il.EmitLdarg(2);
//                il.EmitLdarg(4);
//                if (t == typeof(byte[]))
//                {
//                    il.EmitCall(MessagePackBinaryTypeInfo.ReadBytes);
//                }
//                else
//                {
//                    il.EmitCall(MessagePackBinaryTypeInfo.TypeInfo.GetDeclaredMethod("Read" + t.Name));
//                }
//            }
//            else
//            {
//                il.EmitLdarg(3);
//                il.EmitCallvirt(rawGetFormatter.MakeGenericMethod(t));
//                il.EmitLdarg(1);
//                il.EmitLdarg(2);
//                il.EmitLdarg(3);
//                il.EmitLdarg(4);
//                il.EmitCallvirt(getDeserialize(t));
//            }

//            il.EmitStloc(info.LocalField);
//        }

//        static void EmitNewObject(ILGenerator il, Type type, ObjectSerializationInfo info, DeserializeInfo[] members)
//        {
//            if (info.IsClass)
//            {
//                foreach (var item in info.ConstructorParameters)
//                {
//                    var local = members.First(x => x.MemberInfo == item);
//                    il.EmitLdloc(local.LocalField);
//                }
//                il.Emit(OpCodes.Newobj, info.BestmatchConstructor);

//                foreach (var item in members.Where(x => x.MemberInfo.IsWritable))
//                {
//                    il.Emit(OpCodes.Dup);
//                    il.EmitLdloc(item.LocalField);
//                    item.MemberInfo.EmitStoreValue(il);
//                }
//            }
//            else
//            {
//                var result = il.DeclareLocal(type);
//                if (info.BestmatchConstructor == null)
//                {
//                    il.Emit(OpCodes.Ldloca, result);
//                    il.Emit(OpCodes.Initobj, type);
//                }
//                else
//                {
//                    foreach (var item in info.ConstructorParameters)
//                    {
//                        var local = members.First(x => x.MemberInfo == item);
//                        il.EmitLdloc(local.LocalField);
//                    }
//                    il.Emit(OpCodes.Newobj, info.BestmatchConstructor);
//                    il.Emit(OpCodes.Stloc, result);
//                }

//                foreach (var item in members.Where(x => x.MemberInfo.IsWritable))
//                {
//                    il.EmitLdloca(result);
//                    il.EmitLdloc(item.LocalField);
//                    item.MemberInfo.EmitStoreValue(il);
//                }

//                il.Emit(OpCodes.Ldloc, result);
//            }
//        }

//        // EmitInfos...

//        static readonly Type refByte = typeof(byte[]).MakeByRefType();
//        static readonly Type refInt = typeof(int).MakeByRefType();
//        static readonly MethodInfo rawGetFormatter = typeof(IFormatterResolver).GetRuntimeMethod("GetFormatter", Type.EmptyTypes);
//        static readonly Func<Type, MethodInfo> getSerialize = t => typeof(IMessagePackFormatter<>).MakeGenericType(t).GetRuntimeMethod("Serialize", new[] { refByte, typeof(int), t, typeof(IFormatterResolver) });
//        static readonly Func<Type, MethodInfo> getDeserialize = t => typeof(IMessagePackFormatter<>).MakeGenericType(t).GetRuntimeMethod("Deserialize", new[] { typeof(byte[]), typeof(int), typeof(IFormatterResolver), refInt });
//        static readonly ConstructorInfo dictionaryConstructor = typeof(Dictionary<string, int>).GetTypeInfo().DeclaredConstructors.First(x => { var p = x.GetParameters(); return p.Length == 1 && p[0].ParameterType == typeof(int); });
//        static readonly MethodInfo dictionaryAdd = typeof(Dictionary<string, int>).GetRuntimeMethod("Add", new[] { typeof(string), typeof(int) });
//        static readonly MethodInfo dictionaryTryGetValue = typeof(Dictionary<string, int>).GetRuntimeMethod("TryGetValue", new[] { typeof(string), refInt });
//        static readonly ConstructorInfo invalidOperationExceptionConstructor = typeof(System.InvalidOperationException).GetTypeInfo().DeclaredConstructors.First(x => { var p = x.GetParameters(); return p.Length == 1 && p[0].ParameterType == typeof(string); });

//        static class MessagePackBinaryTypeInfo
//        {
//            public static TypeInfo TypeInfo = typeof(MessagePackBinary).GetTypeInfo();

//            public static MethodInfo WriteFixedMapHeaderUnsafe = typeof(MessagePackBinary).GetRuntimeMethod("WriteFixedMapHeaderUnsafe", new[] { refByte, typeof(int), typeof(int) });
//            public static MethodInfo WriteMapHeader = typeof(MessagePackBinary).GetRuntimeMethod("WriteMapHeader", new[] { refByte, typeof(int), typeof(int) });
//            public static MethodInfo WritePositiveFixedIntUnsafe = typeof(MessagePackBinary).GetRuntimeMethod("WritePositiveFixedIntUnsafe", new[] { refByte, typeof(int), typeof(int) });
//            public static MethodInfo WriteInt32 = typeof(MessagePackBinary).GetRuntimeMethod("WriteInt32", new[] { refByte, typeof(int), typeof(int) });
//            public static MethodInfo WriteBytes = typeof(MessagePackBinary).GetRuntimeMethod("WriteBytes", new[] { refByte, typeof(int), typeof(byte[]) });
//            public static MethodInfo WriteNil = typeof(MessagePackBinary).GetRuntimeMethod("WriteNil", new[] { refByte, typeof(int) });
//            public static MethodInfo ReadBytes = typeof(MessagePackBinary).GetRuntimeMethod("ReadBytes", new[] { typeof(byte[]), typeof(int), refInt });
//            public static MethodInfo ReadInt32 = typeof(MessagePackBinary).GetRuntimeMethod("ReadInt32", new[] { typeof(byte[]), typeof(int), refInt });
//            public static MethodInfo ReadString = typeof(MessagePackBinary).GetRuntimeMethod("ReadString", new[] { typeof(byte[]), typeof(int), refInt });
//            public static MethodInfo IsNil = typeof(MessagePackBinary).GetRuntimeMethod("IsNil", new[] { typeof(byte[]), typeof(int) });
//            public static MethodInfo ReadNext = typeof(MessagePackBinary).GetRuntimeMethod("ReadNext", new[] { typeof(byte[]), typeof(int) });
//            public static MethodInfo WriteStringUnsafe = typeof(MessagePackBinary).GetRuntimeMethod("WriteStringUnsafe", new[] { refByte, typeof(int), typeof(string), typeof(int) });

//            public static MethodInfo ReadMapHeader = typeof(MessagePackBinary).GetRuntimeMethod("ReadMapHeader", new[] { typeof(byte[]), typeof(int), refInt });

//            static MessagePackBinaryTypeInfo()
//            {
//            }
//        }

//        class DeserializeInfo
//        {
//            public ObjectSerializationInfo.EmittableMember MemberInfo { get; set; }
//            public LocalBuilder LocalField { get; set; }
//            public Label SwitchLabel { get; set; }
//        }
//    }
//}

//namespace MessagePack.Internal
//{
//    public class ObjectSerializationInfo
//    {
//        public bool IsIntKey { get; set; }
//        public bool IsStringKey { get { return !IsIntKey; } }
//        public bool IsClass { get; set; }
//        public bool IsStruct { get { return !IsClass; } }
//        public ConstructorInfo BestmatchConstructor { get; set; }
//        public EmittableMember[] ConstructorParameters { get; set; }
//        public EmittableMember[] Members { get; set; }

//        ObjectSerializationInfo()
//        {

//        }

//        public static ObjectSerializationInfo CreateOrNull(Type type)
//        {
//            var ti = type.GetTypeInfo();
//            var isClass = ti.IsClass;

//            var contractAttr = ti.GetCustomAttribute<MessagePackObjectAttribute>();
//            if (contractAttr == null)
//            {
//                return null;
//            }

//            var isIntKey = true;
//            var intMemebrs = new Dictionary<int, EmittableMember>();
//            var stringMembers = new Dictionary<string, EmittableMember>();

//            if (contractAttr.KeyAsPropertyName)
//            {
//                // Opt-out: All public members are serialize target except [Ignore] member.
//                isIntKey = false;

//                var hiddenIntKey = 0;
//                foreach (var item in type.GetRuntimeProperties())
//                {
//                    if (item.GetCustomAttribute<IgnoreAttribute>(true) != null) continue;

//                    var member = new EmittableMember
//                    {
//                        PropertyInfo = item,
//                        IsReadable = (item.GetMethod != null) && item.GetMethod.IsPublic,
//                        IsWritable = (item.SetMethod != null) && item.SetMethod.IsPublic,
//                        StringKey = item.Name
//                    };
//                    if (!member.IsReadable && !member.IsWritable) continue;
//                    member.IntKey = hiddenIntKey++;
//                    stringMembers.Add(member.StringKey, member);
//                }
//                foreach (var item in type.GetRuntimeFields())
//                {
//                    if (item.GetCustomAttribute<IgnoreAttribute>(true) != null) continue;
//                    if (item.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>(true) != null) continue;

//                    var member = new EmittableMember
//                    {
//                        FieldInfo = item,
//                        IsReadable = item.IsPublic,
//                        IsWritable = item.IsPublic && !item.IsInitOnly,
//                        StringKey = item.Name
//                    };
//                    if (!member.IsReadable && !member.IsWritable) continue;
//                    member.IntKey = hiddenIntKey++;
//                    stringMembers.Add(member.StringKey, member);
//                }
//            }
//            else
//            {
//                // Opt-in: Only KeyAttribute members
//                var searchFirst = true;
//                var hiddenIntKey = 0;

//                foreach (var item in type.GetRuntimeProperties())
//                {
//                    if (item.GetCustomAttribute<IgnoreAttribute>(true) != null) continue;

//                    var key = item.GetCustomAttribute<KeyAttribute>(true);
//                    if (key == null) continue;

//                    if (key.IntKey == null && key.StringKey == null) throw new MessagePackDynamicObjectResolverException("both IntKey and StringKey are null." + " type: " + type.FullName + " member:" + item.Name);

//                    if (searchFirst)
//                    {
//                        searchFirst = false;
//                        isIntKey = key.IntKey != null;
//                    }
//                    else
//                    {
//                        if ((isIntKey && key.IntKey == null) || (!isIntKey && key.StringKey == null))
//                        {
//                            throw new MessagePackDynamicObjectResolverException("all members key type must be same." + " type: " + type.FullName + " member:" + item.Name);
//                        }
//                    }

//                    var member = new EmittableMember
//                    {
//                        PropertyInfo = item,
//                        IsReadable = (item.GetMethod != null) && item.GetMethod.IsPublic,
//                        IsWritable = (item.SetMethod != null) && item.SetMethod.IsPublic,
//                    };
//                    if (!member.IsReadable && !member.IsWritable) continue;

//                    if (isIntKey)
//                    {
//                        member.IntKey = key.IntKey.Value;
//                        if (intMemebrs.ContainsKey(member.IntKey)) throw new MessagePackDynamicObjectResolverException("key is duplicated, all members key must be unique." + " type: " + type.FullName + " member:" + item.Name);

//                        intMemebrs.Add(member.IntKey, member);
//                    }
//                    else
//                    {
//                        member.StringKey = key.StringKey;
//                        if (stringMembers.ContainsKey(member.StringKey)) throw new MessagePackDynamicObjectResolverException("key is duplicated, all members key must be unique." + " type: " + type.FullName + " member:" + item.Name);

//                        member.IntKey = hiddenIntKey++;
//                        stringMembers.Add(member.StringKey, member);
//                    }
//                }

//                foreach (var item in type.GetRuntimeFields())
//                {
//                    if (item.GetCustomAttribute<IgnoreAttribute>(true) != null) continue;
//                    if (item.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>(true) != null) continue;

//                    if (item.GetCustomAttribute<IgnoreAttribute>(true) != null) continue;

//                    var key = item.GetCustomAttribute<KeyAttribute>(true);
//                    if (key == null) continue;

//                    if (key.IntKey == null && key.StringKey == null) throw new MessagePackDynamicObjectResolverException("both IntKey and StringKey are null." + " type: " + type.FullName + " member:" + item.Name);

//                    if (searchFirst)
//                    {
//                        searchFirst = false;
//                        isIntKey = key.IntKey != null;
//                    }
//                    else
//                    {
//                        if ((isIntKey && key.IntKey == null) || (!isIntKey && key.StringKey == null))
//                        {
//                            throw new MessagePackDynamicObjectResolverException("all members key type must be same." + " type: " + type.FullName + " member:" + item.Name);
//                        }
//                    }

//                    var member = new EmittableMember
//                    {
//                        FieldInfo = item,
//                        IsReadable = item.IsPublic,
//                        IsWritable = item.IsPublic && !item.IsInitOnly,
//                    };
//                    if (!member.IsReadable && !member.IsWritable) continue;

//                    if (isIntKey)
//                    {
//                        member.IntKey = key.IntKey.Value;
//                        if (intMemebrs.ContainsKey(member.IntKey)) throw new MessagePackDynamicObjectResolverException("key is duplicated, all members key must be unique." + " type: " + type.FullName + " member:" + item.Name);

//                        intMemebrs.Add(member.IntKey, member);
//                    }
//                    else
//                    {
//                        member.StringKey = key.StringKey;
//                        if (stringMembers.ContainsKey(member.StringKey)) throw new MessagePackDynamicObjectResolverException("key is duplicated, all members key must be unique." + " type: " + type.FullName + " member:" + item.Name);

//                        member.IntKey = hiddenIntKey++;
//                        stringMembers.Add(member.StringKey, member);
//                    }
//                }
//            }

//            // GetConstructor
//            var ctor = ti.DeclaredConstructors.Where(x => x.IsPublic).SingleOrDefault(x => x.GetCustomAttribute<SerializationConstructorAttribute>(false) != null);
//            if (ctor == null)
//            {
//                ctor = ti.DeclaredConstructors.Where(x => x.IsPublic).OrderBy(x => x.GetParameters().Length).FirstOrDefault();
//            }
//            // struct allows null ctor
//            if (ctor == null && isClass) throw new MessagePackDynamicObjectResolverException("can't find public constructor. type:" + type.FullName);

//            var constructorParameters = new List<EmittableMember>();
//            if (ctor != null)
//            {
//                var constructorLookupDictionary = stringMembers.ToLookup(x => x.Key, x => x, StringComparer.OrdinalIgnoreCase);

//                var ctorParamIndex = 0;
//                foreach (var item in ctor.GetParameters())
//                {
//                    EmittableMember paramMember;
//                    if (isIntKey)
//                    {
//                        if (intMemebrs.TryGetValue(ctorParamIndex, out paramMember))
//                        {
//                            if (item.ParameterType == paramMember.Type && paramMember.IsReadable)
//                            {
//                                constructorParameters.Add(paramMember);
//                            }
//                            else
//                            {
//                                throw new MessagePackDynamicObjectResolverException("can't find matched constructor parameter, parameterType mismatch. type:" + type.FullName + " parameterIndex:" + ctorParamIndex + " paramterType:" + item.ParameterType.Name);
//                            }
//                        }
//                        else
//                        {
//                            throw new MessagePackDynamicObjectResolverException("can't find matched constructor parameter, index not found. type:" + type.FullName + " parameterIndex:" + ctorParamIndex);
//                        }
//                    }
//                    else
//                    {
//                        var hasKey = constructorLookupDictionary[item.Name];
//                        var len = hasKey.Count();
//                        if (len != 0)
//                        {
//                            if (len != 1)
//                            {
//                                throw new MessagePackDynamicObjectResolverException("duplicate matched constructor parameter name:" + type.FullName + " parameterName:" + item.Name + " paramterType:" + item.ParameterType.Name);
//                            }

//                            paramMember = hasKey.First().Value;
//                            if (item.ParameterType == paramMember.Type && paramMember.IsReadable)
//                            {
//                                constructorParameters.Add(paramMember);
//                            }
//                            else
//                            {
//                                throw new MessagePackDynamicObjectResolverException("can't find matched constructor parameter, parameterType mismatch. type:" + type.FullName + " parameterName:" + item.Name + " paramterType:" + item.ParameterType.Name);
//                            }
//                        }
//                        else
//                        {
//                            throw new MessagePackDynamicObjectResolverException("can't find matched constructor parameter, index not found. type:" + type.FullName + " parameterName:" + item.Name);
//                        }
//                    }
//                    ctorParamIndex++;
//                }
//            }

//            return new ObjectSerializationInfo
//            {
//                IsClass = isClass,
//                BestmatchConstructor = ctor,
//                ConstructorParameters = constructorParameters.ToArray(),
//                IsIntKey = isIntKey,
//                Members = (isIntKey) ? intMemebrs.Values.ToArray() : stringMembers.Values.ToArray()
//            };
//        }

//        public class EmittableMember
//        {
//            public bool IsProperty { get { return PropertyInfo != null; } }
//            public bool IsField { get { return FieldInfo != null; } }
//            public bool IsWritable { get; set; }
//            public bool IsReadable { get; set; }
//            public int IntKey { get; set; }
//            public string StringKey { get; set; }
//            public Type Type { get { return IsField ? FieldInfo.FieldType : PropertyInfo.PropertyType; } }
//            public FieldInfo FieldInfo { get; set; }
//            public PropertyInfo PropertyInfo { get; set; }

//            public void EmitLoadValue(ILGenerator il)
//            {
//                if (IsProperty)
//                {
//                    il.EmitCallvirt(PropertyInfo.GetMethod);
//                }
//                else
//                {
//                    il.Emit(OpCodes.Ldfld, FieldInfo);
//                }
//            }

//            public void EmitStoreValue(ILGenerator il)
//            {
//                if (IsProperty)
//                {
//                    il.EmitCallvirt(PropertyInfo.SetMethod);
//                }
//                else
//                {
//                    il.Emit(OpCodes.Stfld, FieldInfo);
//                }
//            }
//        }
//    }

//    public class MessagePackDynamicObjectResolverException : Exception
//    {
//        public MessagePackDynamicObjectResolverException(string message)
//            : base(message)
//        {

//        }
//    }
//}