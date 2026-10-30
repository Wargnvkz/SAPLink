using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SAPLink.Classes
{
    internal class ProcessDataTools
    {
        protected internal static object GetValue(System.Reflection.MemberInfo mi, object o)
        {
            if (mi is System.Reflection.FieldInfo)
            {
                return (mi as System.Reflection.FieldInfo).GetValue(o);
            }
            if (mi is System.Reflection.PropertyInfo)
            {
                return (mi as System.Reflection.PropertyInfo).GetValue(o, null);
            }
            return null;
        }
        protected internal static Type GetMemberType(System.Reflection.MemberInfo mi)
        {
            if (mi is System.Reflection.FieldInfo)
            {
                return (mi as System.Reflection.FieldInfo).FieldType;
            }
            if (mi is System.Reflection.PropertyInfo)
            {
                return (mi as System.Reflection.PropertyInfo).PropertyType;
            }
            return null;
        }

        protected internal static void SetValue(System.Reflection.MemberInfo mi, object o, object value)
        {
            if (mi is System.Reflection.FieldInfo)
            {
                (mi as System.Reflection.FieldInfo).SetValue(o, value);
            }
            if (mi is System.Reflection.PropertyInfo)
            {
                try
                {
                    (mi as System.Reflection.PropertyInfo).SetValue(o, value, null);
                }
                catch (ArgumentException)
                {
                }
            }
        }
        protected internal static object GetValue(string name, object o)
        {
            var mis = GetFieldAndProperies(o.GetType());
            var mi = mis.Find(m => m.Name == name);
            if (mi == null) return null;
            if (mi is System.Reflection.FieldInfo)
            {
                return (mi as System.Reflection.FieldInfo).GetValue(o);
            }
            if (mi is System.Reflection.PropertyInfo)
            {
                return (mi as System.Reflection.PropertyInfo).GetValue(o, null);
            }
            return null;
        }
        protected internal static Type GetMemberType(string name, Type type)
        {
            var mis = GetFieldAndProperies(type);
            var mi = mis.Find(m => m.Name == name);
            if (mi == null) return null;
            if (mi is System.Reflection.FieldInfo)
            {
                return (mi as System.Reflection.FieldInfo).FieldType;
            }
            if (mi is System.Reflection.PropertyInfo)
            {
                return (mi as System.Reflection.PropertyInfo).PropertyType;
            }
            return null;
        }

        protected internal static void SetValue(string name, object o, object value)
        {
            var mis = GetFieldAndProperies(o.GetType());
            var mi = mis.Find(m => m.Name == name);
            if (mi == null) return;
            if (mi is System.Reflection.FieldInfo)
            {
                (mi as System.Reflection.FieldInfo).SetValue(o, value);
            }
            if (mi is System.Reflection.PropertyInfo)
            {
                try
                {
                    (mi as System.Reflection.PropertyInfo).SetValue(o, value, null);
                }
                catch (ArgumentException)
                {
                }
            }
        }
        protected internal static List<System.Reflection.MemberInfo> GetFieldAndProperies<T>()
        {
            var type = typeof(T);
            return GetFieldAndProperies(type);
        }
        protected internal static List<System.Reflection.MemberInfo> GetFieldAndProperies(Type type)
        {
            var Members = type.GetFields().Cast<System.Reflection.MemberInfo>().ToList();
            Members.AddRange(type.GetProperties().Cast<System.Reflection.MemberInfo>().ToList());
            return Members;
        }
        public static bool CheckIfAnonymousType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException("type");

            // HACK: The only way to detect anonymous types right now.
            return Attribute.IsDefined(type, typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)
                && type.IsGenericType && type.Name.Contains("AnonymousType")
                && (type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))
                && (type.Attributes & TypeAttributes.NotPublic) == TypeAttributes.NotPublic;
        }
    }
}
