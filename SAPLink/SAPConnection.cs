using SAP.Middleware.Connector;
using SAPLink.Classes;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Policy;
using System.Threading;

namespace SAPLink
{
    internal sealed class ConnectionDestinationConfiguration : SAP.Middleware.Connector.IDestinationConfiguration
    {
        static bool registered;
        static readonly object Sync = new object();
        readonly ConcurrentDictionary<string, Logon> logons = new ConcurrentDictionary<string, Logon>();

        public bool ChangeEventsSupported()
        {
            return true;
        }

        public event RfcDestinationManager.ConfigurationChangeHandler ConfigurationChanged;

        public void AddDestination(string name, Logon logon)
        {
            lock (Sync)
            {
                if (!logons.TryAdd(name, logon))
                    throw new InvalidOperationException($"Destination '{name}' already exists");
                EnsureRegistered();
            }
        }

        public void RemoveDestination(string name)
        {
            lock (Sync)
            {
                if (!logons.TryRemove(name, out _)) return;
                // просим NCo закрыть пул этого destination
                ConfigurationChanged?.Invoke(name,
                    new RfcConfigurationEventArgs(RfcConfigParameters.EventType.DELETED));
                if (logons.IsEmpty && registered)
                {
                    RfcDestinationManager.UnregisterDestinationConfiguration(this);
                    registered = false;
                }
            }
        }
        void EnsureRegistered()
        {
            if (registered) return;
            RfcDestinationManager.RegisterDestinationConfiguration(this);
            registered = true;
        }

        public RfcConfigParameters GetParameters(string destinationName)
        {

            if (!logons.TryGetValue(destinationName, out var l)) return null;

            RfcConfigParameters parms = new RfcConfigParameters();

            parms.Add(RfcConfigParameters.AppServerHost, l.Host);
            parms.Add(RfcConfigParameters.SystemNumber, l.Instance);
            parms.Add(RfcConfigParameters.SystemID, l.InstanceName);
            parms.Add(RfcConfigParameters.User, l.Login);
            parms.Add(RfcConfigParameters.Password, l.Password);
            parms.Add(RfcConfigParameters.Client, l.Client);
            parms.Add(RfcConfigParameters.Language, l.Language);
            parms.Add(RfcConfigParameters.PoolSize, l.PoolSize);
            parms.Add(RfcConfigParameters.ConnectionIdleTimeout, "5");
            parms.Add(RfcConfigParameters.PeakConnectionsLimit, "24");
            return parms;

        }

        public PingError TryPing(string DestinationName, out string error)
        {
            try
            {
                RfcDestinationManager.GetDestination(DestinationName).Ping();
                error = null;
                return PingError.None;
            }
            //host unreachable, incorrect address or port, network issues, firewall, or timeout.
            catch (RfcCommunicationException e) { error = e.Message; return PingError.ConnectionError; }
            //invalid user, password, or client; or the user is locked.
            catch (RfcLogonException e) { error = e.Message; return PingError.LogonError; }
            //destination not found or an error in the parameters(e.g., host not specified).
            catch (RfcInvalidParameterException e) { error = e.Message; return PingError.FunctionCallError; }
            // other errors, such as issues with the SAP system itself or unexpected exceptions.
            catch (RfcAbapBaseException e) { error = e.Message; return PingError.OtherError; }
            catch (RfcBaseException e) { error = e.Message; return PingError.OtherError; }

        }

