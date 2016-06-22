using System;
using System.Collections.Generic;
using System.Text;

namespace ScreenConnect.Integration
{
    public class SCHostCategory
    {
        public String name { get { return _name; } }
        public int count { get { return _count; } }
        public List<SCHostSession> sessions { get { return _interface.getSessions(name); } }
        
        internal String _name;
        internal int _count;
        internal SCHostInterface _interface;

        internal SCHostCategory(SCHostInterface hostinterface)
        {
            _interface = hostinterface;
        }
    }
}
