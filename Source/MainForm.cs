using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace RiotWadGui
{
    // ── Entry model dùng trong GUI ──
    class WadEntry
    {
        public string   Hash;           // hex string (tên file trong WAD)
        public int      Offset;
        public int      Size;           // compressed
        public int      SizeUncompressed;
        public byte     CompType;       // 0=Raw 1=GZip 3=Zstd
        public string   Extension;      // phát hiện từ magic bytes
        public byte[]   RawData;        // dữ liệu đã giải nén (load khi mở)
        public bool     Modified;       // đánh dấu đã thay đổi

        public string DisplayName { get { return Hash + Extension; } }
        public string TypeLabel
        {
            get
            {
                switch (CompType)
                {
                    case 0: return "Raw";
                    case 1: return "GZip";
                    case 3: return "Zstd";
                    default: return "#" + CompType;
                }
            }
        }
    }

    // ── Main Form ──
    public class MainForm : Form
    {
        // Controls
        MenuStrip   menuStrip;
        ToolStrip   toolBar;
        SplitContainer splitMain;
        TreeView    treeView;
        ListView    listView;
        StatusStrip statusStrip;
        ToolStripStatusLabel statusLabel;
        ToolStripStatusLabel countLabel;

        // State
        string      currentWadPath;
        byte        wadMajor, wadMinor;
        List<WadEntry> entries = new List<WadEntry>();
        bool        isDirty = false;

        // ── Constructor ──
        public MainForm()
        {
            InitializeComponent();
        }

        void InitializeComponent()
        {
            this.Text = "RiotWadTool GUI - .wad.client Editor";
            this.Size = new Size(1000, 680);
            this.MinimumSize = new Size(700, 450);
            this.StartPosition = FormStartPosition.CenterScreen;

            // ── MenuStrip ──
            menuStrip = new MenuStrip();

            var miFile = new ToolStripMenuItem("&File");
            var miOpen  = new ToolStripMenuItem("&Mở WAD...",   null, OnOpenWad,   Keys.Control | Keys.O);
            var miSave  = new ToolStripMenuItem("&Lưu WAD",     null, OnSaveWad,   Keys.Control | Keys.S);
            var miSaveAs= new ToolStripMenuItem("Lưu &Thành...",null, OnSaveWadAs);
            var miExit  = new ToolStripMenuItem("&Thoát",       null, (s,e)=>Close());
            miFile.DropDownItems.AddRange(new ToolStripItem[]{ miOpen, miSave, miSaveAs, new ToolStripSeparator(), miExit });

            var miEdit = new ToolStripMenuItem("&Chỉnh sửa");
            var miAdd    = new ToolStripMenuItem("&Thêm file...", null, OnAddFiles,  Keys.Control | Keys.A);
            var miDelete = new ToolStripMenuItem("&Xóa mục chọn",null, OnDeleteSelected, Keys.Delete);
            var miRename = new ToolStripMenuItem("&Đổi tên...",  null, OnRenameEntry);
            miEdit.DropDownItems.AddRange(new ToolStripItem[]{ miAdd, miDelete, miRename });

            var miExtract = new ToolStripMenuItem("&Extract");
            var miExtSel  = new ToolStripMenuItem("Extract &Chọn...",  null, OnExtractSelected, Keys.Control | Keys.E);
            var miExtAll  = new ToolStripMenuItem("Extract &Tất cả...",null, OnExtractAll);
            miExtract.DropDownItems.AddRange(new ToolStripItem[]{ miExtSel, miExtAll });

            menuStrip.Items.AddRange(new ToolStripItem[]{ miFile, miEdit, miExtract });
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            // ── ToolBar ──
            toolBar = new ToolStrip();
            toolBar.Items.Add(MakeBtn("📂 Mở",       OnOpenWad));
            toolBar.Items.Add(MakeBtn("💾 Lưu",       OnSaveWad));
            toolBar.Items.Add(new ToolStripSeparator());
            toolBar.Items.Add(MakeBtn("➕ Thêm",      OnAddFiles));
            toolBar.Items.Add(MakeBtn("🗑 Xóa",       OnDeleteSelected));
            toolBar.Items.Add(MakeBtn("✏ Đổi tên",   OnRenameEntry));
            toolBar.Items.Add(new ToolStripSeparator());
            toolBar.Items.Add(MakeBtn("📤 Extract Chọn", OnExtractSelected));
            toolBar.Items.Add(MakeBtn("📦 Extract Tất cả", OnExtractAll));
            this.Controls.Add(toolBar);

            // ── SplitContainer ──
            splitMain = new SplitContainer();
            splitMain.Dock = DockStyle.Fill;

            // ── TreeView (panel trái) ──
            treeView = new TreeView();
            treeView.Dock = DockStyle.Fill;
            treeView.ShowLines = true;
            treeView.ShowPlusMinus = true;
            treeView.FullRowSelect = true;
            treeView.HideSelection = false;
            treeView.AfterSelect += OnTreeSelect;
            treeView.Font = new Font("Consolas", 9f);
            splitMain.Panel1.Controls.Add(treeView);

            // ── ListView (panel phải) ──
            listView = new ListView();
            listView.Dock = DockStyle.Fill;
            listView.View = View.Details;
            listView.FullRowSelect = true;
            listView.GridLines = true;
            listView.MultiSelect = true;
            listView.AllowColumnReorder = true;
            listView.Font = new Font("Consolas", 9f);
            listView.Columns.Add("Tên file",        240);
            listView.Columns.Add("Offset",           90);
            listView.Columns.Add("Compressed",       90);
            listView.Columns.Add("Giải nén",         90);
            listView.Columns.Add("Nén",              70);
            listView.Columns.Add("Trạng thái",       80);
            listView.ContextMenuStrip = BuildContextMenu();
            listView.DoubleClick += OnListViewDoubleClick;
            listView.KeyDown += OnListViewKeyDown;
            splitMain.Panel2.Controls.Add(listView);

            this.Controls.Add(splitMain);

            // ── StatusStrip ──
            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Chưa mở file nào");
            statusLabel.Spring = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            countLabel  = new ToolStripStatusLabel("0 mục");
            statusStrip.Items.AddRange(new ToolStripItem[]{ statusLabel, countLabel });
            this.Controls.Add(statusStrip);

            // Layout order
            this.Controls.SetChildIndex(menuStrip,   0);
            this.Controls.SetChildIndex(toolBar,     1);
            this.Controls.SetChildIndex(splitMain,   2);
            this.Controls.SetChildIndex(statusStrip, 3);

            this.FormClosing += OnFormClosing;
            this.Load += (s, e) => {
                splitMain.Panel1MinSize = 150;
                splitMain.Panel2MinSize = 200;
                splitMain.SplitterDistance = 220;
            };
        }

        ToolStripButton MakeBtn(string text, EventHandler handler)
        {
            var btn = new ToolStripButton(text);
            btn.Click += handler;
            return btn;
        }

        ContextMenuStrip BuildContextMenu()
        {
            var cms = new ContextMenuStrip();
            cms.Items.Add("Thêm file...",        null, OnAddFiles);
            cms.Items.Add("Xóa",                 null, OnDeleteSelected);
            cms.Items.Add("Đổi tên...",          null, OnRenameEntry);
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add("Extract Chọn...",     null, OnExtractSelected);
            cms.Items.Add("Extract Tất cả...",   null, OnExtractAll);
            return cms;
        }

        // ════════════════════════════════════════════════════════
        //  OPEN WAD
        // ════════════════════════════════════════════════════════

        void OnOpenWad(object sender, EventArgs e)
        {
            if (isDirty && !ConfirmDiscard()) return;

            using (var dlg = new OpenFileDialog())
            {
                dlg.Title  = "Mở file WAD";
                dlg.Filter = "WAD Client (*.wad.client)|*.wad.client|WAD files (*.wad)|*.wad|All files (*.*)|*.*";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                LoadWad(dlg.FileName);
            }
        }

        void LoadWad(string path)
        {
            try
            {
                entries.Clear();
                treeView.Nodes.Clear();
                listView.Items.Clear();

                using (BinaryReader br = new BinaryReader(File.OpenRead(path)))
                {
                    // ── Header ──
                    byte[] magic = br.ReadBytes(2);
                    if (magic[0] != 'R' || magic[1] != 'W')
                        throw new Exception("File không phải định dạng WAD hợp lệ (magic != 'RW')");

                    wadMajor = br.ReadByte();
                    wadMinor = br.ReadByte();
                    int count = 0;

                    if (wadMajor == 1)
                    {
                        br.ReadInt16(); // entryHdrOffset
                        br.ReadInt16(); // cellSize
                        count = br.ReadInt32();
                    }
                    else if (wadMajor == 2)
                    {
                        br.ReadByte();          // ECLen
                        br.ReadBytes(83);       // EC
                        br.ReadBytes(8);        // checksum
                        br.ReadInt16();         // entryHdrOffset
                        br.ReadInt16();         // cellSize
                        count = br.ReadInt32();
                    }
                    else if (wadMajor == 3)
                    {
                        br.ReadBytes(256);      // ECDSA
                        br.ReadBytes(8);        // checksum
                        count = br.ReadInt32();
                    }
                    else
                    {
                        throw new Exception("WAD version " + wadMajor + " chưa được hỗ trợ");
                    }

                    // ── Đọc entries ──
                    for (int i = 0; i < count; i++)
                    {
                        WadEntry we = new WadEntry();
                        byte[] ck = br.ReadBytes(8);
                        we.Hash = BitConverter.ToString(ck).Replace("-","").ToLower();
                        we.Offset           = br.ReadInt32();
                        we.SizeUncompressed = br.ReadInt32();
                        we.Size             = br.ReadInt32();

                        if (wadMajor == 1)
                        {
                            we.CompType = (byte)br.ReadInt32();
                        }
                        else // v2, v3
                        {
                            we.CompType = br.ReadByte();
                            br.ReadByte(); br.ReadByte(); br.ReadByte(); // unk
                            br.ReadInt64(); // sha256 partial
                        }
                        entries.Add(we);
                    }

                    // ── Giải nén và detect extension ──
                    for (int i = 0; i < entries.Count; i++)
                    {
                        WadEntry we = entries[i];
                        try
                        {
                            br.BaseStream.Seek(we.Offset, SeekOrigin.Begin);
                            byte[] compData = br.ReadBytes(we.Size);
                            we.RawData  = Decompress(compData, we.CompType, we.SizeUncompressed);
                            we.Extension = MagicDetect(we.RawData);
                        }
                        catch
                        {
                            we.RawData   = new byte[0];
                            we.Extension = ".bin";
                        }
                    }
                }

                currentWadPath = path;
                isDirty = false;

                RebuildTree();
                SetStatus(string.Format("Đã mở: {0}  |  WAD v{1}.{2}", Path.GetFileName(path), wadMajor, wadMinor));
                this.Text = "RiotWadGui - " + Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi mở file:\n" + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ════════════════════════════════════════════════════════
        //  SAVE WAD
        // ════════════════════════════════════════════════════════

        void OnSaveWad(object sender, EventArgs e)
        {
            if (currentWadPath == null) { OnSaveWadAs(sender, e); return; }
            SaveWad(currentWadPath);
        }

        void OnSaveWadAs(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title  = "Lưu WAD";
                dlg.Filter = "WAD Client (*.wad.client)|*.wad.client|WAD (*.wad)|*.wad|All files (*.*)|*.*";
                dlg.FileName = currentWadPath != null ? Path.GetFileName(currentWadPath) : "output.wad.client";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                SaveWad(dlg.FileName);
            }
        }

        void SaveWad(string path)
        {
            if (entries.Count == 0) { MessageBox.Show("Không có mục nào để lưu."); return; }
            try
            {
                // ── Nén lại tất cả entries theo version ──
                List<byte[]> compressedList = new List<byte[]>();
                List<byte>   typeList       = new List<byte>();

                foreach (WadEntry we in entries)
                {
                    byte[] raw  = we.RawData ?? new byte[0];
                    byte[] comp = null;
                    byte   ct   = 0;

                    if (wadMajor == 3)
                    {
                        // Thử GZip (Zstd cần ZstdNet.dll, không build sẵn)
                        comp = TryGZip(raw);
                        if (comp != null && comp.Length < raw.Length) { ct = 1; }
                        else { comp = raw; ct = 0; }
                    }
                    else
                    {
                        comp = TryGZip(raw);
                        if (comp != null && comp.Length < raw.Length) { ct = 1; }
                        else { comp = raw; ct = 0; }
                    }

                    compressedList.Add(comp);
                    typeList.Add(ct);
                }

                // ── Tính kích thước header ──
                int entrySize  = (wadMajor == 1) ? 24 : 32;
                int headerSize;
                if      (wadMajor == 1) headerSize = 12  + entries.Count * entrySize;
                else if (wadMajor == 2) headerSize = 104 + entries.Count * entrySize;
                else                    headerSize = 272 + entries.Count * entrySize;

                // ── Tính offset ──
                int[] offsets = new int[entries.Count];
                int cur = headerSize;
                for (int i = 0; i < entries.Count; i++)
                {
                    offsets[i] = cur;
                    cur += compressedList[i].Length;
                }

                using (BinaryWriter bw = new BinaryWriter(File.Create(path)))
                {
                    // Header magic
                    bw.Write((byte)'R');
                    bw.Write((byte)'W');
                    bw.Write(wadMajor);
                    bw.Write(wadMinor);

                    if (wadMajor == 1)
                    {
                        bw.Write((short)(12));          // entryHdrOffset
                        bw.Write((short)24);            // cellSize
                        bw.Write(entries.Count);
                    }
                    else if (wadMajor == 2)
                    {
                        bw.Write((byte)0);              // ECLen = 0
                        bw.Write(new byte[83]);         // EC padding
                        bw.Write(new byte[8]);          // checksum
                        bw.Write((short)(104));
                        bw.Write((short)32);
                        bw.Write(entries.Count);
                    }
                    else // v3
                    {
                        bw.Write(new byte[256]);        // ECDSA
                        bw.Write(new byte[8]);          // checksum
                        bw.Write(entries.Count);
                    }

                    // Entry table
                    for (int i = 0; i < entries.Count; i++)
                    {
                        WadEntry we = entries[i];
                        byte[] ckBytes = HexToBytes(we.Hash);
                        bw.Write(ckBytes, 0, 8);
                        bw.Write(offsets[i]);
                        bw.Write(we.RawData != null ? we.RawData.Length : 0);
                        bw.Write(compressedList[i].Length);

                        if (wadMajor == 1)
                        {
                            bw.Write((int)typeList[i]);
                        }
                        else
                        {
                            bw.Write(typeList[i]);
                            bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
                            // sha256 partial
                            if (we.RawData != null && we.RawData.Length > 0)
                            {
                                byte[] sha = SHA256.Create().ComputeHash(we.RawData);
                                bw.Write(BitConverter.ToInt64(sha, 0));
                            }
                            else bw.Write((long)0);
                        }
                    }

                    // Data blocks
                    for (int i = 0; i < entries.Count; i++)
                        bw.Write(compressedList[i]);
                }

                currentWadPath = path;
                isDirty = false;
                SetStatus("Đã lưu: " + Path.GetFileName(path));
                this.Text = "RiotWadGui - " + Path.GetFileName(path);
                RefreshListView();
                MessageBox.Show("Lưu thành công!\n" + path, "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi lưu file:\n" + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ════════════════════════════════════════════════════════
        //  ADD FILES
        // ════════════════════════════════════════════════════════

        void OnAddFiles(object sender, EventArgs e)
        {
            if (currentWadPath == null && entries.Count == 0)
            {
                // Hỏi version nếu chưa mở file
                var vForm = new VersionPickForm();
                if (vForm.ShowDialog() != DialogResult.OK) return;
                wadMajor = vForm.SelectedVersion;
                wadMinor = 0;
            }

            using (var dlg = new OpenFileDialog())
            {
                dlg.Title      = "Thêm file vào WAD";
                dlg.Multiselect = true;
                dlg.Filter     = "Tất cả file (*.*)|*.*";
                if (dlg.ShowDialog() != DialogResult.OK) return;

                int added = 0;
                foreach (string f in dlg.FileNames)
                {
                    byte[] data = File.ReadAllBytes(f);
                    string relName = Path.GetFileName(f).ToLowerInvariant().Replace('\\', '/');
                    ulong  hash   = XxHash64.Hash(relName);
                    string hexHash = hash.ToString("x16");

                    // Kiểm tra trùng hash
                    bool dup = false;
                    foreach (WadEntry ex2 in entries)
                        if (ex2.Hash == hexHash) { dup = true; break; }
                    if (dup)
                    {
                        if (MessageBox.Show(
                            string.Format("Hash '{0}' đã tồn tại.\nGhi đè?", hexHash),
                            "Trùng", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                            continue;
                        // Ghi đè
                        foreach (WadEntry ex2 in entries)
                            if (ex2.Hash == hexHash)
                            {
                                ex2.RawData   = data;
                                ex2.Extension = MagicDetect(data);
                                ex2.Modified  = true;
                                break;
                            }
                        added++;
                        continue;
                    }

                    WadEntry we = new WadEntry();
                    we.Hash             = hexHash;
                    we.RawData          = data;
                    we.SizeUncompressed = data.Length;
                    we.Size             = data.Length;
                    we.CompType         = 0;
                    we.Extension        = MagicDetect(data);
                    we.Modified         = true;
                    entries.Add(we);
                    added++;
                }

                if (added > 0)
                {
                    isDirty = true;
                    RebuildTree();
                    SetStatus(string.Format("Đã thêm {0} file", added));
                }
            }
        }

        // ════════════════════════════════════════════════════════
        //  DELETE
        // ════════════════════════════════════════════════════════

        void OnDeleteSelected(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 0) { MessageBox.Show("Chưa chọn mục nào."); return; }

            if (MessageBox.Show(
                string.Format("Xóa {0} mục đã chọn?", listView.SelectedItems.Count),
                "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            List<WadEntry> toRemove = new List<WadEntry>();
            foreach (ListViewItem lvi in listView.SelectedItems)
                toRemove.Add((WadEntry)lvi.Tag);

            foreach (WadEntry we in toRemove)
                entries.Remove(we);

            isDirty = true;
            RebuildTree();
            SetStatus(string.Format("Đã xóa {0} mục", toRemove.Count));
        }

        // ════════════════════════════════════════════════════════
        //  RENAME ENTRY
        // ════════════════════════════════════════════════════════

        void OnRenameEntry(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count != 1)
            {
                MessageBox.Show("Chọn đúng 1 mục để đổi tên."); return;
            }
            WadEntry we = (WadEntry)listView.SelectedItems[0].Tag;

            string input = InputBox("Đổi tên", "Nhập đường dẫn nội bộ mới (VD: assets/characters/annie.png):", we.Hash + we.Extension);
            if (string.IsNullOrEmpty(input)) return;

            // Tính lại hash từ tên mới
            string newPath = input.ToLowerInvariant().Replace('\\', '/');
            ulong  hash    = XxHash64.Hash(newPath);
            string newHex  = hash.ToString("x16");

            // Lấy extension từ tên mới
            string ext = Path.GetExtension(newPath);
            if (string.IsNullOrEmpty(ext)) ext = we.Extension;

            we.Hash      = newHex;
            we.Extension = ext;
            we.Modified  = true;
            isDirty = true;

            RebuildTree();
            SetStatus("Đã đổi tên → " + newHex + ext);
        }

        // ════════════════════════════════════════════════════════
        //  EXTRACT
        // ════════════════════════════════════════════════════════

        void OnExtractSelected(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 0) { MessageBox.Show("Chưa chọn mục nào."); return; }

            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Chọn thư mục đích";
                if (dlg.ShowDialog() != DialogResult.OK) return;

                int ok = 0, fail = 0;
                foreach (ListViewItem lvi in listView.SelectedItems)
                {
                    WadEntry we = (WadEntry)lvi.Tag;
                    try
                    {
                        string outPath = Path.Combine(dlg.SelectedPath, we.DisplayName);
                        File.WriteAllBytes(outPath, we.RawData ?? new byte[0]);
                        ok++;
                    }
                    catch { fail++; }
                }
                MessageBox.Show(string.Format("Extract xong: {0} OK, {1} lỗi", ok, fail), "Kết quả", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void OnExtractAll(object sender, EventArgs e)
        {
            if (entries.Count == 0) { MessageBox.Show("Không có mục nào."); return; }

            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Chọn thư mục đích";
                if (dlg.ShowDialog() != DialogResult.OK) return;

                int ok = 0, fail = 0;
                foreach (WadEntry we in entries)
                {
                    try
                    {
                        File.WriteAllBytes(Path.Combine(dlg.SelectedPath, we.DisplayName), we.RawData ?? new byte[0]);
                        ok++;
                    }
                    catch { fail++; }
                }
                MessageBox.Show(string.Format("Extract xong: {0} OK, {1} lỗi", ok, fail), "Kết quả", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ════════════════════════════════════════════════════════
        //  TREEVIEW
        // ════════════════════════════════════════════════════════

        void RebuildTree()
        {
            treeView.BeginUpdate();
            treeView.Nodes.Clear();

            // Group theo extension
            Dictionary<string, List<WadEntry>> groups = new Dictionary<string, List<WadEntry>>();
            foreach (WadEntry we in entries)
            {
                string ext = we.Extension ?? ".bin";
                if (!groups.ContainsKey(ext)) groups[ext] = new List<WadEntry>();
                groups[ext].Add(we);
            }

            // Root node
            string rootLabel = currentWadPath != null
                ? Path.GetFileName(currentWadPath)
                : "(Chưa lưu)";
            TreeNode root = new TreeNode(rootLabel + "  [" + entries.Count + " mục]");
            root.Tag = "root";

            foreach (var kv in groups)
            {
                TreeNode grpNode = new TreeNode(kv.Key + "  [" + kv.Value.Count + "]");
                grpNode.Tag = kv.Key;
                foreach (WadEntry we in kv.Value)
                {
                    TreeNode n = new TreeNode(we.DisplayName);
                    n.Tag = we;
                    if (we.Modified) n.ForeColor = Color.DodgerBlue;
                    grpNode.Nodes.Add(n);
                }
                root.Nodes.Add(grpNode);
            }

            treeView.Nodes.Add(root);
            root.Expand();

            treeView.EndUpdate();
            RefreshListView();
            countLabel.Text = entries.Count + " mục";
        }

        void OnTreeSelect(object sender, TreeViewEventArgs e)
        {
            RefreshListView(e.Node);
        }

        void RefreshListView(TreeNode node = null)
        {
            listView.BeginUpdate();
            listView.Items.Clear();

            List<WadEntry> toShow = new List<WadEntry>();

            if (node == null || node.Tag as string == "root" || node.Tag == null)
            {
                toShow = entries;
            }
            else if (node.Tag is string)
            {
                // group node
                string ext = (string)node.Tag;
                foreach (WadEntry we in entries)
                    if (we.Extension == ext) toShow.Add(we);
            }
            else if (node.Tag is WadEntry)
            {
                toShow.Add((WadEntry)node.Tag);
            }

            foreach (WadEntry we in toShow)
            {
                ListViewItem lvi = new ListViewItem(we.DisplayName);
                lvi.SubItems.Add(we.Offset.ToString("X8"));
                lvi.SubItems.Add(we.Size.ToString("N0"));
                lvi.SubItems.Add(we.SizeUncompressed == 0 && we.RawData != null ? we.RawData.Length.ToString("N0") : we.SizeUncompressed.ToString("N0"));
                lvi.SubItems.Add(we.TypeLabel);
                lvi.SubItems.Add(we.Modified ? "✏ Sửa" : "");
                lvi.Tag = we;
                if (we.Modified) lvi.ForeColor = Color.DodgerBlue;
                listView.Items.Add(lvi);
            }

            listView.EndUpdate();
        }

        // ════════════════════════════════════════════════════════
        //  EVENTS
        // ════════════════════════════════════════════════════════

        void OnListViewDoubleClick(object sender, EventArgs e)
        {
            // Mở file tạm để xem
            if (listView.SelectedItems.Count != 1) return;
            WadEntry we = (WadEntry)listView.SelectedItems[0].Tag;
            if (we.RawData == null || we.RawData.Length == 0) { MessageBox.Show("Không có dữ liệu."); return; }

            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), we.DisplayName);
                File.WriteAllBytes(tmp, we.RawData);
                System.Diagnostics.Process.Start(tmp);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể mở: " + ex.Message);
            }
        }

        void OnListViewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) OnDeleteSelected(sender, e);
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (isDirty && !ConfirmDiscard()) e.Cancel = true;
        }

        // ════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════

        bool ConfirmDiscard()
        {
            var r = MessageBox.Show("Có thay đổi chưa lưu. Tiếp tục sẽ mất dữ liệu.\nVẫn tiếp tục?",
                "Cảnh báo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            return r == DialogResult.Yes;
        }

        void SetStatus(string msg)
        {
            statusLabel.Text = msg;
        }

        static byte[] Decompress(byte[] data, byte compType, int sizeHint)
        {
            if (compType == 0) return data;  // Raw
            if (compType == 1)               // GZip
            {
                using (MemoryStream ms = new MemoryStream(data))
                using (GZipStream gz = new GZipStream(ms, CompressionMode.Decompress))
                using (MemoryStream out2 = new MemoryStream())
                {
                    gz.CopyTo(out2);
                    return out2.ToArray();
                }
            }
            if (compType == 3)               // Zstd — dùng reflection nếu có ZstdNet.dll
            {
                Type streamType = Type.GetType("ZstdNet.DecompressionStream, ZstdNet");
                if (streamType != null)
                {
                    using (MemoryStream inMs = new MemoryStream(data))
                    using (Stream zs = (Stream)Activator.CreateInstance(streamType, new object[] { inMs }))
                    using (MemoryStream out2 = new MemoryStream())
                    { zs.CopyTo(out2); return out2.ToArray(); }
                }
                throw new NotSupportedException("Zstd cần ZstdNet.dll bên cạnh exe");
            }
            return data;
        }

        static byte[] TryGZip(byte[] raw)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    using (GZipStream gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                        gz.Write(raw, 0, raw.Length);
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        static string MagicDetect(byte[] h)
        {
            if (h == null || h.Length < 2) return ".bin";
            if (h[0] == 0xFF && h[1] == 0xD8)                              return ".jpg";
            if (h[0] == 0x50 && h[1] == 0x4B)                              return ".zip";
            if (h[0] == 0x7B)                                               return ".json";
            if (h[0] == 0x3C)                                               return ".xml";
            if (h.Length < 4) return ".bin";
            byte a=h[0], b=h[1], c=h[2], d=h[3];
            if (a==0x89 && b==0x50 && c==0x4E && d==0x47)                  return ".png";
            if (a==0x47 && b==0x49 && c==0x46)                             return ".gif";
            if (a==0x52 && b==0x49 && c==0x46 && d==0x46)                  return ".webp";
            if (a==0x44 && b==0x44 && c==0x53)                             return ".dds";
            if (a==0x28 && b==0xB5 && c==0x2F && d==0xFD)                  return ".zst";
            if (a==0x4C && b==0x75 && c==0x61)                             return ".luac";
            if (a==0x52 && b==0x53 && c==0x54)                             return ".rst";
            if (a==0xEF && b==0xBB && c==0xBF)                             return ".txt";
            return ".bin";
        }

        static byte[] HexToBytes(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        static string InputBox(string title, string prompt, string defaultVal = "")
        {
            Form f = new Form { Text = title, Size = new Size(500, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
            Label lbl = new Label { Text = prompt, Left = 10, Top = 10, Width = 460, AutoSize = false };
            TextBox tb = new TextBox { Text = defaultVal, Left = 10, Top = 35, Width = 460 };
            Button ok = new Button { Text = "OK", Left = 290, Top = 65, Width = 80, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "Hủy", Left = 390, Top = 65, Width = 80, DialogResult = DialogResult.Cancel };
            f.Controls.AddRange(new Control[]{ lbl, tb, ok, cancel });
            f.AcceptButton = ok; f.CancelButton = cancel;
            return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
        }
    }

    // ════════════════════════════════════════════════════════
    //  Form chọn WAD version khi tạo mới
    // ════════════════════════════════════════════════════════
    class VersionPickForm : Form
    {
        public byte SelectedVersion = 3;
        public VersionPickForm()
        {
            this.Text = "Chọn WAD Version";
            this.Size = new Size(280, 160);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            Label lbl = new Label { Text = "Chọn version WAD để tạo mới:", Left=10, Top=10, Width=240 };
            RadioButton r1 = new RadioButton { Text = "WAD v1 (GZip)", Left=10, Top=35, Width=200 };
            RadioButton r2 = new RadioButton { Text = "WAD v2 (GZip)", Left=10, Top=58, Width=200 };
            RadioButton r3 = new RadioButton { Text = "WAD v3 (Zstd/GZip)", Left=10, Top=81, Width=200, Checked=true };
            Button ok = new Button { Text = "OK", Left=100, Top=108, Width=80, DialogResult=DialogResult.OK };
            ok.Click += (s,e) =>
            {
                if (r1.Checked) SelectedVersion = 1;
                else if (r2.Checked) SelectedVersion = 2;
                else SelectedVersion = 3;
            };
            this.Controls.AddRange(new Control[]{ lbl, r1, r2, r3, ok });
            this.AcceptButton = ok;
        }
    }

    // ════════════════════════════════════════════════════════
    //  xxHash64 (copy từ console tool)
    // ════════════════════════════════════════════════════════
    static class XxHash64
    {
        const ulong P1=11400714785074694791UL, P2=14029467366897019727UL,
                    P3=1609587929392839161UL,  P4=9650029242287828579UL,
                    P5=2870177450012600261UL;
        public static ulong Hash(string text) { return Hash(Encoding.ASCII.GetBytes(text.ToLowerInvariant().Replace('\\','/'))); }
        public static ulong Hash(byte[] data)
        {
            int len=data.Length, pos=0; ulong h64;
            if (len>=32)
            {
                ulong v1=unchecked(P1+P2), v2=P2, v3=0, v4=unchecked(0UL-P1);
                do { v1=R(v1,ToU64(data,pos));pos+=8; v2=R(v2,ToU64(data,pos));pos+=8; v3=R(v3,ToU64(data,pos));pos+=8; v4=R(v4,ToU64(data,pos));pos+=8; } while (pos<=len-32);
                h64=RL(v1,1)+RL(v2,7)+RL(v3,12)+RL(v4,18);
                h64=M(h64,v1); h64=M(h64,v2); h64=M(h64,v3); h64=M(h64,v4);
            } else h64=P5;
            h64+=(ulong)len;
            while(pos<=len-8){h64^=R(0,ToU64(data,pos));pos+=8;h64=RL(h64,27)*P1+P4;}
            while(pos<=len-4){h64^=BitConverter.ToUInt32(data,pos)*P1;pos+=4;h64=RL(h64,23)*P2+P3;}
            while(pos<len){h64^=data[pos++]*P5;h64=RL(h64,11)*P1;}
            h64^=h64>>33;h64*=P2;h64^=h64>>29;h64*=P3;h64^=h64>>32;
            return h64;
        }
        static ulong ToU64(byte[] d,int p){ return BitConverter.ToUInt64(d,p); }
        static ulong R(ulong a,ulong v){ return RL(a+v*P2,31)*P1; }
        static ulong M(ulong h,ulong v){ return (h^R(0,v))*P1+P4; }
        static ulong RL(ulong v,int r){ return (v<<r)|(v>>(64-r)); }
    }

    // ════════════════════════════════════════════════════════
    //  Entry point
    // ════════════════════════════════════════════════════════
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