        /// <summary>
        /// Get Data from SAP table
        /// </summary>
        /// <param name="tablename">SAP table to read</param>
        /// <param name="fields">List of technical names of fields</param>
        /// <param name="where">Condition: Field = 'condition' or others</param>
        /// <returns>List of table rows with values separated with semicolon</returns>
        public List<string> GetTable(string DestinationName, string tablename, List<string> fields, List<string> where, string delimiter = ";")
        {

            RfcDestination dest = RfcDestinationManager.GetDestination(DestinationName);

            RfcRepository repo = dest.Repository;

            IRfcFunction readTable = repo.CreateFunction("RFC_READ_TABLE");

            // we want to query table KNA1
            readTable.SetValue("QUERY_TABLE", tablename);
            // fields will be separated by semicolon
            readTable.SetValue("DELIMITER", delimiter);

            if (fields != null && fields.Count > 0)
            {
                IRfcTable fieldsTable = readTable.GetTable("FIELDS");
                for (int i = 0; i < fields.Count; i++)
                {
                    fieldsTable.Append();
                    fieldsTable.SetValue("FIELDNAME", fields[i]);
                }
            }


            // the table OPTIONS contains the WHERE condition(s) of your query
            // here a single condition, KUNNR is to be 0012345600
            // several conditions have to be concatenated in ABAP syntax, for instance with AND or OR
            //            optsTable.SetValue("TEXT", "KUNNR = '0012345600'");
            if (where != null && where.Count > 0)
            {
                IRfcTable optsTable = readTable.GetTable("OPTIONS");
                for (int i = 0; i < where.Count; i++)
                {
                    optsTable.Append();
                    optsTable.SetValue("TEXT", where[i]);
                }
            }

            readTable.Invoke(dest);

            IRfcTable dataTable = readTable.GetTable("DATA");

            List<string> res = new List<string>();

            foreach (var dataRow in dataTable)
            {
                string data = (string)dataRow.GetValue("WA");
                res.Add(data);
            }
            return res;
        }


        public List<T> GetTable<T>(string DestinationName, string table, params string[] options)
        {
            return GetTable<T>(DestinationName, table, -1, options);
        }

