using SAP.Middleware.Connector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SAPLink
{
    public sealed class SapTransaction : IDisposable
    {
        readonly ConnectionDestinationConfiguration _core;
        readonly string _destination;
        bool _finished;

        internal SapTransaction(ConnectionDestinationConfiguration core, string destination)
        {
            _core = core;
            _destination = destination;
            RfcSessionManager.BeginContext(RfcDestinationManager.GetDestination(destination));
        }

        /// <summary>
        /// Calls function module with appropriate parameters. Name of fields must be the same as in SAP
        /// </summary>
        /// <param name="function">Name of SAP function module</param>
        /// <param name="data">List of input parameters: imports and tables(IEnumerable)</param>
        /// <param name="output">Description of output parameters. Set type of parameters for exports and generic type of List<> for tables. If table return strings type should be string, not List</param>
        /// <returns>Dictionary of results of exports and tables values. tables are strings of type set in Output.</returns>

        public Dictionary<string, object> CallFunction(string function, Dictionary<string, object> data,
            Dictionary<string, Type> output)
        {
            if (_finished) throw new InvalidOperationException("Transaction already finished");
            return _core.CallFunction(_destination, function, data, output);
        }

        public void Commit(bool wait = true) => Finish("BAPI_TRANSACTION_COMMIT", wait);
        public void Rollback() => Finish("BAPI_TRANSACTION_ROLLBACK", false);

        void Finish(string function, bool wait)
        {
            if (_finished) return;
            _finished = true;
            var dest = RfcDestinationManager.GetDestination(_destination);
            try
            {
                var f = dest.Repository.CreateFunction(function);
                if (wait && function.EndsWith("COMMIT")) f.SetValue("WAIT", "X");
                f.Invoke(dest);
            }
            finally { RfcSessionManager.EndContext(dest); }
        }

        public void Dispose()
        {
            if (_finished) return;
            try { Rollback(); } catch { /* контекст уже закрыт в finally */ }
        }
    }
}
