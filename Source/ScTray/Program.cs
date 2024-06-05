using ScreenConnect.Integration;
using ScreenConnect.Integration.Exceptions;
using Security.Windows.Forms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace ScreenConnect.ScTray
{
    internal class Program : Form
    {
        #region Private Fields

        private static SCHostInterface sc;

        private NotifyIcon trayIcon;

        private ContextMenu trayMenu;

        #endregion Private Fields

        #region Public Constructors

        public Program()
        {
            this.trayMenu = new ContextMenu();

            this.trayMenu.Popup += new EventHandler(trayMenu_Popup);

            this.trayIcon = new NotifyIcon();
            this.trayIcon.Text = "ScreenConnect Tray";
            this.trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            this.trayIcon.ContextMenu = this.trayMenu;
            this.trayIcon.Visible = true;
        }

        #endregion Public Constructors

        #region Public Methods

        public static DialogResult InputBox(string title, string promptText, ref string value)
        {
            Form form = new Form();
            Label label = new Label();
            TextBox textBox = new TextBox();
            Button buttonOk = new Button();
            Button buttonCancel = new Button();

            form.Text = title;
            label.Text = promptText;
            textBox.Text = value;

            buttonOk.Text = "OK";
            buttonCancel.Text = "Cancel";
            buttonOk.DialogResult = DialogResult.OK;
            buttonCancel.DialogResult = DialogResult.Cancel;

            label.SetBounds(9, 20, 372, 13);
            textBox.SetBounds(12, 36, 372, 20);
            buttonOk.SetBounds(228, 72, 75, 23);
            buttonCancel.SetBounds(309, 72, 75, 23);

            label.AutoSize = true;
            textBox.Anchor = textBox.Anchor | AnchorStyles.Right;
            buttonOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            form.ClientSize = new Size(396, 107);
            form.Controls.AddRange(new Control[] { label, textBox, buttonOk, buttonCancel });
            form.ClientSize = new Size(Math.Max(300, label.Right + 10), form.ClientSize.Height);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.AcceptButton = buttonOk;
            form.CancelButton = buttonCancel;

            DialogResult dialogResult = form.ShowDialog();
            value = textBox.Text;
            return dialogResult;
        }

        #endregion Public Methods

        #region Protected Methods

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                this.trayIcon.Dispose();
            }

            base.Dispose(isDisposing);
        }

        protected override void OnLoad(EventArgs e)
        {
            this.Visible = false;
            this.ShowInTaskbar = false;

            base.OnLoad(e);
        }

        #endregion Protected Methods

        #region Private Methods

        private static void Main(string[] args)
        {
            WebRequest.DefaultWebProxy = null;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (args.Length == 0)
            {
                MessageBox.Show("Parameter required: ScreenConnect URL", "ScreenConnect Tray", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string bUrl = args[0];
            UserCredentialsDialog ucd = new UserCredentialsDialog(bUrl, "Login to ScreenConnect", "Enter ScreenConnect password");
            string oneTimePassword = null;
            sc = new SCHostInterface(bUrl);
            while (true)
            {
                if (oneTimePassword == null && ucd.ShowDialog() == DialogResult.Cancel)
                {
                    return;
                }
                try
                {
                    if (oneTimePassword == null)
                    {
                        sc.Login(ucd.User, ucd.PasswordToString());
                    }
                    else
                    {
                        try
                        {
                            sc.LoginOneTimePassword(oneTimePassword);
                        }
                        finally
                        {
                            oneTimePassword = null;
                        }
                    }
                    ucd.ConfirmCredentials(true);
                    break;
                }
                catch (ScreenConnectAuthenticationException e)
                {
                    if (e.OneTimePasswordRequired)
                    {
                        DialogResult dResult = InputBox("One Time Password", "Enter One Time Password:", ref oneTimePassword);
                        if (dResult == DialogResult.OK) continue;
                    }
                    ucd.Flags = UserCredentialsDialogFlags.IncorrectPassword;
                    ucd.ConfirmCredentials(false);
                    continue;
                }
                catch (WebException e)
                {
                    if (e.Status == WebExceptionStatus.ProtocolError)
                    {
                        ucd.Flags = UserCredentialsDialogFlags.IncorrectPassword;
                        ucd.ConfirmCredentials(false);
                        continue;
                    }
                    MessageBox.Show(e.ToString(), e.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString(), e.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            Application.Run(new Program());
        }

        private void buildMenu(MenuItem menu, List<SCHostCategory> type)
        {
            foreach (SCHostCategory c in type)
            {
                SCHostCategory cInt = c;
                MenuItem cat = new MenuItem(c.name + " (" + c.count + ")");
                cat.Popup += delegate (object sender1, EventArgs e1)
                {
                    cat.MenuItems.Clear();
                    foreach (SCHostSession s in cInt.sessions)
                    {
                        SCHostSession sInt = s;
                        string name = sInt.name;
                        if (sInt.hostConnected) name = sInt.name + " (Connected)";
                        if (sInt.guestUser != "") name += " [" + sInt.guestUser + "]";
                        MenuItem item = new MenuItem(name);
                        item.MenuItems.Add("");
                        item.Popup += delegate (object sender2, EventArgs e2)
                          {
                              item.MenuItems.Clear();
                              MenuItem connect = new MenuItem("connect", delegate (object sender3, EventArgs e3)
                              {
                                  sInt.connect();
                              });
                              item.MenuItems.Add(connect);
                              MenuItem runcommand = new MenuItem("run command", delegate (object sender3, EventArgs e3)
                              {
                                  string input = null;
                                  DialogResult dResult = InputBox("Run Command", "Enter Command:", ref input);
                                  if (dResult == DialogResult.OK)
                                  {
                                      try
                                      {
                                          MessageBox.Show(sInt.runCommand(input), "Command Result");
                                      }
                                      catch (Exception e)
                                      {
                                          MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                      }
                                  }
                              });
                              item.MenuItems.Add(runcommand);
                              MenuItem mDetails = new MenuItem("details");
                              mDetails.MenuItems.Add("");
                              mDetails.Popup += delegate (object sender3, EventArgs e3)
                              {
                                  mDetails.MenuItems.Clear();
                                  try
                                  {
                                      SCHostSessionDetails details = sInt.details;
                                      if (details.machineName != null) mDetails.MenuItems.Add(new MenuItem("Name: " + details.machineName) { Enabled = false });
                                      if (details.machineDomain != null) mDetails.MenuItems.Add(new MenuItem("Domain: " + details.machineDomain) { Enabled = false });
                                      if (details.networkAddress != null) mDetails.MenuItems.Add(new MenuItem("IP: " + details.networkAddress) { Enabled = false });
                                      if (details.processorName != null) mDetails.MenuItems.Add(new MenuItem("CPU: " + details.processorName) { Enabled = false });
                                      if (details.systemMemoryAvailableMegabytes != null && details.systemMemoryTotalMegabytes != null) mDetails.MenuItems.Add(new MenuItem("RAM: " + details.systemMemoryAvailableMegabytes + "/" + details.systemMemoryTotalMegabytes + " MB available") { Enabled = false });
                                  }
                                  catch { }
                              };
                              item.MenuItems.Add(mDetails);
                          };
                        if (!sInt.guestConnected) item.Enabled = false;
                        cat.MenuItems.Add(item);
                    }
                };
                if (c.count > 0) cat.MenuItems.Add("");
                menu.MenuItems.Add(cat);
            }
        }

        private void OnCreateSession(object sender, EventArgs e)
        {
            string input = null;
            DialogResult dResult = InputBox("Create Session", "Enter Session Name:", ref input);
            if (dResult == DialogResult.OK)
            {
                sc.createSupportSession(input, true, null).connect();
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void trayMenu_Popup(object sender, EventArgs e)
        {
            this.trayMenu.MenuItems.Clear();
            this.trayMenu.MenuItems.Add("New Session", OnCreateSession);
            sc.refreshCategories();
            MenuItem support = new MenuItem("Support");
            buildMenu(support, sc.support);
            MenuItem meet = new MenuItem("Meet");
            buildMenu(meet, sc.meet);
            MenuItem access = new MenuItem("Access");
            buildMenu(access, sc.access);

            this.trayMenu.MenuItems.Add(support);
            this.trayMenu.MenuItems.Add(meet);
            this.trayMenu.MenuItems.Add(access);
            this.trayMenu.MenuItems.Add("Exit", OnExit);
        }

        #endregion Private Methods
    }
}