        /// <summary>
        /// Чтение данных из SAP
        /// </summary>
        /// <typeparam name="T">Тип возвращаемых данных</typeparam>
        /// <param name="table">Имя таблицы из которой читаются данные</param>
        /// <param name="variant">Номер варианта чтения - описываются аттрибутами в классе. Вариант -1 читает все поля.</param>
        /// <param name="options">Строка условия разделенная на несколько из-за особенностей SAP</param>
        /// <returns>Список записей таблицы SAP</returns>
        public List<T> GetTable<T>(string DestinationName, string table, int variant, params string[] options)
        {
            string delimiter = "\uffff";
            var selectedLanguage = "ru-RU";
            Thread.CurrentThread.CurrentCulture =
                CultureInfo.CreateSpecificCulture(selectedLanguage);
            Thread.CurrentThread.CurrentUICulture = new
                CultureInfo(selectedLanguage);

            Type tt = typeof(T);

            RfcDestination dest = RfcDestinationManager.GetDestination(DestinationName);

            RfcRepository repo = dest.Repository;

            IRfcFunction readTable = repo.CreateFunction("RFC_READ_TABLE");

            // we want to query table KNA1
            readTable.SetValue("QUERY_TABLE", table);
            // fields will be separated by semicolon
            readTable.SetValue("DELIMITER", delimiter);

            var fields = tt.GetFields().ToList();
            fields.AddRange(tt.GetFields(System.Reflection.BindingFlags.NonPublic |
                 System.Reflection.BindingFlags.Instance));
            List<int> FieldsN = new List<int>();

            if (fields != null && fields.Count > 0)
            {
                IRfcTable fieldsTable = readTable.GetTable("FIELDS");
                for (int i = 0; i < fields.Count; i++)
                {
                    var o = fields[i].GetCustomAttributes(typeof(SAPExcludeFromReadingAttribute), false);
                    if (o.Length == 0)
                    {
                        var attr1 = fields[i].GetCustomAttributes(typeof(SAPGetTableAttribute), false);
                        if (attr1.Length == 0)
                        {
                            FieldsN.Add(i);
                            fieldsTable.Append();
                            fieldsTable.SetValue("FIELDNAME", fields[i].Name);
                        }
                        else
                        {
                            var sattr = ((SAPGetTableAttribute)attr1[0]);
                            var variants = sattr.Variants;
                            if (variant == -1 || variants.Contains(variant))
                            {

                                var fn = sattr.FieldName;
                                if (String.IsNullOrEmpty(fn)) fn = fields[i].Name;

                                FieldsN.Add(i);
                                fieldsTable.Append();
                                fieldsTable.SetValue("FIELDNAME", fn);
                            }
                        }
                    }
                }
            }

            if (options.Length > 0)
            {
                IRfcTable optsTable = readTable.GetTable("OPTIONS");
                for (int i = 0; i < options.Length; i++)
                {
                    optsTable.Append();
                    optsTable.SetValue("TEXT", options[i]);
                }
            }

            readTable.Invoke(dest);

            IRfcTable dataTable = readTable.GetTable("DATA");

            List<T> res = new List<T>();

            foreach (var dataRow in dataTable)
            {
                string data = (string)dataRow.GetValue("WA");
                var cols = data.Split(new string[] { delimiter }, StringSplitOptions.None);
                var t = (T)Activator.CreateInstance(typeof(T));
                int min = cols.Length;// Math.Min(cols.Length, fields.Count);
                if (min > 0)
                {
                    for (int i = 0; i < min; i++)
                    {
                        var n = FieldsN[i];

                        var attr1 = fields[n].GetCustomAttributes(typeof(SAPGetTableAttribute), false);
                        if (attr1.Length > 0)
                        {
                            var sattr = ((SAPGetTableAttribute)attr1[0]);
                            switch (sattr.SAPType)
                            {
                                case SAPType.Corresponded:
                                    {
                                        var sdata = cols[i];
                                        if (fields[n].FieldType != typeof(string)) sdata = sdata.Trim();
                                        try
                                        {
                                            object v;
                                            if (fields[n].FieldType.Equals(typeof(double)) || fields[n].FieldType.Equals(typeof(float)))
                                            {
                                                var s = sdata.Replace(",", "");
                                                s = s.Replace('.', ',');
                                                v = Convert.ChangeType(s, fields[n].FieldType);
                                            }
                                            else
                                            {
                                                v = Convert.ChangeType(cols[i], fields[n].FieldType);
                                            }
                                            fields[n].SetValue(t, v);
                                        }
                                        catch (Exception)
                                        {
                                        }
                                        break;
                                    }
                                case SAPType.DATE:
                                    {
                                        if (fields[n].FieldType == typeof(DateTime))
                                        {
                                            DateTime dt;
                                            if (DateTime.TryParseExact(cols[i], "yyyyMMdd", System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out dt))
                                            {
                                                fields[n].SetValue(t, dt);
                                            }
                                        }
                                        else
                                        {
                                            var v = Convert.ChangeType(cols[i], fields[n].FieldType);
                                            fields[n].SetValue(t, v);
                                        }

                                        break;
                                    }
                                case SAPType.TIME:
                                    {
                                        if (fields[n].FieldType == typeof(TimeSpan))
                                        {
                                            TimeSpan tm;
                                            if (TimeSpan.TryParseExact(cols[i], "hhmmss", System.Globalization.CultureInfo.CurrentCulture, out tm))
                                            {
                                                fields[n].SetValue(t, tm);
                                            }
                                        }
                                        else
                                        {
                                            var v = Convert.ChangeType(cols[i], fields[n].FieldType);
                                            fields[n].SetValue(t, v);
                                        }
                                        break;
                                    }
                            }
                        }
                        else
                        {
                            object v;
                            if (fields[n].FieldType.Equals(typeof(double)))
                            {
                                var s = cols[i].Replace(",", "");
                                s = s.Replace('.', ',');
                                v = Convert.ChangeType(s, fields[n].FieldType);
                            }
                            else
                            {
                                v = Convert.ChangeType(cols[i], fields[n].FieldType);
                            }
                            fields[n].SetValue(t, v);
                        }


                    }
                }
                res.Add(t);

            }
            return res;
        }


