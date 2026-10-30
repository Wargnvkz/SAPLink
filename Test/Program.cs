using SAPLink;
using SAPLink.Classes;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            var logon = new Logon(
                Env("SAP_HOST"), Env("SAP_INSTANCE", "00"), Env("SAP_SID"),
                Env("SAP_USER"), Env("SAP_PASSWORD"), Env("SAP_CLIENT", "100"), "EN", "5");

            using (var sap = new SAPLink.SAPLink(logon))
            {
                if (sap.TryPing(out var error) != PingError.None)
                {
                    Console.WriteLine($"Connection failed: {error}");
                    return;
                }
                //Reading workplaces from SAP table CRHD where ARBPL is either 'WP1' or 'WP2'
                var workplaces = sap.GetTable<SAPWorkplace>("CRHD", "ARBPL IN ('WP1','WP2')");


                #region Reading without transaction

                //Reading list of users
                var userLists = sap.CallFunction("BAPI_USER_GETLIST",
                    new Dictionary<string, object>()
                    {
                        { "MAX_ROWS", "100" },
                        { "WITH_USERNAME", "X"}
                    },
                    new Dictionary<string, Type>()
                    {
                        { "ROWS",typeof(int) }, //Exports
                        { "USERLIST", typeof(BAPIUSNAME) }, //table
                        { "RETURN", typeof(BAPIRETURN2) } //Table
                    }
                );

                Console.WriteLine("USERNAME; FIRSTNAME;LASTNAME;FULLNAME");
                foreach (var user in (List<BAPIUSNAME>)userLists["USERLIST"])
                {
                    Console.WriteLine($"{user.USERNAME};{user.FIRSTNAME};{user.LASTNAME};{user.FULLNAME}");
                }

                // reading parameters of user 
                var userParameters = sap.CallFunction("BAPI_USER_GET_DETAIL",
                        new Dictionary<string, object>()
                        {
                                            { "USERNAME", "E_SIMONOV" },
                        },
                        new Dictionary<string, Type>()
                        {
                                            { "PARAMETER", typeof(BAPIPARAM) },
                                            { "RETURN", typeof(BAPIRETURN2) }
                        }
                    );
                Console.WriteLine($"PARID;PARVA;PARTXT;");
                foreach (var parameter in (List<BAPIPARAM>)userParameters["PARAMETER"])
                {
                    Console.WriteLine($"{parameter.PARID};{parameter.PARVA};{parameter.PARTXT};");

                }
                #endregion

                #region Calling BAPI with Transaction

                using (var transaction = sap.BeginTransaction())
                {
                    string matnr = "000000000000000001"; // Material number
                    string WERKS_FROM = "1000"; // Plant from where the material is moved
                    string LGORT_FROM = "0001"; // Storage location from where the material is moved
                    string BATCH = ""; // Batch number (if applicable)
                    string BWART = "301"; // Movement type (e.g., 101 for goods receipt, 201 for goods issue, etc.)
                    double QNT = 10; // Quantity to be moved
                    string EI = "PC"; // Unit of measure (e.g., PC for pieces, KG for kilograms, etc.)
                    string WERKS_TO = "2000"; // Plant to where the material is moved
                    string STORE2 = "0002"; // Storage location to where the material is moved
                    string TCODE = "04"; // Transaction code for the movement (e.g., 01 for goods receipt, 02 for goods issue, etc.)
                    string TEXT = "Test movement"; // Description of the movement

                    try
                    {
                        var result = BAPI_GOODSMVT_CREATE(transaction, matnr, WERKS_FROM, LGORT_FROM, BATCH, BWART, QNT, EI, WERKS_TO, STORE2, TCODE, TEXT);

                        if (result.Error)
                        {
                            Console.WriteLine($"Error: {result.Message}");
                            transaction.Rollback();
                        }
                        else
                        {
                            Console.WriteLine($"Material Document: {result.MATERIALDOCUMENT}, Year: {result.MATDOCUMENTYEAR}");
                            transaction.Commit();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                        transaction.Rollback();

                    }
                }
            }


        }
        static string Env(string name, string def = null) =>
    Environment.GetEnvironmentVariable(name) ?? def
    ?? throw new InvalidOperationException($"Set environment variable {name}");


        /// <summary>
        /// 
        /// </summary>
        /// <param name="matnr">Номер материала</param>
        /// <param name="WERKS_FROM">Завод откуда</param>
        /// <param name="LGORT_FROM">Склад откуда</param>
        /// <param name="BATCH">Партия</param>
        /// <param name="BWART">Вид движения</param>
        /// <param name="QNT">Количество</param>
        /// <param name="EI">Единица измерения</param>
        /// <param name="WERKS_TO">Завод куда</param>
        /// <param name="STORE2">Склад куда</param>
        /// <param name="TCODE">Код  01: Goods receipt for purchase order;  02: Goods receipt for production order; 03: Goods issue; 04: Transfer posting; 05: Other goods receipts; 06: Reversal of goods movements; 07: Subsequent adjustment to a subcontract order</param>
        /// <param name="TEXT">Описание движения</param>
        /// <param name="Out">Текст</param>
        public static MatMoveResult BAPI_GOODSMVT_CREATE(SapTransaction sap, string matnr, string WERKS_FROM, string LGORT_FROM, string BATCH, string BWART, double QNT, string EI, string WERKS_TO, string STORE2, string TCODE, string TEXT)//(string tablename, List<string> fields, List<string> where)
        {
            var mmr = new MatMoveResult();
            try
            {
                var result = sap.CallFunction("BAPI_GOODSMVT_CREATE",
                    new Dictionary<string, object>()
                    {
                    { "GOODSMVT_ITEM", new List<GOODSMVT_ITEM>()
                        {
                            new GOODSMVT_ITEM()  {
                                MATERIAL= matnr,
                                PLANT= WERKS_FROM,
                                STGE_LOC= LGORT_FROM,
                                BATCH= BATCH,
                                MOVE_TYPE= BWART,
                                ENTRY_QNT= QNT,
                                ENTRY_UOM= EI,
                                MOVE_MAT= matnr,
                                MOVE_PLANT= WERKS_TO,
                                MOVE_STLOC= STORE2,
                                MOVE_BATCH= BATCH
                            }
                        }

                    },
                    { "GOODSMVT_CODE", new GOODSMVT_CODE() { GM_CODE= TCODE } },
                    { "GOODSMVT_HEADER", new GOODSMVT_HEADER() { PSTNG_DATE= DateTime.Now, DOC_DATE= DateTime.Now, HEADER_TXT= TEXT } }

                    },
                    new Dictionary<string, Type>()
                    {
                    {"RETURN", typeof(BAPIRETURN2) },
                    {"GOODSMVT_HEADRET", typeof(BAPI2017_GM_HEAD_RET) },
                    {"MATERIALDOCUMENT",typeof(string)  },
                    {"MATDOCUMENTYEAR",typeof(string) }

                    }
                );

                if (result["RETURN"] is List<BAPIRETURN2> returnList && returnList.Count > 0)
                {
                    var firstReturn = returnList[0];
                    if (firstReturn.TYPE == "E")
                    {
                        mmr.Error = true;
                        mmr.Message = firstReturn.MESSAGE;
                        return mmr;
                    }
                }
                var headret = result["GOODSMVT_HEADRET"] as BAPI2017_GM_HEAD_RET;
                if (headret != null)
                {
                    mmr.MATERIALDOCUMENT = headret.MAT_DOC;
                    mmr.MATDOCUMENTYEAR = headret.DOC_YEAR.ToString();
                }
                else
                {
                    mmr.MATERIALDOCUMENT = result["MATERIALDOCUMENT"]?.ToString();
                    mmr.MATDOCUMENTYEAR = result["MATDOCUMENTYEAR"]?.ToString();
                }
                return mmr;
            }
            catch (Exception ex)
            {
                mmr.Error = true;
                mmr.Message = ex.Message;
                return mmr;
            }


        }
    }

    public class SAPWorkplace
    {
        [SAPGetTable("OBJID")]
        public string WorkplaceID;
        [SAPGetTable("ARBPL")]
        public string WorkplaceName;
    }
    public class MatMoveResult
    {
        public string MATERIALDOCUMENT { get; set; }
        public string MATDOCUMENTYEAR { get; set; }
        public bool Error { get; set; }
        public string Message { get; set; }
        public string HUDeleteResult { get; set; }


    }
    public class GOODSMVT_ITEM
    {
        public string MATERIAL { get; set; }
        public string PLANT { get; set; }
        public string STGE_LOC { get; set; }
        public string BATCH { get; set; }
        public string MOVE_TYPE { get; set; }
        public double ENTRY_QNT { get; set; }
        public string ENTRY_UOM { get; set; }
        public string MOVE_MAT { get; set; }
        public string MOVE_PLANT { get; set; }
        public string MOVE_STLOC { get; set; }
        public string MOVE_BATCH { get; set; }
        public string ORDERID { get; set; }
        public string MVT_IND { get; set; }
    }
    public class GOODSMVT_HEADER
    {
        public DateTime PSTNG_DATE { get; set; }
        public DateTime DOC_DATE { get; set; }
        public string HEADER_TXT { get; set; }
    }
    public class GOODSMVT_CODE
    {
        public string GM_CODE { get; set; }
    }
    public class BAPIRETURN2
    {
        public string TYPE { get; set; }
        public string ID { get; set; }
        public string NUMBER { get; set; }
        public string MESSAGE { get; set; }
        public string LOG_NO { get; set; }
        public string LOG_MSG_NO { get; set; }
        public string MESSAGE_V1 { get; set; }
        public string MESSAGE_V2 { get; set; }
        public string MESSAGE_V3 { get; set; }
        public string MESSAGE_V4 { get; set; }
        public string PARAMETER { get; set; }
        public string ROW { get; set; }
        public string FIELD { get; set; }
        public string SYSTEM { get; set; }
    }
    public class BAPI2017_GM_HEAD_RET
    {
        public string MAT_DOC { get; set; }
        public int DOC_YEAR { get; set; }
    }

    public class BAPIUSNAME
    {
        public string USERNAME;
        public string FIRSTNAME;
        public string LASTNAME;
        public string FULLNAME;
    }
    public class BAPIPARAM
    {
        public string PARID;
        public string PARVA;
        public string PARTXT;
    }

}
