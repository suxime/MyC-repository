private static void BackgroundWorkerDoWork(object sender, DoWorkEventArgs e)
{
    e.Cancel = true;
    Assembly mainAssembly = e.Argument as Assembly;
    var companyAttribute =
        (AssemblyCompanyAttribute) GetAttribute(mainAssembly, typeof(AssemblyCompanyAttribute));
    if (string.IsNullOrEmpty(AppTitle))
    {
        var titleAttribute =
            (AssemblyTitleAttribute) GetAttribute(mainAssembly, typeof(AssemblyTitleAttribute));
        AppTitle = titleAttribute != null ? titleAttribute.Title : mainAssembly.GetName().Name;
    }
    string appCompany = companyAttribute != null ? companyAttribute.Company : "";
    RegistryLocation = !string.IsNullOrEmpty(appCompany)
        ? $@"Software\{appCompany}\{AppTitle}\AutoUpdater"
        : $@"Software\{AppTitle}\AutoUpdater";
    InstalledVersion = mainAssembly.GetName().Version;
    WebRequest webRequest = WebRequest.Create(AppCastURL);
    webRequest.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
    if (Proxy != null)
    {
        webRequest.Proxy = Proxy;
    }
    var uri = new Uri(AppCastURL);
    WebResponse webResponse;
    try
    {
        if (uri.Scheme.Equals(Uri.UriSchemeFtp))
        {
            var ftpWebRequest = (FtpWebRequest) webRequest;
            ftpWebRequest.Credentials = FtpCredentials;
            ftpWebRequest.UseBinary = true;
            ftpWebRequest.UsePassive = true;
            ftpWebRequest.KeepAlive = true;
            ftpWebRequest.Method = WebRequestMethods.Ftp.DownloadFile;
            webResponse = ftpWebRequest.GetResponse();
        }
        else if(uri.Scheme.Equals(Uri.UriSchemeHttp) || uri.Scheme.Equals(Uri.UriSchemeHttps))
        {
            HttpWebRequest httpWebRequest = (HttpWebRequest) webRequest;
            httpWebRequest.UserAgent = GetUserAgent();
            if (BasicAuthXML != null)
            {
                httpWebRequest.Headers[HttpRequestHeader.Authorization] = BasicAuthXML.ToString();
            }
            webResponse = httpWebRequest.GetResponse();
        }
        else
        {
            webResponse = webRequest.GetResponse(); 
        }
    }
    catch (Exception exception)
    {
        Debug.WriteLine(exception);
        e.Cancel = false;
        return;
    }
    UpdateInfoEventArgs args;
    using (Stream appCastStream = webResponse.GetResponseStream())
    {
        if (appCastStream != null)
        {
            if (ParseUpdateInfoEvent != null)
            {
                using (StreamReader streamReader = new StreamReader(appCastStream))
                {
                    string data = streamReader.ReadToEnd();
                    ParseUpdateInfoEventArgs parseArgs = new ParseUpdateInfoEventArgs(data);
                    ParseUpdateInfoEvent(parseArgs);
                    args = parseArgs.UpdateInfo;
                }
            }
            else
            {
                XmlDocument receivedAppCastDocument = new XmlDocument();
                try
                {
                    receivedAppCastDocument.Load(appCastStream);
                    XmlNodeList appCastItems = receivedAppCastDocument.SelectNodes("item");
                    args = new UpdateInfoEventArgs();
                    if (appCastItems != null)
                    {
                        foreach (XmlNode item in appCastItems)
                        {
                            XmlNode appCastVersion = item.SelectSingleNode("version");
                            try
                            {
                                CurrentVersion = new Version(appCastVersion?.InnerText);
                            }
                            catch (Exception)
                            {
                                CurrentVersion = null;
                            }
                            args.CurrentVersion = CurrentVersion;
                            XmlNode appCastChangeLog = item.SelectSingleNode("changelog");
                            args.ChangelogURL = appCastChangeLog?.InnerText;
                            XmlNode appCastUrl = item.SelectSingleNode("url");
                            args.DownloadURL = appCastUrl?.InnerText;
                            if (Mandatory.Equals(false))
                            {
                                XmlNode mandatory = item.SelectSingleNode("mandatory");
                                Boolean.TryParse(mandatory?.InnerText, out Mandatory);
                                string mode = mandatory?.Attributes["mode"]?.InnerText;
                                if (!string.IsNullOrEmpty(mode))
                                {
                                    UpdateMode = (Mode) Enum.Parse(typeof(Mode), mode);
                                    if (ReportErrors && !Enum.IsDefined(typeof(Mode), UpdateMode))
                                    {
                                        throw new InvalidDataException(
                                            $"{UpdateMode} is not an underlying value of the Mode enumeration.");
                                    }
                                }
                            }
                            args.Mandatory = Mandatory;
                            args.UpdateMode = UpdateMode;
                            XmlNode appArgs = item.SelectSingleNode("args");
                            args.InstallerArgs = appArgs?.InnerText;
                            XmlNode checksum = item.SelectSingleNode("checksum");
                            args.HashingAlgorithm = checksum?.Attributes["algorithm"]?.InnerText;
                            args.Checksum = checksum?.InnerText;
                        }
                    }
                }
                catch (Exception)
                {
                    e.Cancel = false;
                    webResponse.Close();
                    return;
                }
            }
        }
        else
        {
            e.Cancel = false;
            webResponse.Close();
            return;
        }
    }
    if (args.CurrentVersion == null || string.IsNullOrEmpty(args.DownloadURL))
    {
        webResponse.Close();
        if (ReportErrors)
        {
            throw new InvalidDataException();
        }
        return;
    }
    CurrentVersion = args.CurrentVersion;
    ChangelogURL = args.ChangelogURL = GetURL(webResponse.ResponseUri, args.ChangelogURL);
    DownloadURL = args.DownloadURL = GetURL(webResponse.ResponseUri, args.DownloadURL);
    InstallerArgs = args.InstallerArgs ?? String.Empty;
    HashingAlgorithm = args.HashingAlgorithm ?? "MD5";
    Checksum = args.Checksum ?? String.Empty;
    webResponse.Close();
    if (Mandatory)
    {
        ShowRemindLaterButton = false;
        ShowSkipButton = false;
    }
    else
    {
        using (RegistryKey updateKey = Registry.CurrentUser.OpenSubKey(RegistryLocation))
        {
            if (updateKey != null)
            {
                object skip = updateKey.GetValue("skip");
                object applicationVersion = updateKey.GetValue("version");
                if (skip != null && applicationVersion != null)
                {
                    string skipValue = skip.ToString();
                    var skipVersion = new Version(applicationVersion.ToString());
                    if (skipValue.Equals("1") && CurrentVersion <= skipVersion)
                        return;
                    if (CurrentVersion > skipVersion)
                    {
                        using (RegistryKey updateKeyWrite = Registry.CurrentUser.CreateSubKey(RegistryLocation))
                        {
                            if (updateKeyWrite != null)
                            {
                                updateKeyWrite.SetValue("version", CurrentVersion.ToString());
                                updateKeyWrite.SetValue("skip", 0);
                            }
                        }
                    }
                }
                object remindLaterTime = updateKey.GetValue("remindlater");
                if (remindLaterTime != null)
                {
                    DateTime remindLater = Convert.ToDateTime(remindLaterTime.ToString(),
                        CultureInfo.CreateSpecificCulture("en-US").DateTimeFormat);
                    int compareResult = DateTime.Compare(DateTime.Now, remindLater);
                    if (compareResult < 0)
                    {
                        e.Cancel = false;
                        e.Result = remindLater;
                        return;
                    }
                }
            }
        }
    }
    args.IsUpdateAvailable = CurrentVersion > InstalledVersion;
    args.InstalledVersion = InstalledVersion;
    e.Cancel = false;
    e.Result = args;
}
