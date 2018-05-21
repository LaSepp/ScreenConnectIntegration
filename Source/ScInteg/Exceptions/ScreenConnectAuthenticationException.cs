using System;
using System.Collections.Generic;
using System.Text;

namespace ScreenConnect.Integration.Exceptions
{
    public class ScreenConnectAuthenticationException : Exception
    {
        #region Public Properties

        public String LoginResult { get; set; }
        public Boolean OneTimePasswordRequired { get { return LoginResult == "OneTimePasswordInvalid"; } }

        #endregion Public Properties

        #region Public Constructors

        public ScreenConnectAuthenticationException(String message, String loginResult) : base(message)
        {
            this.LoginResult = loginResult;
        }

        #endregion Public Constructors
    }
}