using System;
using System.Collections.Generic;
using System.Text;

namespace ScreenConnect.Integration
{
    public class SCHostCategory
    {
        #region Internal Fields

        internal int _count;
        internal SCHostInterface _interface;
        internal String _name;
        internal int _type;

        #endregion Internal Fields

        #region Public Properties

        public int categoryType { get { return _type; } }
        public int count { get { return _count; } }
        public String name { get { return _name; } }
        public List<SCHostSession> sessions { get { return _interface.getSessions(name, categoryType); } }

        #endregion Public Properties

        #region Internal Constructors

        internal SCHostCategory(SCHostInterface hostinterface, int type)
        {
            _type = type;
            _interface = hostinterface;
        }

        #endregion Internal Constructors
    }
}