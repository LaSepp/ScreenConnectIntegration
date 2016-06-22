using System;
using System.Collections.Generic;
using System.Text;

namespace ScreenConnect.Integration
{
    public class SCHostSession
    {
        public bool hostConnected { get { return _hostConnected; } }
        public bool guestConnected { get { return _guestConnected; } }
        public String name { get { return _name; } }
        public String host { get { return _host; } }
        public String sessionID { get { return _sessionID; } }
        public String[] custon { get { return _custom; } }
        public String guestUser { get { return _guestUser; } }
        public String guestOS { get { return _guestOS; } }

        internal bool _hostConnected;
        internal bool _guestConnected;
        internal String _sessionID;
        internal String _name;
        internal String _token;
        internal String _type;
        internal String _host;
        internal String[] _custom;
        internal String _guestUser;
        internal String _guestOS;
        internal SCHostInterface _interface;

        internal SCHostSession(SCHostInterface hostinterface)
        {
            this._interface = hostinterface;
        }

        public void connect()
        {
            _interface.startHost(this);
        }

        public String getLaunchURL()
        {
            return _interface.getLaunchURL(this);
        }
    }
}
