using SAP.Middleware.Connector;
using SAPLink.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SAPLink.Classes
{
    /// <summary>
    /// Аттрибут исключающий поле из чтения
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class SAPExcludeFromReadingAttribute : Attribute
    {
    }
    /// <summary>
    /// Аттрибут поля изменяющий параметры чтения
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SAPGetTableAttribute : Attribute
    {
        public string FieldName;
        public int[] Variants = new int[0];
        public SAPType SAPType;
        //public int SAPLength;

        /// <summary>
        /// Конструктор для изменения имени поля
        /// </summary>
        /// <param name="SAPFieldname">Имя поля в таблице SAP</param>
        public SAPGetTableAttribute(string SAPFieldname)
        {
            FieldName = SAPFieldname;
        }
        /// <summary>
        /// Конструктор для вариантов чтения
        /// </summary>
        /// <param name="CallVariants">Список вариантов в которых это поле читается. -1 не используется</param>
        public SAPGetTableAttribute(params int[] CallVariants)
        {
            Variants = CallVariants;
        }
        /// <summary>
        /// Устанавливает тип поля в SAP. Если тип поля подходящий, то поле будет преобразовано в соответствующий тип.
        /// </summary>
        /// <param name="SAPType">Тип поля в SAP</param>
        ///// <param name="Length">Длина поля</param>
        public SAPGetTableAttribute(SAPType SAPType)//, int Length)
        {
            this.SAPType = SAPType;
            //SAPLength=Length;
        }

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="SAPFieldname">Имя поля в таблице SAP</param>
        /// <param name="CallVariants">Список вариантов в которых это поле читается. -1 не используется. Если вариантов нет - используется всегда</param>
        /// <param name="SAPType">Тип поля в SAP. Если тип поля в классе подходящий, то поле будет преобразовано в соответствующий тип.</param>
        ///// <param name="Length">Длина поля</param> 
        public SAPGetTableAttribute(string SAPFieldname, SAPType SAPType/*, int Length*/, params int[] CallVariants)
        {
            FieldName = SAPFieldname;
            Variants = CallVariants;
            this.SAPType = SAPType;
            //SAPLength=Length;
        }
        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="SAPFieldname">Имя поля в таблице SAP</param>
        /// <param name="CallVariants">Список вариантов в которых это поле читается. -1 не используется. Если вариантов нет - используется всегда</param>
        /// <param name="SAPType">Тип поля в SAP. Если тип поля в классе подходящий, то поле будет преобразовано в соответствующий тип.</param>
        ///// <param name="Length">Длина поля</param> 
        public SAPGetTableAttribute(string SAPFieldname, params int[] CallVariants)
        {
            FieldName = SAPFieldname;
            Variants = CallVariants;
            this.SAPType = SAPType.Corresponded;
            //SAPLength=Length;
        }
    }
    public enum SAPType
    {
        Corresponded,
        DATE,
        TIME
    }

    public class Logon
    {
        public string Host;
        public string Instance;
        public string InstanceName;
        public string Login;
        public string Password;
        public string Client;
        public string Language = "RU";
        public string PoolSize = "5";

        public Logon(string host, string instance, string instancename, string login, string password, string client, string language, string poolsize)
        {
            Host = host;
            Instance = instance;
            InstanceName = instancename;
            Login = login;
            Password = password;
            Client = client;
            Language = language;
            PoolSize = poolsize;
        }

    }

    public enum PingError
    {
        /// <summary>
        /// No error occurred during the ping operation.
        /// </summary>
        None,
        /// <summary>
        /// host unreachable, incorrect address or port, network issues, firewall, or timeout.
        /// </summary>
        ConnectionError,
        /// <summary>
        /// invalid user, password, or client; or the user is locked.
        /// </summary>
        LogonError,
        /// <summary>
        /// destination not found or an error in the parameters(e.g., host not specified).
        /// </summary>
        FunctionCallError,
        /// <summary>
        /// other errors, such as issues with the SAP system itself or unexpected exceptions.
        /// </summary>
        OtherError
    }
}
