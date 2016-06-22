using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Windows.Forms;
using System.Drawing;
using Security.Windows.Forms;
using ScreenConnect.Integration;

namespace ScreenConnect.ScTray
{
    class Program : Form
    {
        private static SCHostInterface sc;

        static void Main(string[] args)
        {
            WebRequest.DefaultWebProxy = null;
            if (args.Length == 0)
            {
                MessageBox.Show("Parameter required: ScreenConnect URL", "ScreenConnect Tray", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            String bUrl = args[0];
            UserCredentialsDialog ucd = new UserCredentialsDialog(bUrl, "Login to ScreenConnect", "Enter ScreenConnect password");
            while (true)
            {
                if (ucd.ShowDialog() == DialogResult.Cancel)
                {
                    return;
                }
                try
                {
                    sc = new SCHostInterface(bUrl, ucd.User, ucd.PasswordToString());
                    ucd.ConfirmCredentials(true);
                    break;
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

        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;

        public Program()
        {
            trayMenu = new ContextMenu();

            trayMenu.Popup += new EventHandler(trayMenu_Popup);

            trayIcon = new NotifyIcon();
            trayIcon.Text = "ScreenConnect Tray";
            trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            trayIcon.ContextMenu = trayMenu;
            trayIcon.Visible = true;
        }

        void trayMenu_Popup(object sender, EventArgs e)
        {
            trayMenu.MenuItems.Clear();
            trayMenu.MenuItems.Add("New Session", OnCreateSession);
            MenuItem support = new MenuItem("Support");
            buildMenu(support, sc.support);
            MenuItem meet = new MenuItem("Meet");
            buildMenu(meet, sc.meet);
            MenuItem access = new MenuItem("Access");
            buildMenu(access, sc.access);

            trayMenu.MenuItems.Add(support);
            trayMenu.MenuItems.Add(meet);
            trayMenu.MenuItems.Add(access);
            trayMenu.MenuItems.Add("Exit", OnExit);
        }

        void buildMenu(MenuItem menu, List<SCHostCategory> type)
        {
            foreach (SCHostCategory c in type)
            {
                SCHostCategory cInt = c;
                MenuItem cat = new MenuItem(c.name + " (" + c.count + ")");
                cat.Popup += delegate(Object sender1, EventArgs e1)
                {
                    cat.MenuItems.Clear();
                    foreach (SCHostSession s in cInt.sessions)
                    {
                        SCHostSession sInt = s;
                        String name = sInt.name;
                        if (sInt.hostConnected) name = sInt.name + " (Connected)";
                        if (sInt.guestUser != "") name += " [" + sInt.guestUser + "]";
                        MenuItem item = new MenuItem(name, delegate(Object sender2, EventArgs e2)
                        {
                            sInt.connect();
                        });
                        if (!sInt.guestConnected) item.Enabled = false;
                        cat.MenuItems.Add(item);
                    }
                };
                if (c.count > 0) cat.MenuItems.Add("");
                menu.MenuItems.Add(cat);
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            Visible = false;
            ShowInTaskbar = false;

            base.OnLoad(e);
        }

        private void OnExit(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void OnCreateSession(object sender, EventArgs e)
        {
            String input = null;
            DialogResult dResult = InputBox("Create Session", "Enter Session Name:",ref input);
            if (dResult == DialogResult.OK)
            {
                sc.createSupportSession(input, true, null).connect();
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                trayIcon.Dispose();
            }

            base.Dispose(isDisposing);
        }

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
    }
}
