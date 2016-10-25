using System;
using System.Collections.Generic;
using System.Text;

namespace ScreenConnect.Integration
{
    public class SCHostSessionDetails
    {
        #region Internal Fields

        internal string _infoUpdateTime;
        internal string _machineDomain;
        internal string _machineName;
        internal string _networkAddress;
        internal string _processorName;
        internal string _processorVirtualCount;
        internal byte[] _screenshot;
        internal string _screenshotContentType;
        internal string _systemMemoryAvailableMegabytes;
        internal string _systemMemoryTotalMegabytes;

        #endregion Internal Fields

        #region Private Fields

        private SCHostSession _session;

        #endregion Private Fields

        #region Public Properties

        public String infoUpdateTime { get { return _infoUpdateTime; } }
        public String machineDomain { get { return _machineDomain; } }
        public String machineName { get { return _machineName; } }
        public String networkAddress { get { return _networkAddress; } }
        public String processorName { get { return _processorName; } }
        public String processorVirtualCount { get { return _processorVirtualCount; } }
        public byte[] screenshot { get { return _screenshot; } }
        public String screnshotContentType { get { return _screenshotContentType; } }
        public SCHostSession session { get { return _session; } }
        public String systemMemoryAvailableMegabytes { get { return _systemMemoryAvailableMegabytes; } }
        public String systemMemoryTotalMegabytes { get { return _systemMemoryTotalMegabytes; } }

        #endregion Public Properties

        #region Internal Constructors

        internal SCHostSessionDetails(SCHostSession session)
        {
            this._session = session;
        }

        #endregion Internal Constructors
    }
}