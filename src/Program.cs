using System;
using System.Collections.Generic;
using System.Reflection;
using System.Drawing;
using System.Security.AccessControl;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Dungeon Keys")]
[assembly: AssemblyProduct("Dungeon Keys")]
[assembly: AssemblyDescription("Switch between Happy Dungeons accounts easily.")]
[assembly: AssemblyCompany("Totemo Fire")]
[assembly: AssemblyCopyright("Copyright (c) Totemo Fire")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace DungeonKeys
{
    public class Account
    {
        public string Gamertag = "";
        public string Gamerscore = "0";
        public string XuidBase = "";
        public string Secret = "";

        public Account Clone()
        {
            return new Account { Gamertag = Gamertag, Gamerscore = Gamerscore, XuidBase = XuidBase, Secret = Secret };
        }
    }

    // Reads/writes the game's winDurango.json while leaving every other part of the file untouched.
    public static class GameFile
    {
        public static readonly string RelativePath = Path.Combine("UserData", Path.Combine("LocalState", Path.Combine("WinDurango", "winDurango.json")));

        // Accepts the Mount folder (or a folder next to / above it) and returns the path to winDurango.json, or null.
        public static string Find(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return null;
            try
            {
                folder = folder.Trim().Trim('"');
                string direct = Path.Combine(folder, RelativePath);
                if (File.Exists(direct)) return direct;
                string mount = Path.Combine(Path.Combine(folder, "Mount"), RelativePath);
                if (File.Exists(mount)) return mount;
                if (Directory.Exists(folder))
                {
                    foreach (string sub in Directory.GetDirectories(folder))
                    {
                        string p = Path.Combine(sub, RelativePath);
                        if (File.Exists(p)) return p;
                    }
                }
            }
            catch { }
            return null;
        }

        static Regex FieldRegex(string key)
        {
            return new Regex("\"" + key + "\"\\s*:\\s*(?:\"((?:\\\\.|[^\"\\\\])*)\"|(-?[0-9][0-9.eE+\\-]*))", RegexOptions.CultureInvariant);
        }

        static string Get(string text, string key)
        {
            Match m = FieldRegex(key).Match(text);
            if (!m.Success) return null;
            if (m.Groups[1].Success) return Regex.Unescape(m.Groups[1].Value);
            return m.Groups[2].Value;
        }

        public static Account Read(string path)
        {
            string text = File.ReadAllText(path);
            string tag = Get(text, "gamertag");
            string xuid = Get(text, "xuidBase");
            string secret = Get(text, "secret");
            string score = Get(text, "gamerscore");
            if (tag == null || xuid == null || secret == null) return null;
            return new Account { Gamertag = tag, XuidBase = xuid, Secret = secret, Gamerscore = score ?? "0" };
        }

        static string Esc(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static string Set(string text, string key, string value)
        {
            Regex re = FieldRegex(key);
            Match m = re.Match(text);
            if (!m.Success) throw new InvalidDataException("The game file is missing the \"" + key + "\" entry.");
            bool wasQuoted = m.Groups[1].Success;
            bool quote = wasQuoted || key == "gamertag" || key == "secret" || value.Length == 0;
            string newValue = quote ? "\"" + Esc(value) + "\"" : value;
            Group g = wasQuoted ? m.Groups[1] : m.Groups[2];
            int start = wasQuoted ? g.Index - 1 : g.Index;
            int len = wasQuoted ? g.Length + 2 : g.Length;
            return text.Substring(0, start) + newValue + text.Substring(start + len);
        }

        // True when the file holds no account (the game will ask the player to create one).
        public static bool IsBlank(Account a)
        {
            return a != null && a.XuidBase.Trim().Length == 0 && a.Secret.Trim().Length == 0;
        }

        // Empties the account details so the game starts fresh with a new account.
        public static void Clear(string path)
        {
            Write(path, new Account { Gamertag = "", Gamerscore = "0", XuidBase = "", Secret = "" });
        }

        public static void Write(string path, Account a)
        {
            byte[] raw = File.ReadAllBytes(path);
            bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            string text = File.ReadAllText(path);
            text = Set(text, "gamertag", a.Gamertag);
            text = Set(text, "gamerscore", a.Gamerscore);
            text = Set(text, "xuidBase", a.XuidBase);
            text = Set(text, "secret", a.Secret);

            // The file might be marked read-only; undo that so we can update it.
            try
            {
                FileAttributes attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            }
            catch { }

            // Try a few times in case antivirus or the game is touching the file for a moment.
            byte[] data = new UTF8Encoding(bom).GetPreamble();
            byte[] body = new UTF8Encoding(false).GetBytes(text);
            byte[] all = new byte[data.Length + body.Length];
            Buffer.BlockCopy(data, 0, all, 0, data.Length);
            Buffer.BlockCopy(body, 0, all, data.Length, body.Length);
            bool triedPermissions = false;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        fs.Write(all, 0, all.Length);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    // Windows said no. Try giving the current Windows user permission to change this one file.
                    if (triedPermissions) throw;
                    triedPermissions = true;
                    if (!TryGrantAccess(path)) throw;
                }
                catch (IOException)
                {
                    if (attempt >= 5) throw;
                    System.Threading.Thread.Sleep(250);
                }
            }
        }

        static bool TryGrantAccess(string path)
        {
            try
            {
                var me = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                FileSecurity sec = File.GetAccessControl(path);
                // Remove any "deny" rules aimed at this user, then allow full control of this file.
                foreach (FileSystemAccessRule r in sec.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)))
                {
                    if (r.AccessControlType == AccessControlType.Deny && r.IdentityReference.Equals(me))
                        sec.RemoveAccessRuleSpecific(r);
                }
                sec.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
                File.SetAccessControl(path, sec);
                return true;
            }
            catch { return false; }
        }
    }

    public static class Store
    {
        static string dir;

        // Where the program keeps its own data. Prefers a place that already holds saved accounts,
        // otherwise the first place we are actually allowed to write to.
        public static string Dir
        {
            get
            {
                if (dir == null) dir = ChooseDir();
                return dir;
            }
        }

        static string ChooseDir()
        {
            var candidates = new List<string>();
            string[] roots = {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.GetTempPath()
            };
            var legacy = new List<string>();   // folder name used by earlier versions of this program
            foreach (string r in roots)
            {
                if (string.IsNullOrEmpty(r)) continue;
                candidates.Add(Path.Combine(r, "DungeonKeys"));
                legacy.Add(Path.Combine(r, "HappyDungeonsAccountManager"));
            }

            // Keep using a folder that already holds saved accounts (new name first, then the old name).
            foreach (var group in new[] { candidates, legacy })
            {
                foreach (string c in group)
                {
                    try { if (File.Exists(Path.Combine(c, "accounts.json"))) { if (CanWrite(c)) return c; } } catch { }
                }
            }
            foreach (string c in candidates)
            {
                if (CanWrite(c)) return c;
            }
            return candidates[0];
        }

        static bool CanWrite(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string t = Path.Combine(folder, "write-test.tmp");
                File.WriteAllText(t, "ok");
                File.Delete(t);
                return true;
            }
            catch { return false; }
        }

        static string AccountsPath { get { return Path.Combine(Dir, "accounts.json"); } }
        static string ConfigPath { get { return Path.Combine(Dir, "gamefolder.txt"); } }

        public static List<Account> Load()
        {
            try
            {
                if (File.Exists(AccountsPath))
                {
                    var list = new JavaScriptSerializer().Deserialize<List<Account>>(File.ReadAllText(AccountsPath));
                    if (list != null) return list;
                }
            }
            catch { }
            return new List<Account>();
        }

        public static void Save(List<Account> accounts)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(AccountsPath, new JavaScriptSerializer().Serialize(accounts), new UTF8Encoding(false));
        }

        public static string LoadFolder()
        {
            try { if (File.Exists(ConfigPath)) return File.ReadAllText(ConfigPath).Trim(); } catch { }
            return "";
        }

        public static void SaveFolder(string folder)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(ConfigPath, folder); } catch { }
        }

        // Keeps one untouched copy of the very first game file we ever saw, just in case.
        public static void BackupOnce(string gameFile)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string b = Path.Combine(Dir, "original-winDurango-backup.json");
                if (!File.Exists(b)) File.Copy(gameFile, b);
            }
            catch { }
        }
    }

    public class AccountForm : Form
    {
        TextBox tag = new TextBox(), score = new TextBox(), xuid = new TextBox(), secret = new TextBox();
        public Account Result;

        public AccountForm(string title, Account existing)
        {
            Text = title;
            Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 320);

            string[] labels = { "Gamertag", "Gamerscore", "XUID (numbers)", "Secret" };
            TextBox[] boxes = { tag, score, xuid, secret };
            for (int i = 0; i < 4; i++)
            {
                var l = new Label { Text = labels[i], Left = 16, Top = 20 + i * 52, Width = 150, Height = 26, TextAlign = ContentAlignment.MiddleLeft };
                boxes[i].Left = 170; boxes[i].Top = 20 + i * 52; boxes[i].Width = 330;
                Controls.Add(l); Controls.Add(boxes[i]);
            }
            var hint = new Label
            {
                Text = "Tip: the easiest way to add an account is to create the account inside the game, then use \"Save current account\".",
                Left = 16, Top = 228, Width = 490, Height = 40, ForeColor = Color.DimGray
            };
            Controls.Add(hint);
            var ok = new Button { Text = "Save", Left = 300, Top = 280, Width = 95, Height = 32 };
            var cancel = new Button { Text = "Cancel", Left = 405, Top = 280, Width = 95, Height = 32, DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            if (existing != null)
            {
                tag.Text = existing.Gamertag; score.Text = existing.Gamerscore;
                xuid.Text = existing.XuidBase; secret.Text = existing.Secret;
            }
            else score.Text = "0";
        }

        void OnOk(object s, EventArgs e)
        {
            string t = tag.Text.Trim(), sc = score.Text.Trim(), x = xuid.Text.Trim(), se = secret.Text.Trim();
            if (t.Length == 0) { Warn("Please enter a gamertag.", tag); return; }
            long n;
            if (!long.TryParse(sc, out n) || n < 0) { Warn("Gamerscore must be a whole number (for example 0 or 1500).", score); return; }
            if (!Regex.IsMatch(x, "^[0-9]+$")) { Warn("The XUID must contain only digits.", xuid); return; }
            if (!Regex.IsMatch(se, "^[0-9a-fA-F]+$")) { Warn("The secret must contain only 0-9 and a-f.", secret); return; }
            Result = new Account { Gamertag = t, Gamerscore = n.ToString(), XuidBase = x, Secret = se };
            DialogResult = DialogResult.OK;
        }

        void Warn(string msg, Control c)
        {
            MessageBox.Show(this, msg, "Check your entry", MessageBoxButtons.OK, MessageBoxIcon.Information);
            c.Focus();
        }
    }

    public class MainForm : Form
    {
        TextBox folderBox = new TextBox();
        Label folderStatus = new Label();
        Label currentLabel = new Label();
        ListView list = new ListView();
        Button switchBtn = new Button(), saveBtn = new Button(), addBtn = new Button(), editBtn = new Button(), removeBtn = new Button(), newBtn = new Button();
        bool gameBlank;       // game file currently holds no account
        Label status = new Label();
        List<Account> accounts;
        string gameFile;      // full path to winDurango.json, or null
        Account current;      // account currently in the game file, or null

        public MainForm()
        {
            Text = "Dungeon Keys";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Font = new Font("Segoe UI", 10f);
            ClientSize = new Size(680, 560);
            MinimumSize = new Size(640, 540);
            StartPosition = FormStartPosition.CenterScreen;

            // Step 1: game folder
            var g1 = new GroupBox { Text = "Step 1 - Point to game folder location", Left = 12, Top = 8, Width = 656, Height = 112, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var hint = new Label { Text = "Choose the game's \"Mount\" folder.", Left = 14, Top = 26, Width = 620, Height = 22 };
            folderBox.Left = 14; folderBox.Top = 52; folderBox.Width = 520; folderBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            folderBox.Leave += delegate { SetFolder(folderBox.Text); };
            folderBox.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SetFolder(folderBox.Text); } };
            var browse = new Button { Text = "Browse...", Left = 544, Top = 50, Width = 98, Height = 30, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            browse.Click += OnBrowse;
            folderStatus.Left = 14; folderStatus.Top = 84; folderStatus.Width = 628; folderStatus.Height = 22;
            folderStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g1.Controls.AddRange(new Control[] { hint, folderBox, browse, folderStatus });

            // Step 2: accounts
            var g2 = new GroupBox { Text = "Step 2 - Pick an account", Left = 12, Top = 128, Width = 656, Height = 370, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            currentLabel.Left = 14; currentLabel.Top = 26; currentLabel.Width = 628; currentLabel.Height = 24;
            currentLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            list.Left = 14; list.Top = 54; list.Width = 628; list.Height = 224;
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false;
            list.Columns.Add("Account", 300);
            list.Columns.Add("Gamerscore", 120, HorizontalAlignment.Right);
            list.Columns.Add("", 180);
            list.SelectedIndexChanged += delegate { UpdateButtons(); };
            list.DoubleClick += delegate { DoSwitch(); };

            switchBtn.Text = "Switch to selected account";
            switchBtn.Left = 14; switchBtn.Top = 288; switchBtn.Width = 628; switchBtn.Height = 12 + 28;
            switchBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            switchBtn.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            switchBtn.Click += delegate { DoSwitch(); };

            SetupSmall(saveBtn, "Save current account", 14, 160);
            SetupSmall(addBtn, "Import...", 184, 90);
            SetupSmall(editBtn, "Edit...", 284, 90);
            SetupSmall(removeBtn, "Remove", 384, 90);
            SetupSmall(newBtn, "New", 484, 90);
            saveBtn.Click += delegate { DoSaveCurrent(); };
            addBtn.Click += delegate { DoAdd(); };
            editBtn.Click += delegate { DoEdit(); };
            removeBtn.Click += delegate { DoRemove(); };
            newBtn.Click += delegate { DoNew(); };
            g2.Controls.AddRange(new Control[] { currentLabel, list, switchBtn, saveBtn, addBtn, editBtn, removeBtn, newBtn });

            status.Left = 14; status.Top = 506; status.Width = 650; status.Height = 60;
            status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.AddRange(new Control[] { g1, g2, status });

            accounts = Store.Load();
            folderBox.Text = Store.LoadFolder();
            SetFolder(folderBox.Text);
            Activated += delegate { RefreshAll(); };
            Info("Tip: close the game before switching accounts.");
        }

        void SetupSmall(Button b, string text, int left, int width)
        {
            b.Text = text; b.Left = left; b.Width = width; b.Height = 32; b.Top = 330;
            b.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        }

        // ---------- status helpers ----------
        void Info(string m) { status.ForeColor = Color.DimGray; status.Text = m; }
        void Good(string m) { status.ForeColor = Color.ForestGreen; status.Text = m; }
        void Bad(string m) { status.ForeColor = Color.Firebrick; status.Text = m; }

        // ---------- folder ----------
        void OnBrowse(object s, EventArgs e)
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = "Select the game's Mount folder";
                if (Directory.Exists(folderBox.Text)) d.SelectedPath = folderBox.Text;
                if (d.ShowDialog(this) == DialogResult.OK) SetFolder(d.SelectedPath);
            }
        }

        void SetFolder(string folder)
        {
            string f = GameFile.Find(folder);
            if (f != null)
            {
                gameFile = f;
                // Show the Mount folder (the one that contains UserData)
                string root = f;
                for (int i = 0; i < 5; i++) root = Path.GetDirectoryName(root);
                folderBox.Text = root;
                Store.SaveFolder(root);
            }
            else
            {
                gameFile = null;
                if (!string.IsNullOrWhiteSpace(folder)) folderBox.Text = folder;
            }
            RefreshAll();
        }

        // ---------- state ----------
        void RefreshAll()
        {
            current = null;
            gameBlank = false;
            if (gameFile != null)
            {
                try { current = GameFile.Read(gameFile); } catch { }
                if (GameFile.IsBlank(current)) { current = null; gameBlank = true; }
            }

            if (gameFile != null)
            {
                folderStatus.ForeColor = Color.ForestGreen;
                folderStatus.Text = "Game found.";
            }
            else if (string.IsNullOrWhiteSpace(folderBox.Text))
            {
                folderStatus.ForeColor = Color.DimGray;
                folderStatus.Text = "Click \"Browse...\" and select the game's Mount folder.";
            }
            else
            {
                folderStatus.ForeColor = Color.Firebrick;
                folderStatus.Text = "That doesn't look like the game's Mount folder. Please pick the folder that contains \"UserData\".";
            }

            if (gameFile == null) currentLabel.Text = "";
            else if (gameBlank) { currentLabel.ForeColor = Color.Black; currentLabel.Text = "The game has no account right now, it will ask you to create a new one."; }
            else if (current == null) { currentLabel.ForeColor = Color.Firebrick; currentLabel.Text = "Could not read the account that the game is currently using."; }
            else
            {
                bool saved = accounts.Exists(a => a.XuidBase == current.XuidBase);
                currentLabel.ForeColor = Color.Black;
                currentLabel.Text = "The game is currently using:  " + current.Gamertag + (saved ? "" : "   (not saved yet, click \"Save current account\")");
            }

            string selX = SelectedAccount() != null ? SelectedAccount().XuidBase : null;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (Account a in accounts)
            {
                bool active = current != null && a.XuidBase == current.XuidBase;
                var it = new ListViewItem(a.Gamertag);
                it.SubItems.Add(a.Gamerscore);
                it.SubItems.Add(active ? "<- in use by the game" : "");
                it.Tag = a;
                if (active) it.Font = new Font(list.Font, FontStyle.Bold);
                list.Items.Add(it);
                if (selX != null && a.XuidBase == selX) it.Selected = true;
            }
            list.EndUpdate();
            UpdateButtons();
        }

        Account SelectedAccount()
        {
            return list.SelectedItems.Count == 1 ? (Account)list.SelectedItems[0].Tag : null;
        }

        void UpdateButtons()
        {
            Account sel = SelectedAccount();
            bool ok = gameFile != null;
            bool isActive = sel != null && current != null && sel.XuidBase == current.XuidBase;
            switchBtn.Enabled = ok && sel != null && !isActive;
            saveBtn.Enabled = ok && current != null;
            addBtn.Enabled = true;
            editBtn.Enabled = sel != null;
            removeBtn.Enabled = sel != null;
            newBtn.Enabled = ok && !gameBlank;
        }

        // Copies the game's latest data (e.g. new gamerscore) into our saved copy of the same account.
        bool SyncCurrentIntoStore()
        {
            if (current == null) return false;
            Account existing = accounts.Find(a => a.XuidBase == current.XuidBase);
            if (existing == null) return false;
            existing.Gamertag = current.Gamertag; existing.Gamerscore = current.Gamerscore; existing.Secret = current.Secret;
            Store.Save(accounts);
            return true;
        }

        // ---------- actions ----------
        void DoSaveCurrent()
        {
            if (current == null) return;
            Account existing = accounts.Find(a => a.XuidBase == current.XuidBase);
            if (existing != null)
            {
                SyncCurrentIntoStore();
                Good("\"" + current.Gamertag + "\" was already saved - its details have been updated.");
            }
            else
            {
                accounts.Add(current.Clone());
                Store.Save(accounts);
                Good("Saved \"" + current.Gamertag + "\". You can now switch back to it any time.");
            }
            RefreshAll();
        }

        void DoSwitch()
        {
            Account target = SelectedAccount();
            if (gameFile == null || target == null) return;
            if (current != null && current.XuidBase == target.XuidBase) return;
            try
            {
                if (current != null)
                {
                    Account existing = accounts.Find(a => a.XuidBase == current.XuidBase);
                    if (existing == null)
                    {
                        DialogResult r = MessageBox.Show(this,
                            "The game is currently using \"" + current.Gamertag + "\", which isn't saved here yet.\n\nSave it first so you can come back to it later?",
                            "Save current account?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                        if (r == DialogResult.Cancel) return;
                        if (r == DialogResult.Yes) { accounts.Add(current.Clone()); Store.Save(accounts); }
                    }
                    else SyncCurrentIntoStore();
                }
                Store.BackupOnce(gameFile);
                GameFile.Write(gameFile, target);
                Good("Done! The game will now use \"" + target.Gamertag + "\". You can start the game.");
            }
            catch (Exception ex)
            {
                Explain("change the account", ex);
            }
            RefreshAll();
        }

        void DoNew()
        {
            if (gameFile == null) return;
            try
            {
                string who = current != null ? "\"" + current.Gamertag + "\"" : "the current account";
                if (MessageBox.Show(this,
                    "Start fresh with a new account?\n\n" +
                    "The next time you start the game, it will ask you to create a new account.\n\n" +
                    "Nothing is deleted. " + who + " stays on the server, and you can come back to it later if it is saved in this program.",
                    "Start a new account", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

                if (current != null)
                {
                    Account existing = accounts.Find(a => a.XuidBase == current.XuidBase);
                    if (existing == null)
                    {
                        DialogResult r = MessageBox.Show(this,
                            "The account " + who + " isn't saved in this program yet.\n\nSave it now so you can switch back to it later?\n\n" +
                            "If you choose \"No\", you may not be able to get back to it.",
                            "Save current account?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                        if (r == DialogResult.Cancel) return;
                        if (r == DialogResult.Yes) { accounts.Add(current.Clone()); Store.Save(accounts); }
                    }
                    else SyncCurrentIntoStore();
                }
                Store.BackupOnce(gameFile);
                GameFile.Clear(gameFile);
                Good("Ready! Start the game and it will ask you to create a new account.");
            }
            catch (Exception ex) { Explain("start a new account", ex); }
            RefreshAll();
        }

        // Shows a friendly message, plus a short reason line that can be shared if the problem persists.
        void Explain(string action, Exception ex)
        {
            if (ex is UnauthorizedAccessException)
            {
                Bad("Couldn't " + action + " because Windows blocked access to the game's files.");
                if (!IsAdmin())
                {
                    if (MessageBox.Show(this,
                        "Windows is blocking this program from changing the game's files.\n\n" +
                        "This usually happens when the game is installed in a protected place. Giving this program administrator permission normally fixes it.\n\n" +
                        "Restart the program with administrator permission now? (Windows will ask you to confirm.)",
                        "Permission needed", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath);
                            psi.UseShellExecute = true;
                            psi.Verb = "runas";
                            System.Diagnostics.Process.Start(psi);
                            Application.Exit();
                        }
                        catch { Info("The restart was cancelled. Nothing was changed."); }
                    }
                }
                else
                {
                    MessageBox.Show(this,
                        "Windows is still blocking access, even with administrator permission.\n\n" +
                        "Make sure the game is fully closed, and that the game's folder isn't read-only or protected by another program (such as antivirus).\n\n" +
                        "Details: " + ex.Message, "Couldn't " + action, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else if (ex is IOException)
            {
                Bad("Couldn't " + action + " because a file is in use. Please close the game and try again.");
                MessageBox.Show(this, "A file is being used by another program.\n\nPlease close the game and try again.\n\nDetails: " + ex.Message,
                    "Couldn't " + action, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                Bad("Couldn't " + action + ". Something unexpected happened.");
                MessageBox.Show(this, "Something unexpected happened.\n\nDetails: " + ex.Message,
                    "Couldn't " + action, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        static bool IsAdmin()
        {
            try
            {
                return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        void DoAdd()
        {
            using (var f = new AccountForm("Import Existing account", null))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                Account dup = accounts.Find(a => a.XuidBase == f.Result.XuidBase);
                if (dup != null)
                {
                    dup.Gamertag = f.Result.Gamertag; dup.Gamerscore = f.Result.Gamerscore; dup.Secret = f.Result.Secret;
                    Good("That account was already in the list - it has been updated.");
                }
                else
                {
                    accounts.Add(f.Result);
                    Good("Added \"" + f.Result.Gamertag + "\".");
                }
                Store.Save(accounts);
                RefreshAll();
            }
        }

        void DoEdit()
        {
            Account sel = SelectedAccount();
            if (sel == null) return;
            using (var f = new AccountForm("Edit account", sel))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                sel.Gamertag = f.Result.Gamertag; sel.Gamerscore = f.Result.Gamerscore;
                sel.XuidBase = f.Result.XuidBase; sel.Secret = f.Result.Secret;
                Store.Save(accounts);
                Good("Changes saved.");
                RefreshAll();
            }
        }

        void DoRemove()
        {
            Account sel = SelectedAccount();
            if (sel == null) return;
            if (MessageBox.Show(this,
                "Remove \"" + sel.Gamertag + "\" from this list?\n\nThe game itself isn't changed, and the account isn't deleted from the server.",
                "Remove account", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            accounts.Remove(sel);
            Store.Save(accounts);
            Info("Removed \"" + sel.Gamertag + "\" from the list.");
            RefreshAll();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("Something went wrong and the last action could not be completed.\n\n" + e.Exception.Message +
                    "\n\nIf this keeps happening, try moving this program to a folder like Documents and running it again.",
                    "Dungeon Keys", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Application.Run(new MainForm());
        }
    }
}
