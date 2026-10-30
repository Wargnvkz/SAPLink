using SAP.Middleware.Connector;
using SAPLink.Classes;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web;

namespace SAPLink
{
    /// <summary>
    /// Summary description for AppData
    /// 
    /// First operations with SAP can randomly throw System.NullReferenceException by unknown reason. It's a bug in SAP .NET Connector. It's a known issue. It doesn't affect the functionality of the application. Just ignore it.
    /// </summary>
    public sealed class SAPLink : IDisposable
    {
        static readonly ConnectionDestinationConfiguration Config = new ConnectionDestinationConfiguration();
        private string _destination = "SAPLink_" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// Initializes a new instance of the SAPLink class with the specified logon information.
        /// Registers the default destination configuration with the RfcDestinationManager.
        /// </summary>
        /// <param name="logon">The logon information for the SAP system.</param>
        public SAPLink(Logon logon)
        {
            Config.AddDestination(_destination, logon);
        }
        bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Config.RemoveDestination(_destination);
            GC.SuppressFinalize(this);
        }
        ~SAPLink()
        {
            try
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"SAPLink {_destination} haven't been disposed: Dispose/using was forgotten");
#endif
                Dispose();
            }
            catch { /* в финализаторе исключения недопустимы */ }
        }

        public PingError TryPing(out string error)
        {
            return Config.TryPing(_destination, out error);
        }

        public SapTransaction BeginTransaction() => new SapTransaction(Config, _destination);

        public List<T> GetTable<T>(string table, params string[] options)
        {
            return Config.GetTable<T>(_destination, table, options);
        }
        public List<T> GetTable<T>(string table, string options = "")
        {
            var strarr = SplitString(options, 60);
            return Config.GetTable<T>(_destination, table, strarr.ToArray());
        }
        public List<T> GetTable<T>(string table, int variant, string options = "")
        {
            var strarr = SplitString(options, 60);
            return Config.GetTable<T>(_destination, table, variant, strarr.ToArray());
        }

        public List<T> GetTableBitVariant<T>(string table, string options = "")
        {
            var strarr = SplitString(options, 60);
            return Config.GetTableBitVariant<T>(_destination, table, strarr.ToArray());
        }
        public List<T> GetTableBitVariant<T>(string table, int variant, string options = "")
        {
            var strarr = SplitString(options, 60);
            return Config.GetTableBitVariant<T>(_destination, table, variant, strarr.ToArray());
        }

        public List<string> GetTable(string tablename, List<string> fields, List<string> where)
        {
            return Config.GetTable(_destination, tablename, fields, where);
        }

        private static List<string> SplitString(string str, int nSize)
        {
            var list = new List<string>();

            if (string.IsNullOrEmpty(str) || nSize <= 0)
                return list;

            int lastIndex = 0;

            while (lastIndex < str.Length)
            {
                // Если оставшийся кусок строки меньше или равен nSize, забираем его целиком
                if (str.Length - lastIndex <= nSize)
                {
                    list.Add(str.Substring(lastIndex));
                    break;
                }

                // Вычисляем крайнюю правую точку текущего блока для поиска пробела
                int searchEndIndex = lastIndex + nSize;

                // Ищем последний пробел ВНУТРИ текущего блока nSize
                // Начинаем с конца блока (searchEndIndex) и осматриваем nSize символов назад
                int spaceIndex = str.LastIndexOf(' ', searchEndIndex, nSize);

                if (spaceIndex >= lastIndex)
                {
                    // Нашли пробел, режем по нему
                    list.Add(str.Substring(lastIndex, spaceIndex - lastIndex));
                    lastIndex = spaceIndex + 1; // Пропускаем сам пробел
                }
                else
                {
                    // Пробелов нет (очень длинное слово). Забираем nSize символов "как есть"
                    list.Add(str.Substring(lastIndex, nSize));
                    lastIndex += nSize;
                }
            }

            return list;
        }
        /// <summary>
        /// Calls function module with appropriate parameters. Name of fields must be the same as in SAP
        /// </summary>
        /// <param name="FunctionName">Name of SAP function module</param>
        /// <param name="Data">List of input parameters: imports and tables(IEnumerable)</param>
        /// <param name="Output">Description of output parameters. Set type of parameters for exports and generic type of List<> for tables. If table return strings type should be string, not List</param>
        /// <returns>Dictionary of results of exports and tables values. tables are strings of type set in Output.</returns>

        public Dictionary<string, object> CallFunction(string FunctionName, Dictionary<string, object> Data, Dictionary<string, Type> Output)
        {
            return Config.CallFunction(_destination, FunctionName, Data, Output);
        }
    }


}