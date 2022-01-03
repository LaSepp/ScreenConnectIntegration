using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScreenConnect.Integration.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace ScreenConnect.Integration
{
    public class SCHostInterface
    {
        #region Public Fields

        public List<SCHostCategory> access;
        public List<SCHostCategory> meet;
        public List<SCHostCategory> support;

        public string userName;

        #endregion Public Fields

        #region Private Fields

        private const int sessionTypeAccess = 2;
        private const int sessionTypeMeet = 1;
        private const int sessionTypeSupport = 0;
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private string antiForgeryToken;
        private string aspEventValidation;
        private string aspViewState;

        private string baseUrl;

        private CookieCollection cookies;

        private string encryptionKey;

        private string hostName;

        private string LoginResult = null;
        private NetworkCredential nc;

        private string relayPort;

        private string sc6loginbuttonid = null;
        private string serverVersion;
        private int serverVersionMain = 0;

        private string serviceAshx = "/Service.ashx";
        private string urlScheme;

        #endregion Private Fields

        #region Public Properties

        public string LoginErrorCode => LoginResult;
        public bool NoLoginError => LoginResult == null || LoginResult == string.Empty;
        public bool OneTimePasswordRequired => LoginResult == "OneTimePasswordInvalid";

        private bool SC21SessionMode = false;

        #endregion Public Properties

        #region Public Constructors

        public SCHostInterface(string url)
        {
            baseUrl = url.EndsWith("/") ? url.Remove(url.Length - 1) : url;
            serverVersion = getServerVersion();
            try
            {
                serverVersionMain = int.Parse(serverVersion.Split(new char[] { '/' }, 2, StringSplitOptions.RemoveEmptyEntries)[1].Split('.')[0]);
            }
            catch { }
            if (!serverVersion.StartsWith("ScreenConnect/")) serverVersionMain = 999; // Assume newest version if not using internal ScreenConnect Web Server
            if (serverVersionMain >= 5)
            {
                serviceAshx = "/Services/PageService.ashx";
            }
        }

        public SCHostInterface(string url, string username, string password, string oneTimePassword = null) : this(url)
        {
            Login(username, password, oneTimePassword);
        }

        #endregion Public Constructors

        #region Public Methods

        public byte[] createAccessSessionMSI(string name, params string[] customProperties)
        {
            return createAccessSession("msi", name, customProperties);
        }

        public byte[] createAccessSessionPKG(string name, params string[] customProperties)
        {
            return createAccessSession("pkg", name, customProperties);
        }

        public SCHostSession createSupportSession(string name, bool isPublic, string code)
        {
            updateAntiforgeryToken();
            JValue jVsessionID;
            if (serverVersionMain >= 4)
            {
                jVsessionID = JsonConvert.DeserializeObject<JValue>(HttpPost(baseUrl + serviceAshx + "/CreateSession", JsonConvert.SerializeObject(new object[] { sessionTypeSupport, name, isPublic, code, new string[0] })));
            }
            else
            {
                jVsessionID = JsonConvert.DeserializeObject<JValue>(HttpPost(baseUrl + serviceAshx + "/CreateSession", JsonConvert.SerializeObject(new object[] { sessionTypeSupport, name, isPublic, code })));
            }
            string sessionID = jVsessionID.Value<string>();
            foreach (SCHostCategory cat in support)
            {
                foreach (SCHostSession session in cat.sessions)
                {
                    if (session._sessionID == sessionID) return session;
                }
            }
            throw new Exception("Error creating Session");
        }

        public void Login(string username, string password, string oneTimePassword = null)
        {
            userName = username;
            nc = new NetworkCredential(username, password);
            if (serverVersionMain >= 6) loginSc6();
            if (oneTimePassword != null && !SC21SessionMode)
            {
                LoginOneTimePassword(oneTimePassword);
                return;
            }
            if (OneTimePasswordRequired) throw new ScreenConnectAuthenticationException("One Time Password needed", LoginResult);
            if (!NoLoginError) throw new ScreenConnectAuthenticationException("Login failed: " + LoginResult, LoginResult);
            initServerConfig();
            initCategories();
        }

        public void LoginOneTimePassword(string oneTimePassword)
        {
            if (SC21SessionMode)
            {
                loginSc21(oneTimePassword);
            }
            else
            {
                if (oneTimePassword != null) loginSc6Otp(oneTimePassword);
                if (!NoLoginError) throw new ScreenConnectAuthenticationException("Login failed: " + LoginResult, LoginResult);
            }
            initServerConfig();
            initCategories();
        }

        public void refreshCategories()
        {
            initCategories();
        }

        public void updateAntiforgeryToken()
        {
            updateAntiforgeryToken(HttpPost(baseUrl + "/Host", string.Empty));
        }

        #endregion Public Methods

        #region Internal Methods

        internal void addEventToSession(SCHostSession session, SCHostSessionEventType type, string data)
        {
            HttpPost(baseUrl + serviceAshx + "/AddEventToSessions", JsonConvert.SerializeObject(new object[] { new object[] { session.category }, new object[] { session.sessionID }, (int)type, data }));
        }

        internal string getLaunchScheme(SCHostSession session)
        {
            /*  URL SCHEME:
                '{0}://{1}:{2}/{3}/{4}/{5}/{6}/{7}/{8}/{9}/{10}',
				scheme,
				clientLaunchParameters.h,
				clientLaunchParameters.p,
				clientLaunchParameters.s,
				encodeURIComponent(clientLaunchParameters.k || ''),
				encodeURIComponent(clientLaunchParameters.n || ''),
				encodeURIComponent(clientLaunchParameters.r || ''),
				clientLaunchParameters.e,
				encodeURIComponent(clientLaunchParameters.i || ''),
				encodeURIComponent(clientLaunchParameters.a || ''),
				encodeURIComponent(clientLaunchParameters.l || '')
            */

            if (session._token == null)
            {
                JValue accessTokenInfo = JsonConvert.DeserializeObject<JValue>(HttpPost(baseUrl + serviceAshx + "/GetAccessToken", JsonConvert.SerializeObject(new object[] { session.category, session.sessionID })));
                session._token = accessTokenInfo.ToString();
            }

            string url = urlScheme + "://";
            url += hostName + ":";
            url += relayPort + "/";
            url += session._sessionID + "/";
            url += encryptionKey + "/";
            url += HttpUtility.UrlEncode(session._token) + "/";
            url += "/"; // r = User Name?
            url += session._type + "/";
            url += HttpUtility.UrlEncode(session._name) + "/";
            url += "None/";
            return url;
        }

        internal string getLaunchURL(SCHostSession session)
        {
            string appName = "Elsinore.ScreenConnect.WindowsClient.application";
            if (serverVersionMain >= 5 || serverVersion.StartsWith("ScreenConnect/4.3"))
            {
                appName = "Elsinore.ScreenConnect.Client.application";
            }
            string url = baseUrl + "/Bin/" + appName + "?";
            url += "h=" + hostName + "&";
            url += "p=" + relayPort + "&";
            url += "k=" + encryptionKey + "&";
            url += "s=" + session._sessionID + "&";
            if (serverVersionMain >= 4)
            {
                url += "i=" + HttpUtility.UrlEncode(session._name) + "&";
            }
            else
            {
                url += "t=" + HttpUtility.UrlEncode(session._name) + "&";
            }
            if (session._token == null)
            {
                if (serverVersionMain > 6)
                {
                    JValue accessTokenInfo = JsonConvert.DeserializeObject<JValue>(HttpPost(baseUrl + serviceAshx + "/GetAccessToken", JsonConvert.SerializeObject(new object[] { session.category, session.sessionID })));
                    session._token = accessTokenInfo.ToString();
                }
                else
                {
                    JValue accessTokenInfo = JsonConvert.DeserializeObject<JValue>(HttpPost(baseUrl + serviceAshx + "/GetAccessToken", JsonConvert.SerializeObject(new object[] { new object[] { session.category }, session.sessionID })));
                    session._token = accessTokenInfo.ToString();
                }
            }
            url += "n=" + HttpUtility.UrlEncode(session._token) + "&";
            url += "e=" + session._type + "&";
            if (serverVersionMain > 6)
            {
                url += "a=None&";
                url += "r=&";
                url += "l=&";
            }
            url += "y=Host";
            return url;
        }

        internal SCHostSessionDetails getSessionDetails(SCHostSession session)
        {
            JObject gsd = JsonConvert.DeserializeObject<JObject>(HttpPost(baseUrl + serviceAshx + "/GetSessionDetails", JsonConvert.SerializeObject(new object[] { session.category, session.sessionID })));
            SCHostSessionDetails details = new SCHostSessionDetails(session);
            try
            {
                JObject gsdsession = gsd["Session"] as JObject;
                details._networkAddress = gsdsession["GuestNetworkAddress"].ToString();
                details._machineName = gsdsession["GuestMachineName"].ToString();
                details._machineDomain = gsdsession["GuestMachineDomain"].ToString();
                details._processorName = gsdsession["GuestProcessorName"].ToString();
                details._processorVirtualCount = gsdsession["GuestProcessorVirtualCount"].ToString();
                details._systemMemoryTotalMegabytes = gsdsession["GuestSystemMemoryTotalMegabytes"].ToString();
                details._systemMemoryAvailableMegabytes = gsdsession["GuestSystemMemoryAvailableMegabytes"].ToString();
                details._screenshotContentType = gsdsession["GuestScreenshotContentType"].ToString();
                details._infoUpdateTime = gsdsession["GuestInfoUpdateTime"].ToString();
                details._screenshot = Convert.FromBase64String(gsd["GuestScreenshotContent"].ToString());
            }
            catch { }
            try
            {
                List<SCHostSessionEvent> eventlist = new List<SCHostSessionEvent>();
                double basetime = gsd["BaseTime"].Value<double>();
                JArray events = gsd["Events"] as JArray;
                JArray connections = gsd["Connections"] as JArray;
                foreach (JObject evt in events)
                {
                    try
                    {
                        SCHostSessionEvent e = new Integration.SCHostSessionEvent();
                        e.ConnectionId = Guid.Empty;
                        e.Data = evt["Data"].Value<string>();
                        e.EventId = new Guid(evt["EventID"].ToString());
                        e.EventType = (SCHostSessionEventType)evt["EventType"].Value<int>();
                        e.Host = evt["Host"].Value<string>();
                        e.Time = UnixEpoch.AddMilliseconds(basetime - evt["Time"].Value<long>());
                        eventlist.Add(e);
                    }
                    catch { }
                }
                foreach (JObject conn in connections)
                {
                    foreach (JObject evt in conn["Events"] as JArray)
                    {
                        try
                        {
                            SCHostSessionEvent e = new Integration.SCHostSessionEvent();
                            e.ConnectionId = new Guid(conn["ConnectionID"].ToString());
                            e.Data = evt["Data"].Value<string>();
                            e.EventId = new Guid(evt["EventID"].ToString());
                            e.EventType = (SCHostSessionEventType)evt["EventType"].Value<int>();
                            e.Host = conn["ParticipantName"].Value<string>();
                            e.Time = UnixEpoch.AddMilliseconds(basetime - evt["Time"].Value<long>());
                            eventlist.Add(e);
                        }
                        catch { }
                    }
                }
                details._events = eventlist.ToArray();
            }
            catch { }
            return details;
        }

        internal List<SCHostSession> getSessions(string category, int mode)
        {
            List<SCHostSession> sl = new List<SCHostSession>();
            JObject hsi = getHostSessionInfo(category, mode);
            string sssKey = "sss";
            if (serverVersionMain >= 5)
            {
                sssKey = "Sessions";
            }
            JArray sss = (JArray)hsi[sssKey];
            foreach (JObject session in sss)
            {
                SCHostSession scsession = new SCHostSession(this, category);
                if (serverVersionMain >= 5)
                {
                    scsession._name = session["Name"].ToString();
                    scsession._sessionID = session["SessionID"].ToString();
                    try { scsession._token = session["AccessToken"].ToString(); } catch { }
                    scsession._host = session["Host"].ToString();
                    try
                    {
                        scsession._guestOS = session["GuestOperatingSystemName"].ToString();
                        string domain = session["GuestLoggedOnUserDomain"].ToString();
                        scsession._guestUser = (domain == string.Empty ? string.Empty : domain + @"\") + session["GuestLoggedOnUserName"].ToString();
                    }
                    catch
                    {
                        // Attributes not supported
                    }
                    List<string> custom = new List<string>();
                    foreach (JValue cobj in (JArray)session["CustomPropertyValues"])
                    {
                        custom.Add(cobj.Value<string>());
                    }
                    scsession._custom = custom.ToArray();
                    JArray active = (JArray)session["ActiveConnections"];
                    foreach (JObject actInfo in active)
                    {
                        if (actInfo["ProcessType"].Value<int>() == 1) scsession._hostConnected = true;
                        if (actInfo["ProcessType"].Value<int>() == 2) scsession._guestConnected = true;
                    }
                }
                else
                {
                    scsession._host = session["h"].Value<string>();
                    if (session["hcc"] == null && session["gcc"] == null && session["acs"] != null) // ScreenConnect 4.3 Presence Information
                    {
                        JArray acs = (JArray)session["acs"];
                        foreach (JObject acsInfo in acs)
                        {
                            if (acsInfo["pt"].Value<int>() == 1) scsession._hostConnected = true;
                            if (acsInfo["pt"].Value<int>() == 2) scsession._guestConnected = true;
                        }
                    }
                    else
                    {
                        scsession._hostConnected = session["hcc"].Value<int>() > 0;
                        scsession._guestConnected = session["gcc"].Value<int>() > 0;
                    }

                    JObject clp = session["clp"].Value<JObject>();
                    if (serverVersion.StartsWith("ScreenConnect/4"))
                    {
                        scsession._name = clp["i"].Value<string>();
                    }
                    else
                    {
                        scsession._name = clp["t"].Value<string>();
                    }
                    scsession._sessionID = clp["s"].Value<string>();
                    scsession._token = clp["n"].Value<string>();
                    List<string> custom = new List<string>();
                    foreach (JValue cobj in (JArray)session["cps"])
                    {
                        custom.Add(cobj.Value<string>());
                    }
                    scsession._custom = custom.ToArray();
                }
                string stKey = "st";
                if (serverVersionMain >= 5)
                {
                    stKey = "SessionType";
                }
                if (session[stKey].Value<int>() == sessionTypeSupport) scsession._type = "Support";
                if (session[stKey].Value<int>() == sessionTypeMeet) scsession._type = "Meet";
                if (session[stKey].Value<int>() == sessionTypeAccess) scsession._type = "Access";
                sl.Add(scsession);
            }
            return sl;
        }

        internal void startHost(SCHostSession session, bool viaScheme = false)
        {
            updateAntiforgeryToken();
            try { HttpPost(baseUrl + serviceAshx + "/LogInitiatedJoin", JsonConvert.SerializeObject(new object[] { session.sessionID, 1, "(UrlLaunch) ScreenConnectIntegration" })); } catch { }
            if (viaScheme)
            {
                string url = getLaunchScheme(session);
                Process.Start(url);
            }
            else
            {
                string url = getLaunchURL(session);
                int result = LaunchApplication(url, IntPtr.Zero, 0);
                if (result != 0) throw new Exception("Error launching Host Client: " + result);
            }
        }

        #endregion Internal Methods

        #region Private Methods

        [DllImport("dfshim.dll", EntryPoint = "LaunchApplication", CharSet = CharSet.Unicode)]
        private static extern int LaunchApplication(string UrlToDeploymentManifest, System.IntPtr dataMustBeNull, uint flagsMustBeZero);

        private string buildHostSessionInfoParam(string category)
        {
            object info = new object[] { category, null, null, 0 };
            return JsonConvert.SerializeObject(info);
        }

        private string buildHostSessionInfoParamV2(string category)
        {
            object info = new object[] { new string[] { category }, null, null, 0 };
            return JsonConvert.SerializeObject(info);
        }

        private string buildHostSessionInfoParamV3(int mode, string category)
        {
            object info = new object[] { mode, category == null ? new string[0] : new string[] { category }, null, null, 0 };
            return JsonConvert.SerializeObject(info);
        }

        private byte[] createAccessSession(string type, string name, params string[] customProperties)
        {
            string url;
            if (serverVersionMain >= 5)
            {
                switch (type)
                {
                    case "msi":
                        url = "Bin/Elsinore.ScreenConnect.ClientSetup.msi?";
                        break;

                    case "exe":
                        url = "Bin/Elsinore.ScreenConnect.ClientSetup.exe?";
                        break;

                    case "pkg":
                        url = "Bin/Elsinore.ScreenConnect.ClientSetup.pkg?";
                        break;

                    case "deb":
                        url = "Bin/Elsinore.ScreenConnect.ClientSetup.deb?";
                        break;

                    case "rpm":
                        url = "Bin/Elsinore.ScreenConnect.ClientSetup.rpm?";
                        break;

                    case "sh":
                        url = "Bin/Elsinore.ScreenConnect.ClientSetup.sh?";
                        break;

                    default:
                        throw new NotSupportedException("Only types msi, exe, pkg, deb, rpm, sh supported in this version of ScreenConnect");
                }
                url += "h=" + hostName + "&";
                url += "p=" + relayPort + "&";
                url += "k=" + encryptionKey + "&";
                url += "e=Access&y=Guest&";
                url += "t=" + HttpUtility.UrlEncode(name);
                foreach (string p in customProperties)
                {
                    url += "&c=" + HttpUtility.UrlEncode(p);
                }
            }
            else if (serverVersion.StartsWith("ScreenConnect/4"))
            {
                url = getInstallerUrlV4(type, name, customProperties);
            }
            else
            {
                switch (type)
                {
                    case "msi":
                        url = getInstallerUrlV3(false, true, name);
                        break;

                    case "pkg":
                        url = getInstallerUrlV3(true, false, name);
                        break;

                    default:
                        throw new NotSupportedException("Only types msi and pkg supported in this version of ScreenConnect");
                }
            }
            url = url.Replace("\"", string.Empty);
            return HttpDownload(baseUrl + "/" + url);
        }

        // http://stackoverflow.com/questions/15103513/httpwebresponse-cookies-empty-despite-set-cookie-header-no-redirect
        private void fixCookies(HttpWebRequest request, HttpWebResponse response)
        {
            for (int i = 0; i < response.Headers.Count; i++)
            {
                string name = response.Headers.GetKey(i);
                if (name != "Set-Cookie")
                    continue;
                string value = response.Headers.Get(i);
                foreach (string singleCookie in value.Split(','))
                {
                    Match match = Regex.Match(singleCookie, "(.+?)=(.+?);");
                    if (match.Captures.Count == 0)
                        continue;
                    try
                    {
                        response.Cookies.Add(
                            new Cookie(
                                match.Groups[1].ToString(),
                                match.Groups[2].ToString(),
                                "/",
                                request.Address.Host.Split(':')[0]));
                    }
                    catch
                    {
                        // Ignore Malformed Cookies
                    }
                }
            }
        }

        private JObject getHostSessionInfo(int mode = 0)
        {
            return getHostSessionInfo(null, mode);
        }

        private JObject getHostSessionInfo(string category, int mode)
        {
            if (serverVersionMain >= 6)
            {
                return JsonConvert.DeserializeObject<JObject>(HttpPost(baseUrl + serviceAshx + "/GetHostSessionInfo", buildHostSessionInfoParamV3(mode, category))); // Mode 2: Access Sessions
            }
            try
            {
                return JsonConvert.DeserializeObject<JObject>(HttpPost(baseUrl + serviceAshx + "/GetHostSessionInfo", buildHostSessionInfoParam(category)));
            }
            catch
            {
                return JsonConvert.DeserializeObject<JObject>(HttpPost(baseUrl + serviceAshx + "/GetHostSessionInfo", buildHostSessionInfoParamV2(category)));
            }
        }

        private string getInstallerUrlV3(bool asMac, bool asMSI, string name)
        {
            return HttpPost(baseUrl + serviceAshx + "/GetInstallerUrl", JsonConvert.SerializeObject(new object[] { asMac, asMSI, name }));
        }

        private string getInstallerUrlV4(string type, string name, string[] parameters)
        {
            string url = HttpPost(baseUrl + serviceAshx + "/GetInstallerUrl", JsonConvert.SerializeObject(new object[] { type, name, parameters }));
            return url;
        }

        private string getServerVersion()
        {
            System.Net.WebRequest req = System.Net.WebRequest.Create(baseUrl);
            System.Net.WebResponse resp = req.GetResponse();
            string version = resp.Headers["Server"].Split(' ')[0];
            return version;
        }

        private byte[] HttpDownload(string url)
        {
            HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(url);
            req.UserAgent = "ScreenConnect Integration Library";
            req.Timeout = 10000;
            req.Credentials = nc;
            System.Net.WebResponse resp = req.GetResponse();
            if (resp == null) return null;
            Stream respStream = resp.GetResponseStream();
            MemoryStream ms = new MemoryStream();
            byte[] b = new byte[32768];
            int r;
            while ((r = respStream.Read(b, 0, b.Length)) > 0)
            {
                ms.Write(b, 0, r);
            }
            return ms.ToArray();
        }

        private string HttpPost(string url, string Parameters, bool isLoginRequest = false)
        {
            HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(url);
            if (isLoginRequest) req.AllowAutoRedirect = false;
            req.UserAgent = "ScreenConnect Integration Library";
            req.Timeout = 10000;
            req.Credentials = nc;
            if (cookies != null)
            {
                req.CookieContainer = new CookieContainer();
                req.CookieContainer.Add(cookies);
            }
            if (isLoginRequest && !SC21SessionMode)
            {
                req.ContentType = "application/x-www-form-urlencoded";
            }
            else
            {
                req.ContentType = "application/json; charset=UTF-8";
            }
            req.Method = "POST";
            if (antiForgeryToken != null)
            {
                req.Headers.Add("x-anti-forgery-token", antiForgeryToken);
                req.Headers.Add("x-unauthorized-status-code", "403");
            }
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(Parameters);
            req.ContentLength = bytes.Length;
            System.IO.Stream os = req.GetRequestStream();
            os.Write(bytes, 0, bytes.Length);
            os.Close();
            HttpWebResponse resp = req.GetResponse() as HttpWebResponse;
            if (resp == null) return null;
            if (isLoginRequest)
            {
                try { LoginResult = resp.GetResponseHeader("X-Login-Result"); } catch { }
                fixCookies(req, resp);
                cookies = resp.Cookies;
            }
            System.IO.StreamReader sr = new System.IO.StreamReader(resp.GetResponseStream());
            string respStr = sr.ReadToEnd().Trim();
            if (serverVersionMain > 6 && isLoginRequest)
            {
                if (respStr.Contains("Your user account requires an additional authentication step.")) LoginResult = "OneTimePasswordInvalid";
                if (respStr.Contains("Invalid credentials. Please try again.")) LoginResult = "Invalid credentials";
            }
            respStr = Regex.Replace(respStr, @"\\u([\dA-Fa-f]{4})", v => ((char)Convert.ToInt32(v.Groups[1].Value, 16)).ToString()).Replace("%25", "%");
            return respStr;
        }

        private void initCategories()
        {
            int[] modes = new int[] { -1 };
            if (serverVersionMain >= 6)
            {
                modes = new int[] { 0, 1, 2 };
            }
            foreach (int mode in modes)
            {
                JObject hsi = getHostSessionInfo(mode);
                string[] sgsKeys = new string[] { "sgs", "SessionGroupSummaries", "PathSessionGroupSummaries" };
                JArray sgs = null;
                foreach (string sgsKey in sgsKeys)
                {
                    try
                    {
                        sgs = (JArray)hsi[sgsKey];
                    }
                    catch
                    {
                    }
                    if (sgs != null) break;
                }
                initCategories(sgs, mode);
            }
        }

        private void initCategories(JArray sgs, int mode)
        {
            string nKey = "n";
            string stKey = "st";
            string scKey = "sc";
            if (serverVersionMain >= 5)
            {
                nKey = "Name";
                stKey = "SessionType";
                scKey = "SessionCount";
            }
            switch (mode)
            {
                case sessionTypeSupport:
                    support = new List<SCHostCategory>();
                    break;

                case sessionTypeMeet:
                    meet = new List<SCHostCategory>();
                    break;

                case sessionTypeAccess:
                    access = new List<SCHostCategory>();
                    break;

                default:

                    support = new List<SCHostCategory>();
                    meet = new List<SCHostCategory>();
                    access = new List<SCHostCategory>();
                    break;
            }
            if (sgs[0] is JArray) sgs = (JArray)sgs[0];
            foreach (JObject category in sgs)
            {
                string name = category[nKey].Value<string>();
                int type = category[stKey].Value<int>();
                int count = category[scKey].Value<int>();
                List<SCHostCategory> catList = null;
                if (type == sessionTypeSupport) catList = support;
                if (type == sessionTypeMeet) catList = meet;
                if (type == sessionTypeAccess) catList = access;
                SCHostCategory cat = new SCHostCategory(this, mode);
                cat._count = count;
                cat._name = name;
                catList.Add(cat);
            }
        }

        private void initServerConfig()
        {
            string[] instURLSplit;
            if (serverVersionMain >= 5)
            {
                // SC 5 Script Parser Hack
                string script = HttpPost(baseUrl + "/Script.ashx", string.Empty).Replace('\r', ' ').Replace('\n', ' ');
                string script_stripped = script.Remove(0, 5);
                script_stripped = script_stripped.Remove(script_stripped.Length - 1);
                try
                {
                    string scheme = script_stripped.Remove(0, script_stripped.IndexOf("instanceUrlScheme") - 1);
                    scheme = scheme.Remove(scheme.IndexOf(",") - 1);
                    urlScheme = scheme.Split(':')[1].Replace("\"", string.Empty);
                }
                catch { urlScheme = null; }
                string clp = script_stripped.Remove(0, script_stripped.IndexOf("clp") + 5);
                clp = clp.Remove(clp.IndexOf("}") + 1);
                JObject SC = JsonConvert.DeserializeObject<JObject>(clp);
                hostName = SC.GetValue("h").ToString();
                relayPort = SC.GetValue("p").ToString();
                encryptionKey = HttpUtility.UrlEncode(SC.GetValue("k").ToString());
            }
            else
            {
                if (serverVersion.StartsWith("ScreenConnect/4"))
                {
                    instURLSplit = getInstallerUrlV4("msi", string.Empty, new string[0]).Split(new char[] { '?', '&' });
                }
                else
                {
                    instURLSplit = getInstallerUrlV3(false, false, "name").Split(new char[] { '?', '&' });
                }
                foreach (string kvString in instURLSplit)
                {
                    string[] kv = kvString.Split('=');
                    if (kv.Length > 1)
                    {
                        if (kv[0] == "h") hostName = kv[1];
                        if (kv[0] == "p") relayPort = kv[1];
                        if (kv[0] == "k") encryptionKey = kv[1];
                    }
                }
            }
        }

        private void loginSc6()
        {
            System.Net.WebRequest req = System.Net.WebRequest.Create(baseUrl + "/Login");
            System.Net.WebResponse resp = req.GetResponse();
            System.IO.StreamReader sr = new System.IO.StreamReader(resp.GetResponseStream());
            string respStr = sr.ReadToEnd().Trim();

            // ScreenConnect 21+
            if (respStr.Contains("securityNonce: '',"))
            {
                SC21SessionMode = true;
                loginSc21();
                return;
            }

            updateViewstate(respStr);

            // Earlier Versions of SC6
            if (respStr.Contains("ctl00$Main$ctl03")) sc6loginbuttonid = "ctl00$Main$ctl03";

            // Later Versions of SC6
            if (respStr.Contains("ctl00$Main$ctl05")) sc6loginbuttonid = "ctl00$Main$ctl05";

            // ScreenConnect 20
            if (respStr.Contains("ctl00$Main$loginButton")) sc6loginbuttonid = "ctl00$Main$loginButton";

            string loginString = string.Empty;
            loginString += "__EVENTARGUMENT=&";
            loginString += "__EVENTTARGET=&";
            loginString += "__LASTFOCUS=&";
            loginString += "__VIEWSTATE=" + HttpUtility.UrlEncode(aspViewState) + "&";
            loginString += "__VIEWSTATEGENERATOR=" + HttpUtility.UrlEncode(aspEventValidation) + "&";
            loginString += HttpUtility.UrlEncode(sc6loginbuttonid) + "=" + HttpUtility.UrlEncode("Login") + "&";
            loginString += HttpUtility.UrlEncode("ctl00$Main$passwordBox") + "=" + HttpUtility.UrlEncode(nc.Password) + "&";
            loginString += HttpUtility.UrlEncode("ctl00$Main$userNameBox") + "=" + HttpUtility.UrlEncode(nc.UserName);
            try
            {
                updateViewstate(HttpPost(baseUrl + "/Login", loginString, true));
            }
            catch (TimeoutException)
            {
                // Retry Login - sometimes takes too long?
                updateViewstate(HttpPost(baseUrl + "/Login", loginString, true));
            }
        }

        private void loginSc21(string oneTimePassword = null)
        {
            string nonce = GeneratePassword(16, CharacterSetAlphanumeric);
            string loginString = null;
            if (oneTimePassword == null)
            {
                loginString = "[\"" + nc.UserName.Replace("\"", "\\\"") + "\",\"" + nc.Password.Replace("\"", "\\\"") + "\",null,null,\"" + nonce + "\"]";
            }
            else
            {
                loginString = "[\"" + nc.UserName.Replace("\"", "\\\"") + "\",\"" + nc.Password.Replace("\"", "\\\"") + "\",\"" + oneTimePassword.Replace("\"", "\\\"") + "\",null,\"" + nonce + "\"]";
            }
            string loginResult = HttpPost(baseUrl + "/Services/AuthenticationService.ashx/TryLogin", loginString, true);
            switch (loginResult)
            {
                case "0":
                case "1":
                    // Success
                    break;
                case "11":
                    // OTP Auth required
                    LoginResult = "OneTimePasswordInvalid";
                    break;
                default:
                    LoginResult = "Invalid credentials";
                    break;
            }
        }

        private static readonly string CharacterSetAlphanumeric = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        private static string GeneratePassword(int length, string characterSet)
        {
            char[] characterArray = characterSet.ToCharArray();
            byte[] bytes = new byte[length * 8];
            new RNGCryptoServiceProvider().GetBytes(bytes);
            char[] result = new char[length];
            for (int i = 0; i < length; i++)
            {
                ulong value = BitConverter.ToUInt64(bytes, i * 8);
                result[i] = characterArray[value % (uint)characterArray.Length];
            }
            return new string(result);
        }

        private void loginSc6Otp(string oneTimePassword)
        {
            string loginString = string.Empty;
            loginString += "__EVENTARGUMENT=&";
            loginString += "__EVENTTARGET=&";
            loginString += "__LASTFOCUS=&";
            loginString += "__VIEWSTATE=" + HttpUtility.UrlEncode(aspViewState) + "&";
            loginString += HttpUtility.UrlEncode(sc6loginbuttonid) + "=" + HttpUtility.UrlEncode("Login") + "&";
            loginString += HttpUtility.UrlEncode("ctl00$Main$oneTimePasswordBox") + "=" + HttpUtility.UrlEncode(oneTimePassword) + "&";
            updateViewstate(HttpPost(baseUrl + "/Login?Reason=7", loginString, true));
        }

        private void updateAntiforgeryToken(string respStr)
        {
            // get antiForgeryToken
            try
            {
                string antiForgeryTokenInfo = "\"antiForgeryToken\":\"";
                int i = respStr.IndexOf(antiForgeryTokenInfo) + antiForgeryTokenInfo.Length;
                int j = respStr.IndexOf("\"", i);
                if (i > antiForgeryTokenInfo.Length) antiForgeryToken = respStr.Substring(i, j - i);
            }
            catch { }
        }

        private void updateViewstate(string respStr)
        {
            try
            {
                // get the page ViewState
                string viewStateFlag = "id=\"__VIEWSTATE\" value=\"";
                int i = respStr.IndexOf(viewStateFlag) + viewStateFlag.Length;
                int j = respStr.IndexOf("\"", i);
                aspViewState = respStr.Substring(i, j - i);

                // get page EventValidation
                string eventValidationFlag = "id=\"__VIEWSTATEGENERATOR\" value=\"";
                i = respStr.IndexOf(eventValidationFlag) + eventValidationFlag.Length;
                j = respStr.IndexOf("\"", i);
                aspEventValidation = respStr.Substring(i, j - i);
            }
            catch { }
        }

        #endregion Private Methods
    }
}