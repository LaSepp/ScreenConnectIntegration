using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ScreenConnect.Integration
{
    public class SCHostSession
    {
        #region Internal Fields

        internal String[] _custom;
        internal bool _guestConnected;
        internal String _guestOS;
        internal String _guestUser;
        internal String _host;
        internal bool _hostConnected;
        internal SCHostInterface _interface;
        internal String _name;
        internal String _sessionID;
        internal String _token;
        internal String _type;

        #endregion Internal Fields

        #region Private Fields

        private String _category;

        #endregion Private Fields

        #region Public Properties

        public String category { get { return _category; } }
        public String[] custom { get { return _custom; } }

        public SCHostSessionDetails details
        {
            get
            {
                return _interface.getSessionDetails(this);
            }
        }

        public bool guestConnected { get { return _guestConnected; } }
        public String guestOS { get { return _guestOS; } }
        public String guestUser { get { return _guestUser; } }
        public String host { get { return _host; } }
        public bool hostConnected { get { return _hostConnected; } }
        public String name { get { return _name; } }
        public String sessionID { get { return _sessionID; } }

        #endregion Public Properties

        #region Internal Constructors

        internal SCHostSession(SCHostInterface hostinterface, String category)
        {
            this._category = category;
            this._interface = hostinterface;
        }

        #endregion Internal Constructors

        #region Public Methods

        public void connect()
        {
            _interface.startHost(this);
        }

        public String getLaunchURL()
        {
            return _interface.getLaunchURL(this);
        }

        public String runCommand(String command)
        {
            _interface.updateAntiforgeryToken();
            _interface.addEventToSession(this, SCHostSessionEventType.QueuedCommand, command);
            for (int i = 0; i < 15; i++)
            {
                Thread.Sleep(1000);
                SCHostSessionEvent[] events = details.getEvents(SCHostSessionEventType.QueuedCommand, SCHostSessionEventType.RanCommand);
                int c = events.Length - 1;
                if (events[c].EventType == SCHostSessionEventType.RanCommand)
                {
                    if (c > 0 && events[c - 1].EventType == SCHostSessionEventType.QueuedCommand && events[c - 1].Data == command) return events[c].Data;
                }
            }
            throw new TimeoutException("Command execution timed out.");
        }

        #endregion Public Methods
    }
}