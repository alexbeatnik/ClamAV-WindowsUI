// Quarantine: neutralized .quar storage, index, statistics, threat dialog.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClamAVUI
{
    public partial class MainForm : Form
    {
        // ---------- Quarantine and statistics ----------

        // Quarantined files are stored transformed (every byte XOR 0xFF) with a ".quar"
        // extension. The bytes on disk are no longer the malware body, so a resident AV
        // (e.g. Windows Defender) doesn't detect and "steal" files out of our quarantine,
        // and the file can't be launched accidentally. The same XOR restores the original.
        const string QuarExt = ".quar";

        static void XorCopy(string src, string dst)
        {
            using (var fin = File.OpenRead(src))
            using (var fout = new FileStream(dst, FileMode.CreateNew, FileAccess.Write))
            {
                var buf = new byte[81920];
                int n;
                while ((n = fin.Read(buf, 0, buf.Length)) > 0)
                {
                    for (int i = 0; i < n; i++) buf[i] ^= 0xFF;
                    fout.Write(buf, 0, n);
                }
            }
        }

        // A free "name.quar" (or "name(2).quar") slot inside the quarantine folder
        string UniqueQuarPath(string originalName)
        {
            string dest = Path.Combine(quarDir, originalName + QuarExt);
            int i = 1;
            while (File.Exists(dest))
            {
                dest = Path.Combine(quarDir, originalName + "(" + i + ")" + QuarExt);
                i++;
            }
            return dest;
        }

        // Converts any raw file sitting in quarantine into the neutralized .quar form:
        // legacy quarantines (v0.0.2 and older stored files as-is) and files that
        // clamscan --move just dropped there. Index entries follow the rename.
        void NeutralizeQuarantineFolder()
        {
            if (quarDir == null || !Directory.Exists(quarDir)) return;
            var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Directory.GetFiles(quarDir))
            {
                string name = Path.GetFileName(f);
                if (string.Equals(name, "index.txt", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(QuarExt, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    string dest = UniqueQuarPath(name);
                    XorCopy(f, dest);
                    File.Delete(f);
                    renames[name] = Path.GetFileName(dest);
                }
                catch { } // locked/unreadable — retried on the next reload
            }
            if (renames.Count == 0 || !File.Exists(quarIndex)) return;
            try
            {
                var lines = new List<string>();
                foreach (string line in File.ReadAllLines(quarIndex))
                {
                    int p = line.IndexOf('|');
                    string key = p > 0 ? line.Substring(0, p) : null;
                    string renamed;
                    if (key != null && renames.TryGetValue(key, out renamed))
                        lines.Add(renamed + line.Substring(p));
                    else
                        lines.Add(line);
                }
                File.WriteAllLines(quarIndex, lines.ToArray());
            }
            catch { }
        }

        int QuarantineCount()
        {
            if (quarDir == null || !Directory.Exists(quarDir)) return 0;
            int n = 0;
            foreach (string f in Directory.GetFiles(quarDir))
                if (!string.Equals(Path.GetFileName(f), "index.txt", StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        void UpdateStatsUi()
        {
            int q = QuarantineCount();
            statsLabel.Text = string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}\r\n{5}\r\n{6}",
                clamVersion, DbExists() ? DbDateString() : "—",
                lastScanInfo.Length == 0 ? Lang.T("stats.neverScanned") : lastScanInfo,
                totalScans, totalFilesScanned, totalFound, q);
            if (btnQuarantine != null && btnQuarantine.Badge != q)
            {
                btnQuarantine.Badge = q;
                btnQuarantine.Invalidate();
            }
        }

        // Moves a file into quarantine manually (without clamscan --move) in the
        // neutralized .quar form, and writes the index
        bool QuarantineFile(string path)
        {
            try
            {
                string dest = UniqueQuarPath(Path.GetFileName(path));
                XorCopy(path, dest);
                try { File.Delete(path); }
                catch { TryDelete(dest); throw; } // source still there — don't leave two copies
                File.AppendAllText(quarIndex,
                    Path.GetFileName(dest) + "|" + path + "|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\r\n",
                    new UTF8Encoding(false));
                totalMoved++;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Lang.T("msg.quarantineMoveFailed"), ex.Message), Lang.T("quarantine.title"));
                return false;
            }
        }

        void AddExclusion(string path)
        {
            if (!exclusions.Contains(path)) exclusions.Add(path);
        }

        // Asks the user what to do with each detected threat
        void ShowThreatDialog()
        {
            using (var dlg = new Form())
            {
                dlg.Text = Lang.T("threat.title");
                dlg.Size = new Size(760, 420);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = dlg.MaximizeBox = false;
                dlg.BackColor = Theme.Bg;
                dlg.ForeColor = Theme.Text;
                dlg.Font = Font;
                Theme.DarkTitleBar(dlg);

                var list = MakeList();
                list.Columns.Add(Lang.T("col.file"), 400);
                list.Columns.Add(Lang.T("col.threat"), 280);

                foreach (string[] f in foundFiles)
                {
                    if (!File.Exists(f[0])) continue; // already moved or gone
                    var item = new ListViewItem(new string[] { f[0], f[1] });
                    item.Tag = f[0];
                    list.Items.Add(item);
                }
                if (list.Items.Count == 0) return;

                var hint = new Label();
                hint.Dock = DockStyle.Top;
                hint.Height = 30;
                hint.Padding = new Padding(10, 8, 10, 0);
                hint.ForeColor = Theme.Muted;
                hint.Text = Lang.T("threat.hint");

                var buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Height = 50;
                buttons.Padding = new Padding(8);
                buttons.BackColor = Theme.Bg;

                var close = MakeButton(Lang.T("btn.close"), 90, Theme.Card, Theme.Bg, Ico.Close);
                close.DialogResult = DialogResult.Cancel;
                var excl = MakeButton(Lang.T("btn.toExclusions"), 125, Theme.Card, Theme.Bg, Ico.Ban);
                var del = MakeButton(Lang.T("btn.delete"), 100, Theme.Danger, Theme.DangerHot, Ico.Trash);
                var quar = MakeButton(Lang.T("btn.toQuarantine"), 110, Theme.Accent, Theme.AccentHot, Ico.ShieldIcon);

                Func<List<ListViewItem>> picked = delegate
                {
                    var items = new List<ListViewItem>();
                    if (list.SelectedItems.Count > 0)
                        foreach (ListViewItem it in list.SelectedItems) items.Add(it);
                    else
                        foreach (ListViewItem it in list.Items) items.Add(it);
                    return items;
                };
                Action maybeClose = delegate { if (list.Items.Count == 0) dlg.Close(); };

                quar.Click += delegate
                {
                    foreach (var it in picked())
                        if (QuarantineFile((string)it.Tag)) { movedCount++; list.Items.Remove(it); }
                    SaveSettings();
                    UpdateStatsUi();
                    maybeClose();
                };
                del.Click += delegate
                {
                    var items = picked();
                    if (MessageBox.Show(dlg, string.Format(Lang.T("msg.deleteConfirm"), items.Count),
                        Lang.T("title.deletion"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    foreach (var it in items)
                    {
                        try { File.Delete((string)it.Tag); list.Items.Remove(it); }
                        catch (Exception ex) { MessageBox.Show(dlg, ex.Message, Lang.T("title.error")); }
                    }
                    maybeClose();
                };
                excl.Click += delegate
                {
                    foreach (var it in picked())
                    {
                        AddExclusion((string)it.Tag);
                        list.Items.Remove(it);
                    }
                    SaveSettings();
                    statusLabel.Text = string.Format(Lang.T("status.exclusionsCount"), exclusions.Count);
                    maybeClose();
                };

                buttons.Controls.Add(close);
                buttons.Controls.Add(excl);
                buttons.Controls.Add(del);
                buttons.Controls.Add(quar);

                dlg.Controls.Add(list);
                dlg.Controls.Add(hint);
                dlg.Controls.Add(buttons);
                dlg.CancelButton = close;
                dlg.ShowDialog(this);
            }
            UpdateStatsUi();
        }

        Dictionary<string, string[]> ReadQuarIndex()
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(quarIndex)) return map;
            foreach (string line in File.ReadAllLines(quarIndex))
            {
                string[] parts = line.Split('|');
                if (parts.Length >= 3) map[parts[0]] = parts;
            }
            return map;
        }

        void RemoveQuarIndexEntry(string fileName)
        {
            if (!File.Exists(quarIndex)) return;
            var keep = new List<string>();
            foreach (string line in File.ReadAllLines(quarIndex))
                if (!line.StartsWith(fileName + "|", StringComparison.OrdinalIgnoreCase))
                    keep.Add(line);
            File.WriteAllLines(quarIndex, keep.ToArray());
        }



    }
}