        public List<T> GetTableBitVariant<T>(string DestinationName, string table, params string[] options)
        {
            return GetTableBitVariant<T>(DestinationName, table, -1, options);
        }

        /// <summary>
        /// Чтение данных из SAP
        /// </summary>
        /// <typeparam name="T">Тип возвращаемых данных</typeparam>
        /// <param name="table">Имя таблицы из которой читаются данные</param>
        /// <param name="variant">Битовый вариант чтения полей. Вариант -1 читает все поля.</param>
        /// <param name="options">Строка условия разделенная на несколько из-за особенностей SAP</param>
        /// <returns>Список записей таблицы SAP</returns>
        public List<T> GetTableBitVariant<T>(string DestinationName, string table, long variant, params string[] options)
        {
            string delimiter = "\uffff";
            var selectedLanguage = "ru-RU";
            Thread.CurrentThread.CurrentCulture =
                CultureInfo.CreateSpecificCulture(selectedLanguage);
            Thread.CurrentThread.CurrentUICulture = new
                CultureInfo(selectedLanguage);

            Type tt = typeof(T);

            RfcDestination dest = RfcDestinationManager.GetDestination(DestinationName);

            RfcRepository repo = dest.Repository;

            IRfcFunction readTable = repo.CreateFunction("RFC_READ_TABLE");

            // we want to query table KNA1
            readTable.SetValue("QUERY_TABLE", table);
            // fields will be separated by semicolon
            readTable.SetValue("DELIMITER", delimiter);

            var fields = tt.GetFields().ToList();
            fields.AddRange(tt.GetFields(System.Reflection.BindingFlags.NonPublic |
                 System.Reflection.BindingFlags.Instance));
            List<int> FieldsN = new List<int>();

            if (fields != null && fields.Count > 0)
            {
                IRfcTable fieldsTable = readTable.GetTable("FIELDS");
                for (int i = 0; i < fields.Count; i++)
                {
                    var o = fields[i].GetCustomAttributes(typeof(SAPExcludeFromReadingAttribute), false);
                    if (o.Length == 0)
                    {
                        var attr1 = fields[i].GetCustomAttributes(typeof(SAPGetTableAttribute), false);
                        if (attr1.Length == 0)
                        {
                            FieldsN.Add(i);
                            fieldsTable.Append();
                            fieldsTable.SetValue("FIELDNAME", fields[i].Name);
                        }
                        else
                        {
                            var sattr = ((SAPGetTableAttribute)attr1[0]);
                            var variants = sattr.Variants;
                            if (variant == -1 || (variant & i) == i)
                            {

                                var fn = sattr.FieldName;
                                if (String.IsNullOrEmpty(fn)) fn = fields[i].Name;

                                FieldsN.Add(i);
                                fieldsTable.Append();
                                fieldsTable.SetValue("FIELDNAME", fn);
                            }
                        }
                    }
                }
            }

            if (options.Length > 0)
            {
                IRfcTable optsTable = readTable.GetTable("OPTIONS");
                for (int i = 0; i < options.Length; i++)
                {
                    optsTable.Append();
                    optsTable.SetValue("TEXT", options[i]);
                }
            }

            readTable.Invoke(dest);

            IRfcTable dataTable = readTable.GetTable("DATA");

            List<T> res = new List<T>();

            foreach (var dataRow in dataTable)
            {
                string data = (string)dataRow.GetValue("WA");
                var cols = data.Split(new string[] { delimiter }, StringSplitOptions.None);
                var t = (T)Activator.CreateInstance(typeof(T));
                int min = cols.Length;// Math.Min(cols.Length, fields.Count);
                if (min > 0)
                {
                    for (int i = 0; i < min; i++)
                    {
                        var n = FieldsN[i];

                        var attr1 = fields[n].GetCustomAttributes(typeof(SAPGetTableAttribute), false);
                        if (attr1.Length > 0)
                        {
                            var sattr = ((SAPGetTableAttribute)attr1[0]);
                            switch (sattr.SAPType)
                            {
                                case SAPType.Corresponded:
                                    {
                                        var sdata = cols[i];
                                        if (fields[n].FieldType != typeof(string)) sdata = sdata.Trim();
                                        try
                                        {
                                            object v;
                                            if (fields[n].FieldType.Equals(typeof(double)) || fields[n].FieldType.Equals(typeof(float)))
                                            {
                                                var s = sdata.Replace(",", "");
                                                s = s.Replace('.', ',');
                                                v = Convert.ChangeType(s, fields[n].FieldType);
                                            }
                                            else
                                            {
                                                v = Convert.ChangeType(cols[i], fields[n].FieldType);
                                            }
                                            fields[n].SetValue(t, v);
                                        }
                                        catch (Exception)
                                        {
                                        }
                                        break;
                                    }
                                case SAPType.DATE:
                                    {
                                        if (fields[n].FieldType == typeof(DateTime))
                                        {
                                            DateTime dt;
                                            if (DateTime.TryParseExact(cols[i], "yyyyMMdd", System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out dt))
                                            {
                                                fields[n].SetValue(t, dt);
                                            }
                                        }
                                        else
                                        {
                                            var v = Convert.ChangeType(cols[i], fields[n].FieldType);
                                            fields[n].SetValue(t, v);
                                        }

                                        break;
                                    }
                                case SAPType.TIME:
                                    {
                                        if (fields[n].FieldType == typeof(TimeSpan))
                                        {
                                            TimeSpan tm;
                                            if (TimeSpan.TryParseExact(cols[i], "hhmmss", System.Globalization.CultureInfo.CurrentCulture, out tm))
                                            {
                                                fields[n].SetValue(t, tm);
                                            }
                                        }
                                        else
                                        {
                                            var v = Convert.ChangeType(cols[i], fields[n].FieldType);
                                            fields[n].SetValue(t, v);
                                        }
                                        break;
                                    }
                            }
                        }
                        else
                        {
                            object v;
                            if (fields[n].FieldType.Equals(typeof(double)))
                            {
                                var s = cols[i].Replace(",", "");
                                s = s.Replace('.', ',');
                                v = Convert.ChangeType(s, fields[n].FieldType);
                            }
                            else
                            {
                                v = Convert.ChangeType(cols[i], fields[n].FieldType);
                            }
                            fields[n].SetValue(t, v);
                        }


                    }
                }
                res.Add(t);

            }
            return res;
        }

