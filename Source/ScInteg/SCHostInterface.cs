using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
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

        #endregion Public Fields

        #region Private Fields

        private const int sessionTypeAccess = 2;
        private const int sessionTypeMeet = 1;
        private const int sessionTypeSupport = 0;
        private string aspEventValidation;

        private string aspViewState;

        private String baseUrl;

        private CookieCollection cookies;

        private String encryptionKey;

        private String hostName;

        private NetworkCredential nc;

        private String relayPort;

        private String serverVersion;

        private String serviceAshx = "/Service.ashx";

        #endregion Private Fields

        #region Public Constructors

        public SCHostInterface(String url, String username, String password)
        {
            this.baseUrl = url.EndsWith("/") ? url.Remove(url.Length - 1) : url;
            this.nc = new NetworkCredential(username, password);
            serverVersion = getServerVersion();
            if (serverVersion.StartsWith("ScreenConnect/6")) loginSc6();
            initServerConfig();
            initCategories();
        }

        #endregion Public Constructors

        #region Public Methods

        public byte[] createAccessSessionMSI(String name, params String[] customProperties)
        {
            return createAccessSession("msi", name, customProperties);
        }

        public byte[] createAccessSessionPKG(String name, params String[] customProperties)
        {
            return createAccessSession("pkg", name, customProperties);
        }

        public SCHostSession createSupportSession(String name, bool isPublic, String code)
        {
            JValue jVsessionID;
            if (serverVersion.StartsWith("ScreenConnect/4") || serverVersion.StartsWith("ScreenConnect/5") || serverVersion.StartsWith("ScreenConnect/6"))
            {
                jVsessionID = JsonConvert.DeserializeObject<JValue>(HttpPost(baseUrl + serviceAshx + "/CreateSession", JsonConvert.SerializeObject(new Object[] { sessionTypeSupport, name, isPublic, code, new String[0] })));
            }
            else
            {
                jVsessionID = JsonConvert.DeserializeObject<JValue>(HttpPost(baseUrl + serviceAshx + "/CreateSession", JsonConvert.SerializeObject(new Object[] { sessionTypeSupport, name, isPublic, code })));
            }
            String sessionID = jVsessionID.Value<String>();
            foreach (SCHostCategory cat in support)
            {
                foreach (SCHostSession session in cat.sessions)
                {
                    if (session._sessionID == sessionID) return session;
                }
            }
            throw new Exception("Error creating Session");
        }

        public void refreshCategories()
        {
            initCategories();
        }

        #endregion Public Methods

        #region Internal Methods

        internal String getLaunchURL(SCHostSession session)
        {
            String appName = "Elsinore.ScreenConnect.WindowsClient.application";
            if (serverVersion.StartsWith("ScreenConnect/5") || serverVersion.StartsWith("ScreenConnect/6") || serverVersion.StartsWith("ScreenConnect/4.3"))
            {
                appName = "Elsinore.ScreenConnect.Client.application";
            }
            String url = baseUrl + "/Bin/" + appName + "?";
            url += "h=" + hostName + "&";
            url += "p=" + relayPort + "&";
            url += "k=" + encryptionKey + "&";
            url += "s=" + session._sessionID + "&";
            if (serverVersion.StartsWith("ScreenConnect/4") || serverVersion.StartsWith("ScreenConnect/5") || serverVersion.StartsWith("ScreenConnect/6"))
            {
                url += "i=" + HttpUtility.UrlEncode(session._name) + "&";
            }
            else
            {
                url += "t=" + HttpUtility.UrlEncode(session._name) + "&";
            }
            url += "n=" + HttpUtility.UrlEncode(session._token) + "&";
            url += "e=" + session._type + "&";
            url += "y=Host";
            return url;
        }

        internal SCHostSessionDetails getSessionDetails(SCHostSession session)
        {
            JObject gsd = JsonConvert.DeserializeObject<JObject>(HttpPost(baseUrl + serviceAshx + "/GetSessionDetails", JsonConvert.SerializeObject(new Object[] { session.category, session.sessionID })));
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
            return details;
        }

        internal List<SCHostSession> getSessions(String category, int mode)
        {
            List<SCHostSession> sl = new List<SCHostSession>();
            JObject hsi = getHostSessionInfo(category, mode);
            String sssKey = "sss";
            if (serverVersion.StartsWith("ScreenConnect/5") || serverVersion.StartsWith("ScreenConnect/6"))
            {
                sssKey = "Sessions";
            }
            JArray sss = (JArray)hsi[sssKey];
            foreach (JObject session in sss)
            {
                SCHostSession scsession = new SCHostSession(this, category);
                if (serverVersion.StartsWith("ScreenConnect/5") || serverVersion.StartsWith("ScreenConnect/6"))
                {
                    scsession._name = session["Name"].ToString();
                    scsession._sessionID = session["SessionID"].ToString();
                    scsession._token = session["AccessToken"].ToString();
                    scsession._host = session["Host"].ToString();
                    try
                    {
                        scsession._guestOS = session["GuestOperatingSystemName"].ToString();
                        String domain = session["GuestLoggedOnUserDomain"].ToString();
                        scsession._guestUser = (domain == "" ? "" : domain + @"\") + session["GuestLoggedOnUserName"].ToString();
                    }
                    catch
                    {
                        // Attributes not supported
                    }
                    List<String> custom = new List<String>();
                    foreach (JValue cobj in (JArray)session["CustomPropertyValues"])
                    {
                        custom.Add(cobj.Value<String>());
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
                    scsession._host = session["h"].Value<String>();
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
                        scsession._name = clp["i"].Value<String>();
                    }
                    else
                    {
                        scsession._name = clp["t"].Value<String>();
                    }
                    scsession._sessionID = clp["s"].Value<String>();
                    scsession._token = clp["n"].Value<String>();
                    List<String> custom = new List<String>();
                    foreach (JValue cobj in (JArray)session["cps"])
                    {
                        custom.Add(cobj.Value<String>());
                    }
                    scsession._custom = custom.ToArray();
                }
                String stKey = "st";
                if (serverVersion.StartsWith("ScreenConnect/5") || serverVersion.StartsWith("ScreenConnect/6"))
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

        internal void startHost(SCHostSession session)
        {
            String url = getLaunchURL(session);
            int result = LaunchApplication(url, IntPtr.Zero, 0);
            if (result != 0) throw new Exception("Error launching Host Client: " + result);
        }

        #endregion Internal Methods

        #region Private Methods

        [DllImport("dfshim.dll", EntryPoint = "LaunchApplication", CharSet = CharSet.Unicode)]
        private static extern int LaunchApplication(string UrlToDeploymentManifest, System.IntPtr dataMustBeNull, System.UInt32 flagsMustBeZero);

        private String buildHostSessionInfoParam(String category)
        {
            Object info = new Object[] { category, null, null, 0 };
            return JsonConvert.SerializeObject(info);
        }

        private String buildHostSessionInfoParamV2(String category)
        {
            Object info = new Object[] { new String[] { category }, null, null, 0 };
            return JsonConvert.SerializeObject(info);
        }

        private String buildHostSessionInfoParamV3(int mode, String category)
        {
            Object info = new Object[] { mode, category == null ? new String[0] : new String[] { category }, null, null, 0 };
            return JsonConvert.SerializeObject(info);
        }

        private byte[] createAccessSession(String type, String name, params String[] customProperties)
        {
            String url;
            if (serverVersion.StartsWith("ScreenConnect/5") || serverVersion.StartsWith("ScreenConnect/6"))
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
                foreach (String p in customProperties)
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
            url = url.Replace("\"", "");
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
                foreach (var singleCookie in value.Split(','))
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

        private JObject getHostSessionInfo(String category, int mode)
        {
            if (serverVersion.StartsWith("ScreenConnect/6"))
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

        private String getInstallerUrlV3(bool asMac, bool asMSI, String name)
        {
            return HttpPost(baseUrl + serviceAshx + "/GetInstallerUrl", JsonConvert.SerializeObject(new Object[] { asMac, asMSI, name }));
        }

        private String getInstallerUrlV4(String type, String name, String[] parameters)
        {
            String url = HttpPost(baseUrl + serviceAshx + "/GetInstallerUrl", JsonConvert.SerializeObject(new Object[] { type, name, parameters }));
            return url;
        }

        private String getServerVersion()
        {
            System.Net.WebRequest req = System.Net.WebRequest.Create(baseUrl);
            System.Net.WebResponse resp = req.GetResponse();
            String version = resp.Headers["Server"].Split(' ')[0];

            if (version.StartsWith("ScreenConnect/5") || version.StartsWith("ScreenConnect/6"))
            {
                serviceAshx = "/Services/PageService.ashx";
            }

            return version;
        }

        private byte[] HttpDownload(String url)
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

        private string HttpPost(String url, string Parameters, Boolean isLoginRequest = false)
        {
            HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(url);
            if (isLoginRequest) req.AllowAutoRedirect = false;
            req.UserAgent = "ScreenConnect Integration Library";
            req.Timeout = 10000;
            req.Credentials = nc;
            if (this.cookies != null)
            {
                req.CookieContainer = new CookieContainer();
                req.CookieContainer.Add(this.cookies);
            }
            if (isLoginRequest)
            {
                req.ContentType = "application/x-www-form-urlencoded";
            }
            else
            {
                req.ContentType = "application/json; charset=UTF-8";
            }
            req.Method = "POST";
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(Parameters);
            req.ContentLength = bytes.Length;
            System.IO.Stream os = req.GetRequestStream();
            os.Write(bytes, 0, bytes.Length);
            os.Close();
            HttpWebResponse resp = req.GetResponse() as HttpWebResponse;
            if (resp == null) return null;
            if (isLoginRequest)
            {
                fixCookies(req, resp);
                this.cookies = resp.Cookies;
            }
            System.IO.StreamReader sr = new System.IO.StreamReader(resp.GetResponseStream());
            String respStr = sr.ReadToEnd().Trim();
            respStr = Regex.Replace(respStr, @"\\u([\dA-Fa-f]{4})", v => ((char)Convert.ToInt32(v.Groups[1].Value, 16)).ToString()).Replace("%25", "%");
            return respStr;
        }

        private void initCategories()
        {
            int[] modes = new int[] { -1 };
            if (serverVersion.StartsWith("ScreenConnect/6"))
            {
                modes = new int[] { 0, 1, 2 };
            }
            foreach (int mode in modes)
            {
                JObject hsi = getHostSessionInfo(mode);
                String[] sgsKeys = new String[] { "sgs", "SessionGroupSummaries", "PathSessionGroupSummaries" };
                JArray sgs = null;
                foreach (String sgsKey in sgsKeys)
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
            String nKey = "n";
            String stKey = "st";
            String scKey = "sc";
            if (serverVersion.StartsWith("ScreenConnect/5") || serverVersion.StartsWith("ScreenConnect/6"))
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
                String name = category[nKey].Value<String>();
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
            String[] instURLSplit;
            if (serverVersion.StartsWith("ScreenConnect/5") || serverVersion.StartsWith("ScreenConnect/6"))
            {
                // SC 5 Script Parser Hack
                String script = HttpPost(baseUrl + "/Script.ashx", "").Replace('\r', ' ').Replace('\n', ' ');
                String script_stripped = script.Remove(0, 5);
                script_stripped = script_stripped.Remove(script_stripped.Length - 1);
                String clp = script_stripped.Remove(0, script_stripped.IndexOf("clp") + 5);
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
                    instURLSplit = getInstallerUrlV4("msi", "", new String[0]).Split(new char[] { '?', '&' });
                }
                else
                {
                    instURLSplit = getInstallerUrlV3(false, false, "name").Split(new char[] { '?', '&' });
                }
                foreach (String kvString in instURLSplit)
                {
                    String[] kv = kvString.Split('=');
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
            String respStr = sr.ReadToEnd().Trim();
            updateViewstate(respStr);
            String loginString = "";
            loginString += "__EVENTARGUMENT=&";
            loginString += "__EVENTTARGET=&";
            loginString += "__LASTFOCUS=&";
            loginString += "__VIEWSTATE=" + HttpUtility.UrlEncode(aspViewState) + "&";
            loginString += "__VIEWSTATEGENERATOR=" + HttpUtility.UrlEncode(aspEventValidation) + "&";
            loginString += HttpUtility.UrlEncode("ctl00$Main$ctl03") + "=" + HttpUtility.UrlEncode("Login") + "&";
            loginString += HttpUtility.UrlEncode("ctl00$Main$passwordBox") + "=" + HttpUtility.UrlEncode(nc.Password) + "&";
            loginString += HttpUtility.UrlEncode("ctl00$Main$userNameBox") + "=" + HttpUtility.UrlEncode(nc.UserName);
            HttpPost(baseUrl + "/Login", loginString, true);
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