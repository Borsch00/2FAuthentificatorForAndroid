using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using _2FAuthAndroidLibrary;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Windows2FAuth
{
    public partial class FormMain : Form
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        public const int WM_LBUTTONDOWN = 0x0201;
        [DllImportAttribute("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImportAttribute("user32.dll")]
        public static extern bool ReleaseCapture();

        CSteamAuth cSteamAuth;
        bool keepRefreshCode;
        bool keepRefreshPendings;
        private bool WantClose = false;
        private bool loading;
        const string Programmname = "Steam authenticator";
        private string _secret = "";
        private string sgaPath = "";
        public FormMain()
        {
            InitializeComponent();
            notifyIconMain.Icon = global::Windows2FAuth.Properties.Resources.icon_closed;
            this.Width = 320; // Change width
            this.Height = 420; // and height
            labelStatus.Location = new Point(0, this.Height - labelStatus.Height - 40 /*Magick number*/ );
            foreach (var contr in this.Controls)
                if (contr is FlowLayoutPanel)
                {
                    (contr as Control).Location = new Point(0, 26);
                    (contr as Control).Width = 305;
                    (contr as Control).Height = 410;
                }
            cSteamAuth = new CSteamAuth();

            flowLayoutPanelLinker.Visible = false;
            flowLayoutPanelPendingConfirmation.Visible = false;
            flowLayoutPanelTwoFactorCodes.Visible = false;
            flowLayoutPanelLogin.Visible = false;
            flowLayoutPanelCrypto.Visible = false;
            toolStripMain.Renderer = new WithoutBorder();
            labelStatus.Location = new Point(0, this.Height - labelStatus.Height);
        }

        private void FormMain_Shown(object sender, EventArgs e)
        {
            Logging.LogInfo("==============Program start==============");
            if (File.Exists(CSteamAuth.SGAccountFile))
            {
               var fileInfo = new FileInfo(CSteamAuth.SGAccountFile);
                if (fileInfo.Length > 0)
                {
                    flowLayoutPanelCrypto.Visible = true;
                    defTextBoxCryptoCode.Focus();
                    return;
                }
                else
                    File.Delete(CSteamAuth.SGAccountFile);
            }
            importToolStripMenuItem.Visible = true;
            flowLayoutPanelLogin.Visible = true;
        }
        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            Logging.LogInfo("==============Program close==============");
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if(e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = !WantClose; // Hide if user dont wanna close
                this.Hide();
            }
         }

        public async Task Login()
        {
            string username = defTextBoxUsername.text;
            string password = defTextBoxPassword.text;
            string emailcode = defTextBoxEmailCode.text;
            string captcha = defTextBoxCaptcha.text;
            string twoFactorCode = defTextBoxTwoFactorCode.text;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                buttonLogin.Enabled = true;
                return;
            }
            
            LoadingOn();
            panelCaptcha.Visible = false;
            panelEmailCode.Visible = false;

            var res = await cSteamAuth.Login(username,password,captcha,emailcode,twoFactorCode).ConfigureAwait(true);
            switch (res)
            {
                case SteamAuth.UserLogin.LoginResult.NeedEmail:
                    panelEmailCode.Visible = true;
                    defTextBoxEmailCode.Focus();
                    break;
                case SteamAuth.UserLogin.LoginResult.BadCredentials:
                    break;
                case SteamAuth.UserLogin.LoginResult.NeedCaptcha:
                    captchaUpdate();
                    defTextBoxCaptcha.Focus();
                    break;
                case SteamAuth.UserLogin.LoginResult.LoginOkay:
                    flowLayoutPanelLogin.Visible = false;
                    flowLayoutPanelLinker.Visible = true;
                    buttonLink.Focus();

                    defTextBoxUsername.Reset();
                    defTextBoxPassword.Reset();
                    defTextBoxEmailCode.Reset();
                    defTextBoxCaptcha.Reset();
                    defTextBoxTwoFactorCode.Reset();
                    
                    toolStripDropDownMenu.Visible = false;
                    break;
                case SteamAuth.UserLogin.LoginResult.Need2FA:
                    MessageBox.Show("This account already have authenticator. You must delete him.", "Authenticator already exist", MessageBoxButtons.OK);
                    this.Close();
                    break;
            }
            labelStatus.Text = res.ToString();
            LoadingOff();
            buttonLogin.Enabled = true;
        }
        public async Task Link()
        {
            string phoneNumber = defTextBoxPhone.text;

            LoadingOn();
            panelSMS.Visible = false;
            panelPhone.Visible = false;
            panelSecret.Visible = false;

            var res = await cSteamAuth.Link(phoneNumber).ConfigureAwait(true);
            switch(res)
            {
                case LinkResult.MustProvidePhoneNumber:
                    panelPhone.Visible = true;
                    defTextBoxPhone.Focus();
                    break;
                case LinkResult.AuthenticatorPresent:
                    MessageBox.Show("This account already have authenticator. You must delete him.", "Authenticator already exist", MessageBoxButtons.OK);
                    this.Close();
                    break;
                case LinkResult.MustRemovePhoneNumber:
                    MessageBox.Show("This account already have phone number. You must remove him to use other number.", "Phone number already provided", MessageBoxButtons.OK);
                    this.Close();
                    break;
                case LinkResult.AwaitingFinalization:
                    panelPhone.Visible = false;
                    panelSMS.Visible = true;
                    panelSecret.Visible = true;
                    panelFinalizeLink.Visible = true;
                    buttonLink.Text = "Resend code";
                    defTextBoxSMS.Focus();
                    break;
            }
            labelStatus.Text = res.ToString();
            LoadingOff();
            buttonLink.Enabled = true;
        }
        public async Task FinalizeLink()
        {
            string smsCode = defTextBoxSMS.text;
            string sSecret = defTextBoxSecret.text;

            panelSecret.Visible = false;
            panelSMS.Visible = false;

            LoadingOn();

            var res  = await cSteamAuth.FinalizeLink(smsCode,sSecret).ConfigureAwait(true);
            switch (res)
            {
                case FinalizeResult.BadSMSCode:
                    defTextBoxSMS.Visible = true;
                    defTextBoxSMS.Reset();
                    defTextBoxSMS.Focus();
                    break;
                case FinalizeResult.IncorrectSecretCode:
                    defTextBoxSecret.Visible = true;
                    defTextBoxSecret.Reset();
                    defTextBoxSecret.Focus();
                    break;
                case FinalizeResult.CantSaveAccountData:
                    MessageBox.Show("Cant save steamguard file, please do it manualy.", "Steamguard saving error.", MessageBoxButtons.OK);
                    defTextBoxSMS.Reset();
                    defTextBoxPhone.Reset();
                    defTextBoxSecret.Reset();
                    OpenCodesTab();
                    toolStripDropDownMenu.Visible = true;
                    break;
                case FinalizeResult.Success:
                    defTextBoxSMS.Reset();
                    defTextBoxPhone.Reset();
                    defTextBoxSecret.Reset();
                    OpenCodesTab();
                    toolStripDropDownMenu.Visible = true;
                    break;
            }
            buttonFinalize.Enabled = true;
            labelStatus.Text = res.ToString();
            LoadingOff();
        }
        public async Task DeLink()
        {
            LoadingOn();

            if (await cSteamAuth.DeleteAuthenticator().ConfigureAwait(true))
            {
                flowLayoutPanelTwoFactorCodes.Visible = false;
                flowLayoutPanelLogin.Visible = true;
                keepRefreshCode = false;
                keepRefreshPendings = false;
            }
            else
            {
                LoadingOff();
                labelStatus.Text = "Cant delete authenticator!";
            }
        }
        private async Task ShowRevocationCode()
        {
            var code = cSteamAuth.GetRevocationCode();
            if (string.IsNullOrEmpty(code))
            {
                buttonShowRevocationCode.Enabled = true;
                return;
            }
            panelRevocationCode.Visible = true;
            textBoxRevocationCode.Text = code;

            Timer TimerRevocationCode = new Timer() { Interval = 1000, Enabled = true };
            TimerRevocationCode.Tick += TimerRevocationCode_Tick;
            TimerRevocationCode_Tick(null, null);
            await Task.Delay(TimeSpan.FromSeconds(10));
            TimerRevocationCode.Stop();
            progressBarRevocationCode.Value = 0;

            panelRevocationCode.Visible = false;
            textBoxRevocationCode.Text = "";
            buttonShowRevocationCode.Enabled = true;
        }
        private void LoadSGAccount(string secret)
        {
            defTextBoxCryptoCode.Reset(); // Reset text box
            toolStripDropDownMenu.Visible = true;
            if (secret.Length < 4)
                return;
            _secret = secret;

            if (cSteamAuth.LoadAuthenticator(secret,sgaPath))
            {
                OpenCodesTab();
                exportToolStripMenuItem.Visible = true;
                importToolStripMenuItem.Visible = false;
                RefreshPendings().Forget();
                this.Focus();
                notifyIconMain.Icon = Properties.Resources.icon_opened;
                this.Icon = Properties.Resources.icon_opened;
                return;
            }
            labelStatus.Text = "Cant load steam guard file!";
        }
        private void OpenCodesTab()
        {
            flowLayoutPanelLogin.Visible = false;
            flowLayoutPanelLinker.Visible = false;
            flowLayoutPanelPendingConfirmation.Visible = false;
            flowLayoutPanelCrypto.Visible = false;
            flowLayoutPanelTwoFactorCodes.Visible = true;
            this.Text = $"{Programmname} ({cSteamAuth.GetAccountName()})";
            RefreshCode().Forget();
        }
        private void OpenPendingsTab()
        {
            flowLayoutPanelLinker.Visible = false;
            flowLayoutPanelLogin.Visible = false;
            flowLayoutPanelCrypto.Visible = false;
            flowLayoutPanelTwoFactorCodes.Visible = false;
            flowLayoutPanelPendingConfirmation.Visible = true;
        }
        private async Task RefreshCode()
        {
            if (keepRefreshCode == false) //Starting
                keepRefreshCode = true;
            toolStripTextBox2FACodes.Visible = true;

            while (keepRefreshCode)//Updating
            {
                string sTwoFactorCode = await cSteamAuth.Get2FACode().ConfigureAwait(true);
                int iTwoFactorCodeTime = await cSteamAuth.Get2FACodeLeft().ConfigureAwait(true);
                try
                {
                    this.BeginInvoke(new MethodInvoker(delegate
                    {
                        if (!(string.IsNullOrEmpty(sTwoFactorCode) || textBoxTwoFactorCode.Text == sTwoFactorCode))
                            textBoxTwoFactorCode.Text = sTwoFactorCode;
                        if (toolStripTextBox2FACodes.Text != sTwoFactorCode)
                            toolStripTextBox2FACodes.Text = sTwoFactorCode;

                        if (!keepRefreshCode)
                            textBoxTwoFactorCode.Text = "";
                    }));
                }
                catch (Exception e) { Logging.LogError("Cant invoke to UI: " + e.Message); }
                await Task.Delay(1000);
            }
            return;
        }
        private async Task RefreshPendings()
        {
            panelPendingButtons.Visible = false;
            LoadingOn();

            if (keepRefreshPendings == false)
                keepRefreshPendings = true;
            while(keepRefreshPendings)
            {
                bool AddedNew = false; // Are we added a new element to the list?
                var confirmationList = await cSteamAuth.GetConfirmations().ConfigureAwait(true);
                foreach (var confirmation in confirmationList)
                    if (!checkedListBoxPendings.checkedListBox.Items.Contains(confirmation))
                    {
                        checkedListBoxPendings.checkedListBox.Items.Add(confirmation);
                        AddedNew = true;
                    }

                LoadingOff();
                bool empty = (checkedListBoxPendings.checkedListBox.Items.Count <= 0);
                panelPendingButtons.Visible = !empty;
                checkedListBoxPendings.Enabled = !empty;

                if(AddedNew)
                {
                    checkedListBoxPendings.Update();
                    if (this.WindowState == FormWindowState.Minimized || this.Focused == false)
                        notifyIconMain.BalloonTipClicked += new EventHandler(buttonGoToPendings_Click);
                        notifyIconMain.ShowBalloonTip(3000, "Pending", "You have new pending", ToolTipIcon.Info);
                }
                await Task.Delay(1000); // Wait 1 sec
            }
        }
        private void captchaUpdate()
        {
            panelCaptcha.Visible = true;
            defTextBoxCaptcha.Text = "";

            if (string.IsNullOrEmpty(cSteamAuth.captchaGID))
                return;

            string filepath = cSteamAuth.GetCaptchaFile(cSteamAuth.captchaGID);
            if (string.IsNullOrEmpty(filepath))
                return;
            using (FileStream stream = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {
                pictureBoxCaptcha.Image = Image.FromStream(stream);
            }
        }
        private async Task LoadingOn()
        {
            loading = true;
            while (loading)
            {
                labelStatus.Text = ".";
                await Task.Delay(300);
                if (loading)
                {
                    labelStatus.Text = "..";
                    await Task.Delay(300);
                }
                if (loading)
                {
                    labelStatus.Text = "...";
                    await Task.Delay(300);
                }
            }
        }
        private void LoadingOff()
        {
            loading = false;
            if(labelStatus.Text.StartsWith("."))
                labelStatus.Text = "";
        }

        private async void buttonLogin_Click(object sender, EventArgs e){
            ((Button)sender).Enabled = false;
            await Login();
            ((Button)sender).Enabled = true;
        } //Login button
        private async void buttonLink_Click(object sender, EventArgs e){
            ((Button)sender).Enabled = false;
            await Link();
            ((Button)sender).Enabled = true;
        } // LinkButton
        private async void buttonFinalize_Click(object sender, EventArgs e)
        {
            buttonFinalize.Enabled = false;
            await FinalizeLink();
            ((Button)sender).Enabled = true;
        } // Finalize link button
        private void buttonGoToPendings_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Normal;
            this.Activate();
            this.Show();

            OpenPendingsTab();
        } // Go to pendings tab
        private async void buttonShowRevocationCode_Click(object sender, EventArgs e)
        {
            ((Button)sender).Enabled = false;
            await ShowRevocationCode();
            ((Button)sender).Enabled = true;
        } // Show revocation code button
        private async void buttonDeLink_Click(object sender, EventArgs e)
        {
            // TODO: Show main menu, after delinking, and delete sga file?
            if (MessageBox.Show("Are you sure?", "Warning", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
                return;

            ((Button)sender).Enabled = false;
            await DeLink();
            ((Button)sender).Enabled = true;
        } // Delete authenticator button
        private async void buttonPendingAccept_Click(object sender, EventArgs e)
        {
            if (checkedListBoxPendings.checkedListBox.CheckedItems.Count <= 0)
                return;
            (sender as Button).Enabled = false;

            keepRefreshPendings = false;// Turn off list modifing

            List<Confirmation_light> confirmationList = new List<Confirmation_light>();
            confirmationList = checkedListBoxPendings.checkedListBox.CheckedItems.Cast<Confirmation_light>().ToList();

            foreach (var confirmation in confirmationList)
            {
                if (await (confirmation as Confirmation_light).Confirm().ConfigureAwait(true))
                    checkedListBoxPendings.checkedListBox.Items.Remove(confirmation);
                checkedListBoxPendings.Update();
            }

            RefreshPendings().Forget();// Keep refreshing
            (sender as Button).Enabled = true;
        } // Pending Accept
        private async void buttonPendingDeny_Click(object sender, EventArgs e)
        {
            if (checkedListBoxPendings.checkedListBox.CheckedItems.Count <= 0)
                return;
            (sender as Button).Enabled = false;

            keepRefreshPendings = false;// Turn off list modifing

            List<Confirmation_light> confirmationList = new List<Confirmation_light>();
            confirmationList = checkedListBoxPendings.checkedListBox.CheckedItems.Cast<Confirmation_light>().ToList();

            foreach (var confirmation in confirmationList)
            {
                if (await (confirmation as Confirmation_light).Deny().ConfigureAwait(true))
                    checkedListBoxPendings.checkedListBox.Items.Remove(confirmation);
                checkedListBoxPendings.Update();
            }

            RefreshPendings().Forget();// Keep refreshing
            (sender as Button).Enabled = true;
        } // Pending Deny
        private void buttonGoToCodes_Click(object sender, EventArgs e)
        {
            OpenCodesTab();
            // TODO: Create refresh button
        } // Go to codes Tab
        private void exportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_secret))
                return;

            var saveDialog = new SaveFileDialog();
            saveDialog.Filter = "sga files (*.sga)|*.sga";
            var dialogRes = saveDialog.ShowDialog();
            if (dialogRes == DialogResult.OK)
                labelStatus.Text = "Saving result: " + cSteamAuth.SaveSGAccount(_secret, saveDialog.FileName);
        } // Export steamguard file
        private void importToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Turn all off
            flowLayoutPanelLinker.Visible = false;
            flowLayoutPanelLogin.Visible = false;
            flowLayoutPanelPendingConfirmation.Visible = false;
            flowLayoutPanelTwoFactorCodes.Visible = false;
            toolStripDropDownMenu.Visible = false;
            keepRefreshCode = false;
            keepRefreshPendings = false;

            var openDialog = new OpenFileDialog() { Filter = "sga files (*.sga)|*.sga" };
            var dialogRes = openDialog.ShowDialog();
            if (dialogRes == DialogResult.OK && !string.IsNullOrEmpty(openDialog.FileName))
            {
                flowLayoutPanelCrypto.Visible = true;
                sgaPath = openDialog.FileName;
            }
            else
            {
                flowLayoutPanelLogin.Visible = true;
                toolStripMain.Visible = true;
                toolStripDropDownMenu.Visible = true;
            }
        } // Import steamguard file
        private void notifyIconMain_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.WindowState = FormWindowState.Normal;
            this.Activate();
            this.Show();
        } // Show form after double click
        private void closeToolStripMenuItemClose_Click(object sender, EventArgs e)
        {
            WantClose = true;
            this.Close();
        } // Close form
        private void defTextBoxPhone_KeyPress(object sender, KeyPressEventArgs e)
        {
            if ((e.KeyChar <= 47 || e.KeyChar >= 58) && e.KeyChar != 8 && e.KeyChar != 43)
                e.Handled = true;
        } // Phone textBox filter
        private void defTextBoxCryptoCode_KeyPress(object sender, KeyPressEventArgs e) 
        {
            if ((e.KeyChar <= 47 || e.KeyChar >= 58) && e.KeyChar != 8)
                e.Handled = true;
        }// Secret code filter
        private void textBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            buttonLogin.Enabled = false;
            Login().Forget();
        } //Username, password, captcha, email textboxes
        private void defTextBoxCryptoCode_TextChanged(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty((sender as defTextBox).text))
                if ((sender as defTextBox).text.Length == 4)
                    LoadSGAccount(defTextBoxCryptoCode.text);
        } // Load sga if user enter 4 numbers
        private void checkedListBoxPendings_MouseDown(object sender, MouseEventArgs e)
        {
            int y = e.Y / ((ListBox)sender).ItemHeight;
            if (y < ((ListBox)sender).Items.Count)
                ((ListBox)sender).SelectedIndex = y;
            else
                ((ListBox)sender).SelectedIndex = -1;
        } // Selecting empty zones
        private void toolStripTextBox2FACodes_Click(object sender, EventArgs e)
        {
            //toolStripTextBox2FACodes.SelectAll();
            Clipboard.SetText(toolStripTextBox2FACodes.Text);
            notifyIconMain.ShowBalloonTip(1000, "Copy to clipboard", "Two factor code was copied to clipboard.", ToolTipIcon.Info);
        } // Copy 2fa code from toolstrip to clipboard
        private void textBoxTwoFactorCode_Click(object sender, EventArgs e)
        {
            //textBoxTwoFactorCode.SelectAll();
            Clipboard.SetText(textBoxTwoFactorCode.Text);
            notifyIconMain.ShowBalloonTip(1000, "Copy to clipboard", "Two factor code was copied to clipboard.", ToolTipIcon.Info);
        }// Copy 2fa code from form to clipboard
        private void toolStripButtonClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }// Close form

        private void TimerRevocationCode_Tick(object sender, EventArgs e)
        {
            progressBarRevocationCode.Increment(10);
        } // Revocation code showing timer

        private void toolStripMain_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }
    public static class Extensions
    {
        public static void Forget(this Task task){ }
        public static void Center(this Control contr)
        {
            if (contr.Parent != null)
                if (!(contr.Parent is FormMain))
                    contr.Location = new Point((contr.Parent.Width - contr.Width) / 2, contr.Location.Y);
        }
    }
    public class defTextBox : TextBox
    {
        private string defText;
        private Color foreColor;
        public string text { get { return isDef() ? null : Text; } }
        public bool isDef()
        {
            if (!this.Created)
                return true;
            return this.Text == defText;
        }
        public void Reset()
        {
            Text = defText;
        }
        protected override void OnCreateControl()
        {
            defText = this.Text;
            foreColor = this.ForeColor;
            this.ForeColor = Color.FromArgb(160, 160, 160);
            base.OnCreateControl();
        }
        protected override void OnLeave(EventArgs e)
        {
            if(this.Text == "")
            {
                this.Text = defText;
                this.ForeColor = Color.FromArgb(160, 160, 160);
            }
            base.OnLeave(e);
        }
        protected override void OnEnter(EventArgs e)
        {

            if (this.Text == defText)
            {
                this.Text = "";
                this.ForeColor = foreColor;
            }
            base.OnEnter(e);
        }
    }
    public class CheckedListBoxExtended : Control
    {
        public CheckBox CheckAll = new CheckBox() { Text = "Select all", Dock = DockStyle.Top };
        public CheckedListBox checkedListBox = new CheckedListBox() { CheckOnClick = true };
        bool manualCheckChange;
        public CheckedListBoxExtended()
        {
            this.Controls.Add(CheckAll);
            this.Controls.Add(checkedListBox);

            CheckAll.CheckedChanged += new EventHandler(this.CheckAll_checkChange);
            checkedListBox.ItemCheck += CheckedListBox_ItemCheck;
            checkedListBox.Location = new Point(0, CheckAll.Height);
        }

        private void CheckedListBox_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (checkedListBox.Items.Count == checkedListBox.CheckedItems.Count && e.NewValue == CheckState.Unchecked)
            {
                manualCheckChange = true;
                CheckAll.Checked = false;
            }
            else if ((checkedListBox.Items.Count - 1) == checkedListBox.CheckedItems.Count && e.NewValue == CheckState.Checked)
            {
                manualCheckChange = true;
                CheckAll.Checked = true;
            }
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            checkedListBox.Size = new Size(this.Size.Width, this.Size.Height - CheckAll.Size.Height);
        }
        void CheckAll_checkChange(object sender,EventArgs e)
        {
            if(!manualCheckChange)
                for (int i = 0; i < checkedListBox.Items.Count; i++)
                    checkedListBox.SetItemChecked(i, CheckAll.Checked);
            CheckAll.Text = CheckAll.Checked ? "Deselect all" : "Select all";

            manualCheckChange = false;
        }
    }
    public class WithoutBorder : ToolStripSystemRenderer
    {
        
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            //base.OnRenderToolStripBorder(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.ForeColor;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
                e.Item.BackColor = Color.FromArgb(150, 200, 0, 0);
            else
                e.Item.BackColor = e.ToolStrip.BackColor;
            base.OnRenderButtonBackground(e);
        }

    }
    }