        public void CallFunctionNoParameters(string DestinationName, string function)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination(DestinationName);

            RfcRepository repo = dest.Repository;

            IRfcFunction readTable = repo.CreateFunction(function);
            readTable.Invoke(dest);

        }
        /// <summary>
        /// Calls function module with appropriate parameters. Name of fields must be the same as in SAP
        /// </summary>
        /// <param name="DestinationName">destination name</param>
        /// <param name="functionName">Name of SAP function module</param>
        /// <param name="Data">List of input parameters: imports and tables(IEnumerable)</param>
        /// <param name="Output">Description of output parameters. Set type of parameters for exports and generic type of List<> for tables. If table return strings type should be string, not List</param>
        /// <returns>Dictionary of results of exports and tables values. tables are strings of type set in Output.</returns>
        public Dictionary<string, object> CallFunction(string DestinationName, string functionName, Dictionary<string, object> Data, Dictionary<string, Type> Output)
        {
            //foreach (var ot in Output) if (CheckIfAnonymousType(ot.Value)) throw new Exception("Anonymous types are not alowed as output type");
            if (Data == null) Data = new Dictionary<string, object>();

            RfcDestination dest = RfcDestinationManager.GetDestination(DestinationName);

            RfcRepository repo = dest.Repository;

            IRfcFunction readTable = repo.CreateFunction(functionName);

            Dictionary<string, string> Upper2Key = new Dictionary<string, string>();
            Data.Keys.ToList().ForEach(k =>
                {
                    var uk = k.ToUpper();
                    if (!Upper2Key.ContainsKey(uk))
                        Upper2Key.Add(uk, k);
                    else
                        throw new ArgumentException($"Parameter {uk} was already used. Names of SAP parameters are case insensetive", nameof(Data));
                }
            );
            for (int i = 0; i < readTable.Count; i++)
            {
                var param = readTable[i];
                var md = param.Metadata;
                if (md.Direction == RfcDirection.EXPORT) continue;
                var name = md.Name.ToUpper();
                if (Upper2Key.ContainsKey(name))
                {
                    var key = Upper2Key[name];
                    var data = Data[key];
                    switch (md.Direction)
                    {
                        case RfcDirection.IMPORT:
                            if (md.DataType == RfcDataType.STRUCTURE)
                            {
                                var structure = readTable.GetStructure(name);
                                if (md.ValueMetadataAsStructureMetadata != null)
                                {
                                    var mds = md.ValueMetadataAsStructureMetadata;
                                    var type = data.GetType();
                                    var mis = ProcessDataTools.GetFieldAndProperies(type);
                                    for (int f = 0; f < mds.FieldCount; f++)
                                    {
                                        var fd = mds[f];
                                        var fieldname = fd.Name;
                                        var fnu = fieldname.ToUpper();
                                        var mi = mis.Find(m => m.Name.ToUpper() == fnu);
                                        if (mi == null) continue;
                                        var value = ProcessDataTools.GetValue(mi, data);
                                        structure.SetValue(fieldname, value);
                                    }

                                }

                            }
                            else
                            {
                                readTable.SetValue(name, data);
                            }
                            break;
                        case RfcDirection.TABLES:
                            {
                                if (!(data is IEnumerable)) continue;

                                var table = readTable.GetTable(name);
                                if (md.ValueMetadataAsTableMetadata != null)
                                {
                                    var mdt = md.ValueMetadataAsTableMetadata;
                                    var type = data.GetType();
                                    var subtypes = type.GetGenericArguments();
                                    var eltype = subtypes[0];//type.GetElementType();
                                    var mis = ProcessDataTools.GetFieldAndProperies(eltype);
                                    foreach (var o in data as IEnumerable)
                                    {
                                        table.Append();
                                        for (int f = 0; f < mdt.LineType.FieldCount; f++)
                                        {
                                            var fd = mdt.LineType[f];
                                            var fieldname = fd.Name;
                                            var fnu = fieldname.ToUpper();
                                            var mi = mis.Find(m => m.Name.ToUpper() == fnu);
                                            if (mi == null) continue;
                                            var value = ProcessDataTools.GetValue(mi, o);
                                            table.SetValue(fieldname, value);
                                        }
                                    }
                                }
                            }

                            break;
                    }

                }
            }

            readTable.Invoke(dest);

            if (Output == null || Output.Count == 0) return null;
            Dictionary<string, string> Upper2KeyRes = new Dictionary<string, string>();
            Output.Keys.ToList().ForEach(k => Upper2KeyRes.Add(k.ToUpper(), k));

            Dictionary<string, object> Result = new Dictionary<string, object>();

            for (int i = 0; i < readTable.Count; i++)
            {
                var param = readTable[i];
                var md = param.Metadata;
                if (md.Direction == RfcDirection.IMPORT) continue;
                var name = md.Name.ToUpper();
                if (Upper2KeyRes.ContainsKey(name))
                {
                    var key = Upper2KeyRes[name];
                    var type = Output[key];
                    var data = readTable[name];
                    switch (md.Direction)
                    {
                        case RfcDirection.EXPORT:
                            if (md.DataType == RfcDataType.STRUCTURE)
                            {
                                var obj = Activator.CreateInstance(type);
                                var mis = ProcessDataTools.GetFieldAndProperies(type);
                                var structure = readTable.GetStructure(name);
                                if (md.ValueMetadataAsStructureMetadata != null)
                                {
                                    var mds = md.ValueMetadataAsStructureMetadata;
                                    for (int f = 0; f < mds.FieldCount; f++)
                                    {
                                        var fd = mds[f];
                                        var fieldname = fd.Name;
                                        var fnu = fieldname.ToUpper();
                                        var mi = mis.Find(m => m.Name.ToUpper() == fnu);
                                        if (mi == null) continue;
                                        var value = structure.GetValue(fieldname);
                                        var mt = ProcessDataTools.GetMemberType(mi);
                                        var val = Convert.ChangeType(value, mt);
                                        ProcessDataTools.SetValue(mi, obj, val);
                                    }
                                }
                                Result.Add(key, obj);
                            }
                            else
                            {
                                Result.Add(key, Convert.ChangeType(data.GetValue(), type));
                            }
                            break;
                        case RfcDirection.TABLES:
                            {
                                var listType = typeof(List<>);
                                var constructedListType = listType.MakeGenericType(type);
                                var list = Activator.CreateInstance(constructedListType);
                                MethodInfo listAdd = constructedListType.GetMethod("Add");

                                bool anonymous = ProcessDataTools.CheckIfAnonymousType(type);


                                var ctors = type.GetConstructors();
                                var parlen = new int[ctors.Length];
                                for (int p = 0; p < ctors.Length; p++)
                                {
                                    var pars = ctors[p].GetParameters();
                                    parlen[p] = pars.Length;
                                }
                                var minpar = parlen.Min();
                                var ind = parlen.ToList().IndexOf(minpar);
                                var constructor = ctors[ind];

                                var objectparameters = new object[minpar];


                                var mis = ProcessDataTools.GetFieldAndProperies(type);
                                var table = readTable.GetTable(name);
                                if (md.ValueMetadataAsTableMetadata != null)
                                {
                                    var mdt = md.ValueMetadataAsTableMetadata;

                                    var linefields = new List<RfcFieldMetadata>();
                                    for (int f = 0; f < mdt.LineType.FieldCount; f++)
                                    {
                                        linefields.Add(mdt.LineType[f]);
                                    }


                                    foreach (var Row in table)
                                    {
                                        if (anonymous)
                                        {
                                            List<object> parameters = new List<object>();
                                            foreach (var mi in mis)
                                            {
                                                var uname = mi.Name.ToUpper();
                                                var lfr = linefields.Find(lf => lf.Name == uname);
                                                if (lfr != null)
                                                {
                                                    var value = Row.GetValue(lfr.Name);
                                                    var mt = ProcessDataTools.GetMemberType(mi);
                                                    var val = Convert.ChangeType(value, mt);
                                                    parameters.Add(val);
                                                }
                                            }
                                            var obj = constructor.Invoke(parameters.ToArray());
                                            listAdd.Invoke(list, new object[] { obj });
                                        }
                                        else
                                        {
                                            var obj = Activator.CreateInstance(type);
                                            for (int f = 0; f < mdt.LineType.FieldCount; f++)
                                            {
                                                var fd = mdt.LineType[f];
                                                var fieldname = fd.Name;
                                                var fnu = fieldname.ToUpper();
                                                var mi = mis.Find(m => m.Name.ToUpper() == fnu);
                                                if (mi == null) continue;
                                                var value = Row.GetValue(fieldname);
                                                var mt = ProcessDataTools.GetMemberType(mi);
                                                var val = Convert.ChangeType(value, mt);
                                                ProcessDataTools.SetValue(mi, obj, val);
                                            }
                                            listAdd.Invoke(list, new object[] { obj });
                                        }
                                    }
                                }
                                Result.Add(key, list);
                            }
                            break;
                    }
                }
            }

            return Result;

        }

    }

}