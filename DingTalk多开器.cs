using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DingTalkMultiCore;

namespace DingTalkMultiOpenUI
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            DingTalkEngine.EnableDebugPrivilege();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly ListView listView;
        private readonly Button openButton;
        private readonly Button newButton;
        private readonly Button renameButton;
        private readonly Button deleteButton;
        private readonly Button refreshButton;
        private readonly Label tipLabel;

        private List<ProfileInfo> profiles;
        private bool busy;

        public MainForm()
        {
            Text = "钉钉多开 - 账号管理器";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(780, 430);
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;

            listView = new ListView();
            listView.Dock = DockStyle.Fill;
            listView.View = View.Details;
            listView.FullRowSelect = true;
            listView.GridLines = true;
            listView.MultiSelect = false;
            listView.Columns.Add("账号名称", 190);
            listView.Columns.Add("状态", 110);
            listView.Columns.Add("数据目录", 470);
            listView.DoubleClick += delegate { OpenSelectedProfile(); };

            tipLabel = new Label();
            tipLabel.Dock = DockStyle.Top;
            tipLabel.Height = 38;
            tipLabel.Padding = new Padding(6, 6, 6, 2);
            tipLabel.ForeColor = Color.DimGray;
            tipLabel.Text =
                "每个账号使用独立的钉钉数据目录，可同时启动任意多个。\r\n" +
                "新建后第一次打开会显示登录二维码，请用对应账号扫码登录。";

            openButton = new Button();
            openButton.Text = "打开选中账号";
            openButton.AutoSize = true;
            openButton.Padding = new Padding(8, 2, 8, 2);
            openButton.Click += delegate { OpenSelectedProfile(); };

            newButton = new Button();
            newButton.Text = "新建账号";
            newButton.AutoSize = true;
            newButton.Padding = new Padding(8, 2, 8, 2);
            newButton.Click += delegate { CreateNewProfile(); };

            renameButton = new Button();
            renameButton.Text = "重命名";
            renameButton.AutoSize = true;
            renameButton.Padding = new Padding(8, 2, 8, 2);
            renameButton.Click += delegate { RenameSelectedProfile(); };

            deleteButton = new Button();
            deleteButton.Text = "删除账号";
            deleteButton.AutoSize = true;
            deleteButton.Padding = new Padding(8, 2, 8, 2);
            deleteButton.Click += delegate { DeleteSelectedProfile(); };

            refreshButton = new Button();
            refreshButton.Text = "刷新状态";
            refreshButton.AutoSize = true;
            refreshButton.Padding = new Padding(8, 2, 8, 2);
            refreshButton.Click += delegate { RefreshList(true); };

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Top;
            buttonPanel.AutoSize = true;
            buttonPanel.Padding = new Padding(6, 6, 6, 6);
            buttonPanel.FlowDirection = FlowDirection.LeftToRight;
            buttonPanel.WrapContents = false;
            buttonPanel.Controls.Add(openButton);
            buttonPanel.Controls.Add(newButton);
            buttonPanel.Controls.Add(renameButton);
            buttonPanel.Controls.Add(deleteButton);
            buttonPanel.Controls.Add(refreshButton);

            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.Controls.Add(listView);
            root.Controls.Add(tipLabel);

            Controls.Add(root);
            Controls.Add(buttonPanel);

            Shown += delegate
            {
                profiles = DingTalkEngine.LoadProfiles();
                RefreshList(true);
            };

            FormClosing += delegate
            {
                if (busy)
                {
                    MessageBox.Show(
                        "正在处理，请稍候关闭。",
                        "钉钉多开",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            };
        }

        private void RefreshList(bool scanRunning)
        {
            if (profiles == null)
            {
                profiles = DingTalkEngine.LoadProfiles();
            }

            if (scanRunning)
            {
                Cursor oldCursor = Cursor;
                Cursor = Cursors.WaitCursor;
                try
                {
                    Application.DoEvents();
                    DingTalkEngine.RefreshRunningStatus(profiles);
                }
                finally
                {
                    Cursor = oldCursor;
                }
            }

            listView.BeginUpdate();
            try
            {
                listView.Items.Clear();
                foreach (ProfileInfo profile in profiles)
                {
                    ListViewItem item = new ListViewItem(profile.Name);
                    item.SubItems.Add(profile.Running ? "运行中" : "未运行");
                    item.SubItems.Add(profile.DataPath);
                    item.Tag = profile;
                    if (profile.Running)
                    {
                        item.ForeColor = Color.FromArgb(0, 128, 0);
                    }
                    listView.Items.Add(item);
                }
            }
            finally
            {
                listView.EndUpdate();
            }

            if (listView.Items.Count > 0)
            {
                listView.Items[0].Selected = true;
                listView.Select();
            }
        }

        private ProfileInfo GetSelectedProfile()
        {
            if (listView.SelectedItems == null || listView.SelectedItems.Count == 0)
            {
                return null;
            }
            return listView.SelectedItems[0].Tag as ProfileInfo;
        }

        private void OpenSelectedProfile()
        {
            ProfileInfo profile = GetSelectedProfile();
            if (profile == null)
            {
                MessageBox.Show(
                    "请先在列表中选择一个账号。",
                    "钉钉多开",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (profile.Running)
            {
                MessageBox.Show(
                    "该账号已经在运行中，无需重复打开。",
                    "钉钉多开",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            SetBusy(true);
            LaunchResult result;
            try
            {
                result = DingTalkEngine.LaunchProfile(profile, profiles);
            }
            finally
            {
                SetBusy(false);
            }

            RefreshList(true);

            switch (result)
            {
                case LaunchResult.Ok:
                    MessageBox.Show(
                        profile.Name + " 已启动，请在其窗口中完成登录。",
                        "钉钉多开",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    break;
                case LaunchResult.AlreadyRunning:
                    MessageBox.Show(
                        profile.Name + " 已经在运行。",
                        "钉钉多开",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    break;
                case LaunchResult.LauncherNotFound:
                    MessageBox.Show(
                        "未找到钉钉程序（DingtalkLauncher.exe / DingTalk.exe）。",
                        "钉钉多开",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
                case LaunchResult.PermissionDenied:
                    MessageBox.Show(
                        "权限不足，无法解除钉钉单实例互斥量。\r\n" +
                        "请右键本程序选择“以管理员身份运行”。",
                        "钉钉多开",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
                case LaunchResult.Timeout:
                    MessageBox.Show(
                        "等待 " + profile.Name + " 启动超时。\r\n" +
                        "可以点击“刷新状态”查看是否其实已经启动。",
                        "钉钉多开",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
            }
        }

        private void CreateNewProfile()
        {
            if (profiles == null)
            {
                profiles = DingTalkEngine.LoadProfiles();
            }

            int number = profiles.Count + 1;
            string name = "账号" + number;
            bool nameTaken = true;
            while (nameTaken)
            {
                nameTaken = false;
                foreach (ProfileInfo profile in profiles)
                {
                    if (string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        number++;
                        name = "账号" + number;
                        nameTaken = true;
                        break;
                    }
                }
            }

            ProfileInfo created = new ProfileInfo();
            created.Name = name;
            created.DataPath = DingTalkEngine.BuildNewProfilePath(profiles);
            profiles.Add(created);
            DingTalkEngine.SaveProfiles(profiles);
            RefreshList(false);

            listView.Items[listView.Items.Count - 1].Selected = true;
            MessageBox.Show(
                "已新建 " + name + "。\r\n数据目录：\r\n" + created.DataPath +
                "\r\n\r\n点击“打开选中账号”完成首次登录。",
                "钉钉多开",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void RenameSelectedProfile()
        {
            ProfileInfo profile = GetSelectedProfile();
            if (profile == null)
            {
                MessageBox.Show(
                    "请先选择要重命名的账号。",
                    "钉钉多开",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string newName = PromptDialog.Show(this, "输入新的账号名称：", profile.Name);
            if (string.IsNullOrEmpty(newName))
            {
                return;
            }

            newName = newName.Trim();
            if (newName.Length == 0)
            {
                return;
            }

            foreach (ProfileInfo other in profiles)
            {
                if (other != profile &&
                    string.Equals(other.Name, newName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "该名称已存在，请换一个。",
                        "钉钉多开",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            profile.Name = newName;
            DingTalkEngine.SaveProfiles(profiles);
            RefreshList(false);
        }

        private void DeleteSelectedProfile()
        {
            ProfileInfo profile = GetSelectedProfile();
            if (profile == null)
            {
                MessageBox.Show(
                    "请先选择要删除的账号。",
                    "钉钉多开",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DialogResult answer = MessageBox.Show(
                "确定删除列表中的账号“" + profile.Name + "”？\r\n\r\n" +
                "只删除账号管理列表，不会删除数据目录：\r\n" +
                profile.DataPath + "\r\n\r\n" +
                "如果该账号正在运行，请先退出钉钉再删除。",
                "钉钉多开",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
            {
                return;
            }

            profiles.Remove(profile);
            DingTalkEngine.SaveProfiles(profiles);
            RefreshList(false);
        }

        private void SetBusy(bool value)
        {
            busy = value;
            openButton.Enabled = !value;
            newButton.Enabled = !value;
            renameButton.Enabled = !value;
            deleteButton.Enabled = !value;
            refreshButton.Enabled = !value;
            Cursor = value ? Cursors.WaitCursor : Cursors.Default;
            if (value)
            {
                Application.DoEvents();
            }
        }
    }

    internal sealed class PromptDialog : Form
    {
        private readonly TextBox textBox;

        private PromptDialog(string title, string label, string initial)
        {
            Text = title;
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(420, 120);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label prompt = new Label();
            prompt.Text = label;
            prompt.Location = new Point(14, 14);
            prompt.AutoSize = true;

            textBox = new TextBox();
            textBox.Location = new Point(14, 40);
            textBox.Width = 392;
            textBox.Text = initial ?? string.Empty;

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(236, 80);
            ok.Size = new Size(80, 28);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(326, 80);
            cancel.Size = new Size(80, 28);

            Controls.Add(prompt);
            Controls.Add(textBox);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public static string Show(IWin32Window owner, string label, string initial)
        {
            using (PromptDialog dialog = new PromptDialog("输入名称", label, initial))
            {
                if (dialog.ShowDialog(owner) == DialogResult.OK)
                {
                    return dialog.textBox.Text;
                }
                return null;
            }
        }
    }
}
