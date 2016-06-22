using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net;
using System.Web;
using System.Runtime.InteropServices;
using System.IO;
using System.Text.RegularExpressions;

namespace ScreenConnect.Integration
{
    public class SCHostInterface
    {
        const int sessionTypeSupport = 0;
        const int sessionTypeMeet = 1;
        const int sessionTypeAccess = 2;

        private String baseUrl;
        private NetworkCredential nc;
        private String hostName;
        private String relayPort;
        private String encryptionKey;
        private String serverVersion;
        public List<SCHostCategory> support;
        public List<SCHostCategory> meet;
        public List<SCHostCategory> access;

        private String serviceAshx = "/Service.ashx";

        [DllImport("dfshim.dll", EntryPoint = "LaunchApplication", CharSet = CharSet.Unicode)]
        private static extern int LaunchApplication(string UrlToDeploymentManifest, System.IntPtr dataMustBeNull, System.UInt32 flagsMustBeZero); 

        public SCHostInterface(String url, String username, String password)
        {
            this.baseUrl = url;
            this.nc = new NetworkCredential(username, password);
            serverVersion = getServerVersion();
            initServerConfig();
            initCategories();
        }

        public SCHostSession createSupportSession(String name, bool isPublic, String code)
        {
            JValue jVsessionID;
            if (serverVersion.StartsWith("ScreenConnect/4") || serverVersion.StartsWith("ScreenConnect/5"))
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

        public byte[] createAccessSessionMSI(String name, params String[] customProperties)
        {
            return createAccessSession("msi", name, customProperties);
        }

        public byte[] createAccessSessionPKG(String name, params String[] customProperties)
        {
            return createAccessSession("pkg", name, customProperties);
        }

        private byte[] createAccessSession(String type, String name, params String[] customProperties)
        {
            String url;
            if (serverVersion.StartsWith("ScreenConnect/5"))
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

        private String getServerVersion()
        {
            System.Net.WebRequest req = System.Net.WebRequest.Create(baseUrl);
            System.Net.WebResponse resp = req.GetResponse();
            String version = resp.Headers["Server"].Split(' ')[0];

            if (version.StartsWith("ScreenConnect/5"))
            {
                serviceAshx = "/Services/PageService.ashx";
            }

            return version;
        }

        public void refreshCategories()
        {
            initCategories();
        }

        private void initServerConfig()
        {
            String[] instURLSplit;
            if (serverVersion.StartsWith("ScreenConnect/5"))
            {
                // SC 5 Script Parser Hack
                String script = HttpPost(baseUrl + "/Script.ashx", "").Replace('\r',' ').Replace('\n',' ');
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

        private void initCategories()
        {
            JObject hsi = getHostSessionInfo();
            String[] sgsKeys = new String[]{"sgs","SessionGroupSummaries","PathSessionGroupSummaries"};
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
            initCategories(sgs);

        }

        private void initCategories(JArray sgs)
        {
            String nKey = "n";
            String stKey = "st";
            String scKey = "sc";
            if (serverVersion.StartsWith("ScreenConnect/5"))
            {
                nKey = "Name";
                stKey = "SessionType";
                scKey = "SessionCount";
            }
            support = new List<SCHostCategory>();
            meet = new List<SCHostCategory>();
            access = new List<SCHostCategory>();
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
                SCHostCategory cat = new SCHostCategory(this);
                cat._count = count;
                cat._name = name;
                catList.Add(cat);
            }
        }

        internal List<SCHostSession> getSessions(String category)
        {
            List<SCHostSession> sl = new List<SCHostSession>();
            JObject hsi = getHostSessionInfo(category);
            String sssKey = "sss";
            if (serverVersion.StartsWith("ScreenConnect/5"))
            {
                sssKey = "Sessions";
            }
            JArray sss = (JArray)hsi[sssKey];
            foreach (JObject session in sss)
            {
                SCHostSession scsession = new SCHostSession(this);
                if (serverVersion.StartsWith("ScreenConnect/5"))
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
                    catch {
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
                    foreach(JValue cobj in (JArray)session["cps"]){
                        custom.Add(cobj.Value<String>());
                    }
                    scsession._custom = custom.ToArray();
                }
                String stKey = "st";
                if (serverVersion.StartsWith("ScreenConnect/5"))
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

        internal String getLaunchURL(SCHostSession session)
        {
            String appName = "Elsinore.ScreenConnect.WindowsClient.application";
            if (serverVersion.StartsWith("ScreenConnect/5") || serverVersion.StartsWith("ScreenConnect/4.3"))
            {
                appName = "Elsinore.ScreenConnect.Client.application";
            }
            String url = baseUrl + "/Bin/" + appName + "?";
            url += "h=" + hostName + "&";
            url += "p=" + relayPort + "&";
            url += "k=" + encryptionKey + "&";
            url += "s=" + session._sessionID + "&";
            if (serverVersion.StartsWith("ScreenConnect/4") || serverVersion.StartsWith("ScreenConnect/5"))
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

        private JObject getHostSessionInfo()
        {
            return getHostSessionInfo(null);
        }

        private JObject getHostSessionInfo(String category)
        {
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

        private string HttpPost(String url, string Parameters)
        {
            HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(url);
            req.UserAgent = "ScreenConnect Integration Library";
            req.Timeout = 10000;
            req.Credentials = nc;
            //req.ContentType = "application/x-www-form-urlencoded";
            req.ContentType = "text/plain; charset=UTF-8";
            req.Method = "POST";
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(Parameters);
            req.ContentLength = bytes.Length;
            System.IO.Stream os = req.GetRequestStream();
            os.Write(bytes, 0, bytes.Length);
            os.Close();
            System.Net.WebResponse resp = req.GetResponse();
            if (resp == null) return null;
            System.IO.StreamReader sr = new System.IO.StreamReader(resp.GetResponseStream());
            String respStr = sr.ReadToEnd().Trim();
            respStr = Regex.Replace(respStr, @"\\u([\dA-Fa-f]{4})", v => ((char)Convert.ToInt32(v.Groups[1].Value, 16)).ToString()).Replace("%25", "%");
            return respStr;
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
    }
}
