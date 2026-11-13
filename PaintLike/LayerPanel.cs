using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PaintLike
{
    /// <summary>
    /// Bảng điều khiển Layer & Bộ lọc ảnh OpenCV theo chuẩn Studio Dark UI
    /// Hỗ trợ kéo thả di chuyển, thay đổi kích thước linh hoạt (Responsive Layout),
    /// xem trước thumbnail layer và áp dụng bộ lọc nhanh.
    /// </summary>
    public partial class LayerPanel : UserControl
    {
        private List<Layer> layers;
        private int currentLayerIndex = 0;
        private FlowLayoutPanel? flowLayerItems;
        private Panel? headerPanel;
        private Label? lblLayerTitle;
        private Button? btnClosePanel;
        private Panel? panelButtons;
        private Panel? panelFilters;
        private ComboBox? cmbFilters;
        private Button? btnApplyFilter;
        private TextBox? txtFilterStatus;

        private int canvasWidth = 800;
        private int canvasHeight = 600;

        // Dragging fields
        private bool isDragging = false;
        private Point dragStartPosition = Point.Empty;

        // Resizing fields
        private bool isResizing = false;
        private Point resizeStartPosition = Point.Empty;
        private Size resizeStartSize = Size.Empty;
        private const int ResizeHandleSize = 14;

        public event EventHandler? LayerSelectionChanged;
        public event EventHandler? LayersChanged;

        public LayerPanel(List<Layer> layerList)
        {
            InitializeComponent();
            this.layers = layerList;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = UITheme.BgPanel;
            this.BorderStyle = BorderStyle.None;
            this.Size = new Size(380, 540);
            this.Anchor = AnchorStyles.None;

            InitializeControls();
        }

        private void InitializeControls()
        {
            // 1. Thanh tiêu đề kéo thả (Header Bar)
            headerPanel = new Panel
            {
                Location = new Point(2, 2),
                Size = new Size(this.Width - 4, 34),
                BackColor = UITheme.BgPanelHeader,
                Cursor = Cursors.SizeAll
            };
            headerPanel.MouseDown += (s, e) => LblLayerTitle_MouseDown(s, e);
            headerPanel.MouseMove += (s, e) => LblLayerTitle_MouseMove(s, e);
            headerPanel.MouseUp += (s, e) => LblLayerTitle_MouseUp(s, e);

            lblLayerTitle = new Label
            {
                Text = "  LAYERS & BỘ LỌC ẢNH",
                Font = UITheme.FontSubtitle,
                Size = new Size(this.Width - 44, 34),
                Location = new Point(6, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                ForeColor = UITheme.IconSelected,
                Cursor = Cursors.SizeAll
            };
            lblLayerTitle.MouseDown += (s, e) => LblLayerTitle_MouseDown(s, e);
            lblLayerTitle.MouseMove += (s, e) => LblLayerTitle_MouseMove(s, e);
            lblLayerTitle.MouseUp += (s, e) => LblLayerTitle_MouseUp(s, e);
            headerPanel.Controls.Add(lblLayerTitle);

            btnClosePanel = new Button
            {
                Size = new Size(28, 26),
                Location = new Point(headerPanel.Width - 32, 4)
            };
            UITheme.StyleButton(btnClosePanel, IconType.None, "✕", ButtonVariant.Ghost);
            btnClosePanel.Click += (s, e) => this.Visible = false;
            headerPanel.Controls.Add(btnClosePanel);

            this.Controls.Add(headerPanel);

            // 2. Danh sách Layer (FlowLayoutPanel)
            flowLayerItems = new FlowLayoutPanel
            {
                Name = "flowLayerItems",
                Location = new Point(10, 42),
                Size = new Size(this.Width - 20, 175),
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6, 6, 6, 2)
            };
            this.Controls.Add(flowLayerItems);

            // 3. Cụm nút thao tác Layer
            panelButtons = new Panel
            {
                Location = new Point(10, 223),
                Size = new Size(this.Width - 20, 66),
                BackColor = Color.Transparent
            };
            this.Controls.Add(panelButtons);

            // Hàng 1: Thêm, Nhân bản, Lên, Xuống
            Button btnAddLayer = new Button { Name = "btnAddLayer", Location = new Point(0, 0), Size = new Size(96, 30) };
            UITheme.StyleButton(btnAddLayer, IconType.Plus, "Thêm", ButtonVariant.Primary);
            btnAddLayer.Click += (s, e) => BtnAddLayer_Click(s, e);
            panelButtons.Controls.Add(btnAddLayer);

            Button btnDuplicate = new Button { Name = "btnDuplicate", Location = new Point(102, 0), Size = new Size(96, 30) };
            UITheme.StyleButton(btnDuplicate, IconType.Duplicate, "Nhân bản", ButtonVariant.Default);
            btnDuplicate.Click += (s, e) => BtnDuplicate_Click(s, e);
            panelButtons.Controls.Add(btnDuplicate);

            Button btnMoveUp = new Button { Name = "btnMoveUp", Location = new Point(204, 0), Size = new Size(75, 30) };
            UITheme.StyleButton(btnMoveUp, IconType.ArrowUp, "Lên", ButtonVariant.Default);
            btnMoveUp.Click += (s, e) => BtnMoveUp_Click(s, e);
            panelButtons.Controls.Add(btnMoveUp);

            Button btnMoveDown = new Button { Name = "btnMoveDown", Location = new Point(285, 0), Size = new Size(75, 30) };
            UITheme.StyleButton(btnMoveDown, IconType.ArrowDown, "Xuống", ButtonVariant.Default);
            btnMoveDown.Click += (s, e) => BtnMoveDown_Click(s, e);
            panelButtons.Controls.Add(btnMoveDown);

            // Hàng 2: Đổi tên, Độ mờ, Xóa
            Button btnRename = new Button { Name = "btnRename", Location = new Point(0, 35), Size = new Size(116, 30) };
            UITheme.StyleButton(btnRename, IconType.Rename, "Đổi tên", ButtonVariant.Default);
            btnRename.Click += (s, e) => BtnRename_Click(s, e);
            panelButtons.Controls.Add(btnRename);

            Button btnOpacity = new Button { Name = "btnOpacity", Location = new Point(122, 35), Size = new Size(116, 30) };
            UITheme.StyleButton(btnOpacity, IconType.Opacity, "Độ mờ", ButtonVariant.Default);
            btnOpacity.Click += (s, e) => BtnOpacity_Click(s, e);
            panelButtons.Controls.Add(btnOpacity);

            Button btnDeleteLayer = new Button { Name = "btnDeleteLayer", Location = new Point(244, 35), Size = new Size(116, 30) };
            UITheme.StyleButton(btnDeleteLayer, IconType.Trash, "Xóa Layer", ButtonVariant.Danger);
            btnDeleteLayer.Click += (s, e) => BtnDeleteLayer_Click(s, e);
            panelButtons.Controls.Add(btnDeleteLayer);

            // 4. Bảng Bộ Lọc Ảnh OpenCV (Filters Section)
            panelFilters = new Panel
            {
                Location = new Point(10, 296),
                Size = new Size(this.Width - 20, 232),
                BackColor = Color.White
            };
            panelFilters.Paint += (s, e) =>
            {
                using (Pen borderPen = new Pen(UITheme.BorderSubtle, 1f))
                {
                    e.Graphics.DrawRectangle(borderPen, 0, 0, panelFilters.Width - 1, panelFilters.Height - 1);
                }
            };
            this.Controls.Add(panelFilters);

            Label lblFiltersTitle = new Label
            {
                Text = "BỘ LỌC XỬ LÝ ẢNH (OPENCV)",
                Font = UITheme.FontGroupHeader,
                Size = new Size(340, 18),
                Location = new Point(8, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UITheme.AccentPrimary,
                BackColor = Color.Transparent
            };
            panelFilters.Controls.Add(lblFiltersTitle);

            cmbFilters = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = UITheme.BgInput,
                ForeColor = UITheme.TextPrimary,
                Font = UITheme.FontBody,
                Location = new Point(8, 27),
                Size = new Size(252, 26)
            };
            cmbFilters.Items.AddRange(new object[]
            {
                "-- Chọn bộ lọc chuyên sâu --",
                "Khử răng cưa (Simple Bilateral)",
                "Khử răng cưa (Advanced Multi-Pass)",
                "Khử răng cưa Đường cong (Curves)",
                "Độ sáng (Brightness)",
                "Độ tương phản (Contrast)",
                "Làm mờ (Box Blur)",
                "Làm mờ mịn (Gaussian Blur)",
                "Trắng đen (Grayscale)",
                "Phát hiện cạnh (Edge Detection)",
                "Làm sắc nét (Sharpen)",
                "Đảo ngược màu (Invert Colors)",
                "Màu cổ điển (Sepia)",
                "Độ bão hòa màu (Saturation)"
            });
            cmbFilters.SelectedIndex = 0;
            panelFilters.Controls.Add(cmbFilters);

            btnApplyFilter = new Button
            {
                Location = new Point(266, 26),
                Size = new Size(86, 27)
            };
            UITheme.StyleButton(btnApplyFilter, IconType.Filter, "Áp dụng", ButtonVariant.Accent);
            btnApplyFilter.Click += (s, e) => BtnApplyFilter_Click(s, e, cmbFilters);
            panelFilters.Controls.Add(btnApplyFilter);

            Label lblQuickFilters = new Label
            {
                Text = "BỘ LỌC NHANH (1-CLICK):",
                Size = new Size(340, 16),
                Location = new Point(8, 59),
                Font = UITheme.FontGroupHeader,
                ForeColor = UITheme.TextSecondary,
                BackColor = Color.Transparent
            };
            panelFilters.Controls.Add(lblQuickFilters);

            // Lưới 2 hàng x 5 cột cho Quick Filter Chips
            string[,] quickGrid = new string[,]
            {
                { "Grayscale", "Sepia", "Invert", "Edges", "AASimple" },
                { "Blur", "Sharpen", "Brightness", "Contrast", "Saturation" }
            };
            string[,] quickLabels = new string[,]
            {
                { "Trắng đen", "Sepia", "Đảo màu", "Tách cạnh", "Khử RC" },
                { "Làm mờ", "Sắc nét", "Độ sáng", "Tương phản", "Bão hòa" }
            };

            int chipW = 66;
            int chipH = 26;
            int gapX = 4;
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    string filterKey = quickGrid[row, col];
                    string filterLabel = quickLabels[row, col];
                    Button chipBtn = new Button
                    {
                        Size = new Size(chipW, chipH),
                        Location = new Point(8 + col * (chipW + gapX), 77 + row * 30)
                    };
                    UITheme.StyleButton(chipBtn, IconType.None, filterLabel, ButtonVariant.Default);
                    chipBtn.Click += (s, e) => ApplyQuickFilter(filterKey);
                    panelFilters.Controls.Add(chipBtn);
                }
            }

            txtFilterStatus = new TextBox
            {
                Location = new Point(8, 142),
                Size = new Size(344, 80),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UITheme.BgInput,
                ForeColor = UITheme.TextSecondary,
                Font = UITheme.FontSmall,
                Text = "Sẵn sàng xử lý trên Layer đang chọn.\r\n• Chọn bộ lọc nhanh ở trên hoặc dùng danh sách chuyên sâu.\r\n• Phím tắt: Ctrl+Z để hoàn tác."
            };
            panelFilters.Controls.Add(txtFilterStatus);
            this.Tag = txtFilterStatus;

            RefreshLayerList();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateResponsiveLayout();
        }

        private void UpdateResponsiveLayout()
        {
            int innerW = Math.Max(300, this.Width - 20);

            if (headerPanel != null)
            {
                headerPanel.Width = this.Width - 4;
                if (lblLayerTitle != null) lblLayerTitle.Width = headerPanel.Width - 42;
                if (btnClosePanel != null) btnClosePanel.Location = new Point(headerPanel.Width - 32, 4);
            }

            int filtersH = 232;
            int buttonsH = 66;
            int listH = Math.Max(90, this.Height - 42 - buttonsH - filtersH - 24);

            if (flowLayerItems != null)
            {
                flowLayerItems.Size = new Size(innerW, listH);
                int itemW = Math.Max(260, flowLayerItems.ClientSize.Width - 16);
                foreach (Control c in flowLayerItems.Controls)
                {
                    c.Width = itemW;
                }
            }

            if (panelButtons != null && flowLayerItems != null)
            {
                panelButtons.Location = new Point(10, flowLayerItems.Bottom + 6);
                panelButtons.Width = innerW;
            }

            if (panelFilters != null && panelButtons != null)
            {
                panelFilters.Location = new Point(10, panelButtons.Bottom + 6);
                panelFilters.Size = new Size(innerW, filtersH);

                if (cmbFilters != null && btnApplyFilter != null)
                {
                    btnApplyFilter.Location = new Point(innerW - 94, 26);
                    cmbFilters.Width = Math.Max(140, btnApplyFilter.Left - 14);
                }

                if (txtFilterStatus != null)
                {
                    txtFilterStatus.Width = innerW - 16;
                }
            }
        }

        public int GetLayerCount()
        {
            return layers.Count;
        }

        public void RefreshLayerThumbnails()
        {
            if (flowLayerItems == null) return;
            foreach (Control c in flowLayerItems.Controls)
            {
                c.Invalidate();
            }
        }

        public void RefreshLayerList()
        {
            if (flowLayerItems == null || layers == null) return;

            flowLayerItems.SuspendLayout();
            flowLayerItems.Controls.Clear();

            int itemW = Math.Max(260, flowLayerItems.ClientSize.Width - 16);

            for (int i = layers.Count - 1; i >= 0; i--)
            {
                LayerItem layerItem = new LayerItem(layers[i])
                {
                    Width = itemW
                };

                if (i == currentLayerIndex)
                {
                    layerItem.SetSelected(true);
                }

                layerItem.VisibilityChanged += (s, e) => LayersChanged?.Invoke(this, EventArgs.Empty);
                layerItem.SelectionChanged += (s, e) =>
                {
                    foreach (LayerItem item in flowLayerItems.Controls)
                    {
                        item.SetSelected(false);
                    }
                    layerItem.SetSelected(true);

                    for (int j = 0; j < layers.Count; j++)
                    {
                        if (layers[j] == layerItem.Layer)
                        {
                            currentLayerIndex = j;
                            break;
                        }
                    }
                    LayerSelectionChanged?.Invoke(this, EventArgs.Empty);
                };

                flowLayerItems.Controls.Add(layerItem);
            }

            flowLayerItems.ResumeLayout(true);
            UpdateResponsiveLayout();
            flowLayerItems.Invalidate();
            this.Invalidate();
        }

        // ========== Các Button Handler ==========

        private void BtnAddLayer_Click(object? sender, EventArgs e)
        {
            string layerName = $"Layer {layers.Count + 1}";
            Layer newLayer = new Layer(layerName, canvasWidth, canvasHeight);

            layers.Insert(0, newLayer);
            currentLayerIndex = 0;

            RefreshLayerList();
            LayersChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnDeleteLayer_Click(object? sender, EventArgs e)
        {
            if (layers.Count <= 1)
            {
                MessageBox.Show("Phải giữ lại ít nhất 1 Layer trên vùng vẽ!", "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (currentLayerIndex >= 0 && currentLayerIndex < layers.Count)
            {
                layers[currentLayerIndex].Dispose();
                layers.RemoveAt(currentLayerIndex);

                if (currentLayerIndex >= layers.Count)
                    currentLayerIndex = layers.Count - 1;

                RefreshLayerList();
                LayersChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnMoveUp_Click(object? sender, EventArgs e)
        {
            // Trên UI, index 0 nằm trên cùng. "Lên" là giảm index về phía 0
            if (currentLayerIndex > 0)
            {
                Layer temp = layers[currentLayerIndex];
                layers[currentLayerIndex] = layers[currentLayerIndex - 1];
                layers[currentLayerIndex - 1] = temp;

                currentLayerIndex--;
                RefreshLayerList();
                LayersChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnMoveDown_Click(object? sender, EventArgs e)
        {
            // "Xuống" là tăng index về phía cuối danh sách
            if (currentLayerIndex < layers.Count - 1)
            {
                Layer temp = layers[currentLayerIndex];
                layers[currentLayerIndex] = layers[currentLayerIndex + 1];
                layers[currentLayerIndex + 1] = temp;

                currentLayerIndex++;
                RefreshLayerList();
                LayersChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnRename_Click(object? sender, EventArgs e)
        {
            if (currentLayerIndex >= 0 && currentLayerIndex < layers.Count)
            {
                Layer currentLayer = layers[currentLayerIndex];
                string? newName = PromptDialog("Đặt tên mới cho Layer:", currentLayer.Name);
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    currentLayer.Name = newName.Trim();
                    RefreshLayerList();
                    LayersChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void BtnOpacity_Click(object? sender, EventArgs e)
        {
            if (currentLayerIndex >= 0 && currentLayerIndex < layers.Count)
            {
                Layer currentLayer = layers[currentLayerIndex];
                int currentPct = (int)Math.Round(currentLayer.Opacity / 255.0 * 100);

                if (ShowStudioSliderDialog(
                    "Độ Trong Suốt Layer (Opacity)",
                    $"Điều chỉnh độ hiển thị cho '{currentLayer.Name}'",
                    0, 100, currentPct,
                    v => $"{v}%",
                    out int selectedPct))
                {
                    currentLayer.Opacity = (int)Math.Round(selectedPct / 100.0 * 255);
                    RefreshLayerList();
                    LayersChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void BtnDuplicate_Click(object? sender, EventArgs e)
        {
            if (currentLayerIndex >= 0 && currentLayerIndex < layers.Count)
            {
                Layer duplicatedLayer = layers[currentLayerIndex].Clone();
                layers.Insert(currentLayerIndex, duplicatedLayer);

                RefreshLayerList();
                LayersChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Dialog nhập chữ chuẩn Studio Dark (sử dụng ClientSize tránh lỗi cắt nút)
        /// </summary>
        private string? PromptDialog(string prompt, string defaultValue = "")
        {
            using (Form promptForm = new Form())
            {
                promptForm.Text = "Đổi Tên Layer";
                promptForm.ClientSize = new Size(340, 140);
                promptForm.StartPosition = FormStartPosition.CenterParent;
                promptForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                promptForm.MaximizeBox = false;
                promptForm.MinimizeBox = false;
                promptForm.BackColor = UITheme.BgPanel;
                promptForm.ForeColor = UITheme.TextPrimary;
                promptForm.Font = UITheme.FontBody;

                Label label = new Label
                {
                    Left = 18,
                    Top = 16,
                    Width = 304,
                    Height = 22,
                    Text = prompt,
                    Font = UITheme.FontBodyBold,
                    ForeColor = UITheme.TextPrimary
                };

                TextBox textBox = new TextBox
                {
                    Left = 18,
                    Top = 44,
                    Width = 304,
                    Text = defaultValue,
                    BackColor = UITheme.BgInput,
                    ForeColor = UITheme.TextPrimary,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = UITheme.FontBody
                };

                Button okButton = new Button
                {
                    Left = 146,
                    Top = 92,
                    Width = 84,
                    Height = 32,
                    DialogResult = DialogResult.OK
                };
                UITheme.StyleButton(okButton, IconType.None, "Lưu tên", ButtonVariant.Primary);

                Button cancelButton = new Button
                {
                    Left = 238,
                    Top = 92,
                    Width = 84,
                    Height = 32,
                    DialogResult = DialogResult.Cancel
                };
                UITheme.StyleButton(cancelButton, IconType.None, "Hủy", ButtonVariant.Default);

                promptForm.Controls.Add(label);
                promptForm.Controls.Add(textBox);
                promptForm.Controls.Add(okButton);
                promptForm.Controls.Add(cancelButton);
                promptForm.AcceptButton = okButton;
                promptForm.CancelButton = cancelButton;

                return promptForm.ShowDialog(this.FindForm()) == DialogResult.OK ? textBox.Text : null;
            }
        }

        /// <summary>
        /// Dialog thanh trượt chuẩn Studio Dark dùng chung cho Opacity, Brightness, Contrast, Blur, Saturation
        /// </summary>
        private bool ShowStudioSliderDialog(
            string title,
            string subtitle,
            int min,
            int max,
            int initial,
            Func<int, string> formatter,
            out int resultValue)
        {
            resultValue = initial;
            using (Form dlg = new Form())
            {
                dlg.Text = title;
                dlg.ClientSize = new Size(360, 170);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = UITheme.BgPanel;
                dlg.ForeColor = UITheme.TextPrimary;
                dlg.Font = UITheme.FontBody;

                Label lblSub = new Label
                {
                    Location = new Point(18, 14),
                    Size = new Size(240, 22),
                    Text = subtitle,
                    Font = UITheme.FontBodyBold,
                    ForeColor = UITheme.TextPrimary
                };

                Label lblBadge = new Label
                {
                    Location = new Point(262, 14),
                    Size = new Size(80, 22),
                    Text = formatter(initial),
                    Font = UITheme.FontMonoBold,
                    ForeColor = UITheme.AccentSecondaryHover,
                    TextAlign = ContentAlignment.MiddleRight
                };

                TrackBar slider = new TrackBar
                {
                    Minimum = min,
                    Maximum = max,
                    Value = Math.Max(min, Math.Min(max, initial)),
                    Location = new Point(14, 46),
                    Size = new Size(332, 45),
                    TickStyle = TickStyle.None,
                    BackColor = UITheme.BgPanel
                };

                slider.ValueChanged += (s, e) =>
                {
                    lblBadge.Text = formatter(slider.Value);
                };

                Button btnOk = new Button
                {
                    Location = new Point(166, 118),
                    Size = new Size(84, 32),
                    DialogResult = DialogResult.OK
                };
                UITheme.StyleButton(btnOk, IconType.None, "Áp dụng", ButtonVariant.Primary);

                Button btnCancel = new Button
                {
                    Location = new Point(258, 118),
                    Size = new Size(84, 32),
                    DialogResult = DialogResult.Cancel
                };
                UITheme.StyleButton(btnCancel, IconType.None, "Hủy", ButtonVariant.Default);

                dlg.Controls.Add(lblSub);
                dlg.Controls.Add(lblBadge);
                dlg.Controls.Add(slider);
                dlg.Controls.Add(btnOk);
                dlg.Controls.Add(btnCancel);
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(this.FindForm()) == DialogResult.OK)
                {
                    resultValue = slider.Value;
                    return true;
                }
                return false;
            }
        }

        public Layer? GetCurrentLayer()
        {
            if (currentLayerIndex >= 0 && currentLayerIndex < layers.Count)
                return layers[currentLayerIndex];
            return null;
        }

        public void SetCanvasDimensions(int width, int height)
        {
            canvasWidth = width;
            canvasHeight = height;
        }

        public int GetCurrentLayerIndex()
        {
            return currentLayerIndex;
        }

        public void UpdateAllLayersSize(int newWidth, int newHeight)
        {
            foreach (Layer layer in layers)
            {
                if (layer.Image.Width != newWidth || layer.Image.Height != newHeight)
                {
                    Bitmap newBitmap = new Bitmap(newWidth, newHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(newBitmap))
                    {
                        g.Clear(Color.Transparent);
                        g.DrawImage(layer.Image, 0, 0);
                    }
                    layer.Image.Dispose();
                    layer.Image = newBitmap;
                }
            }
        }

        private void LblLayerTitle_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragStartPosition = e.Location;
            }
        }

        private void LblLayerTitle_MouseMove(object? sender, MouseEventArgs e)
        {
            if (isDragging && e.Button == MouseButtons.Left)
            {
                int offsetX = e.X - dragStartPosition.X;
                int offsetY = e.Y - dragStartPosition.Y;

                Point newLocation = this.Location;
                newLocation.X += offsetX;
                newLocation.Y += offsetY;

                Control? parent = this.Parent;
                if (parent != null)
                {
                    newLocation.X = Math.Max(0, Math.Min(newLocation.X, parent.ClientSize.Width - this.Width));
                    newLocation.Y = Math.Max(0, Math.Min(newLocation.Y, parent.ClientSize.Height - this.Height));
                }

                this.Location = newLocation;
            }
        }

        private void LblLayerTitle_MouseUp(object? sender, MouseEventArgs e)
        {
            isDragging = false;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (IsInResizeArea(e.Location))
            {
                isResizing = true;
                resizeStartPosition = e.Location;
                resizeStartSize = this.Size;
                this.Cursor = Cursors.SizeNWSE;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (IsInResizeArea(e.Location) && !isResizing)
            {
                this.Cursor = Cursors.SizeNWSE;
            }
            else if (!IsInResizeArea(e.Location) && !isResizing)
            {
                this.Cursor = Cursors.Default;
            }

            if (isResizing)
            {
                int offsetX = e.X - resizeStartPosition.X;
                int offsetY = e.Y - resizeStartPosition.Y;

                int newWidth = Math.Max(350, resizeStartSize.Width + offsetX);
                int newHeight = Math.Max(440, resizeStartSize.Height + offsetY);

                this.Size = new Size(newWidth, newHeight);
                this.Refresh();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            isResizing = false;
            this.Cursor = Cursors.Default;
        }

        private bool IsInResizeArea(Point point)
        {
            return point.X > this.Width - ResizeHandleSize &&
                   point.Y > this.Height - ResizeHandleSize;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Viền ngoài của bảng LayerPanel
            using (Pen borderPen = new Pen(UITheme.BorderStrong, 1.5f))
            {
                g.DrawRectangle(borderPen, 1, 1, this.Width - 2, this.Height - 2);
            }

            // Góc kéo thay đổi kích thước (Resize Grip)
            Point[] resizeIndicator = new Point[]
            {
                new Point(this.Width - 13, this.Height - 3),
                new Point(this.Width - 3, this.Height - 3),
                new Point(this.Width - 3, this.Height - 13)
            };
            using (SolidBrush gripBrush = new SolidBrush(UITheme.AccentPrimaryHover))
            {
                g.FillPolygon(gripBrush, resizeIndicator);
            }
        }

        // ===== IMAGE FILTER HANDLERS =====

        private void BtnApplyFilter_Click(object? sender, EventArgs e, ComboBox? cmb)
        {
            if (cmb == null) return;
            if (currentLayerIndex < 0 || currentLayerIndex >= layers.Count)
            {
                MessageBox.Show("Vui lòng chọn Layer trước!", "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Layer currentLayer = layers[currentLayerIndex];

            switch (cmb.SelectedIndex)
            {
                case 1:
                    currentLayer.ApplyAntiAliasing("simple");
                    UpdateFilterStatus("✓ Đã áp dụng Anti-Aliasing (Simple Bilateral)");
                    break;
                case 2:
                    currentLayer.ApplyAntiAliasing("advanced");
                    UpdateFilterStatus("✓ Đã áp dụng Anti-Aliasing (Advanced)");
                    break;
                case 3:
                    currentLayer.ApplyAntiAliasing("curves");
                    UpdateFilterStatus("✓ Đã áp dụng Anti-Aliasing (Đường cong)");
                    break;
                case 4:
                    ShowBrightnessDialog(currentLayer);
                    break;
                case 5:
                    ShowContrastDialog(currentLayer);
                    break;
                case 6:
                    ShowBlurDialog(currentLayer, false);
                    break;
                case 7:
                    ShowBlurDialog(currentLayer, true);
                    break;
                case 8:
                    currentLayer.ConvertToGrayscale();
                    UpdateFilterStatus("✓ Đã chuyển sang Trắng đen (Grayscale)");
                    break;
                case 9:
                    currentLayer.DetectEdges();
                    UpdateFilterStatus("✓ Đã áp dụng Tách cạnh (Edge Detection)");
                    break;
                case 10:
                    currentLayer.ApplySharpen(2);
                    UpdateFilterStatus("✓ Đã áp dụng Làm sắc nét (Sharpen)");
                    break;
                case 11:
                    currentLayer.InvertColors();
                    UpdateFilterStatus("✓ Đã đảo ngược màu (Invert Colors)");
                    break;
                case 12:
                    currentLayer.ApplySepia();
                    UpdateFilterStatus("✓ Đã áp dụng Màu cổ điển (Sepia)");
                    break;
                case 13:
                    ShowSaturationDialog(currentLayer);
                    break;
                default:
                    UpdateFilterStatus("⚠ Vui lòng chọn một bộ lọc từ danh sách!");
                    return;
            }

            RefreshLayerThumbnails();
            LayersChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyQuickFilter(string filterName)
        {
            if (currentLayerIndex < 0 || currentLayerIndex >= layers.Count)
            {
                MessageBox.Show("Vui lòng chọn Layer trước!", "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Layer currentLayer = layers[currentLayerIndex];

            switch (filterName)
            {
                case "Grayscale":
                    currentLayer.ConvertToGrayscale();
                    UpdateFilterStatus("✓ Đã chuyển sang Trắng đen (Grayscale)");
                    break;
                case "Sepia":
                    currentLayer.ApplySepia();
                    UpdateFilterStatus("✓ Đã áp dụng Màu cổ điển (Sepia)");
                    break;
                case "Invert":
                    currentLayer.InvertColors();
                    UpdateFilterStatus("✓ Đã đảo ngược màu (Invert)");
                    break;
                case "Edges":
                    currentLayer.DetectEdges();
                    UpdateFilterStatus("✓ Đã áp dụng Phát hiện cạnh (Edges)");
                    break;
                case "AASimple":
                    currentLayer.ApplyAntiAliasing("simple");
                    UpdateFilterStatus("✓ Đã khử răng cưa nhanh (AA Simple)");
                    break;
                case "Blur":
                    ShowBlurDialog(currentLayer, false);
                    break;
                case "Sharpen":
                    currentLayer.ApplySharpen(2);
                    UpdateFilterStatus("✓ Đã làm sắc nét (Sharpen)");
                    break;
                case "Brightness":
                    ShowBrightnessDialog(currentLayer);
                    break;
                case "Contrast":
                    ShowContrastDialog(currentLayer);
                    break;
                case "Saturation":
                    ShowSaturationDialog(currentLayer);
                    break;
            }

            RefreshLayerThumbnails();
            LayersChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ShowBrightnessDialog(Layer currentLayer)
        {
            if (ShowStudioSliderDialog(
                "Điều Chỉnh Độ Sáng (Brightness)",
                "Cường độ ánh sáng (-100 đến +100)",
                -100, 100, 0,
                v => v > 0 ? $"+{v}" : $"{v}",
                out int val))
            {
                currentLayer.ApplyBrightness(val);
                UpdateFilterStatus($"✓ Đã áp dụng Độ sáng: {val}");
                LayersChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ShowContrastDialog(Layer currentLayer)
        {
            if (ShowStudioSliderDialog(
                "Điều Chỉnh Độ Tương Phản (Contrast)",
                "Hệ số tương phản (0.5x đến 3.0x)",
                5, 30, 10,
                v => $"{(v / 10.0):F1}x",
                out int val))
            {
                double contrast = val / 10.0;
                currentLayer.ApplyContrast(contrast);
                UpdateFilterStatus($"✓ Đã áp dụng Tương phản: {contrast:F1}x");
                LayersChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ShowBlurDialog(Layer currentLayer, bool isGaussian)
        {
            string title = isGaussian ? "Làm Mờ Mịn (Gaussian Blur)" : "Làm Mờ (Box Blur)";
            if (ShowStudioSliderDialog(
                title,
                "Bán kính làm mờ (1 đến 10)",
                1, 10, 3,
                v => $"Mức {v}",
                out int val))
            {
                if (isGaussian)
                {
                    currentLayer.ApplyGaussianBlur(val);
                    UpdateFilterStatus($"✓ Gaussian Blur áp dụng: mức {val}");
                }
                else
                {
                    currentLayer.ApplyBlur(val);
                    UpdateFilterStatus($"✓ Blur áp dụng: mức {val}");
                }
                LayersChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ShowSaturationDialog(Layer currentLayer)
        {
            if (ShowStudioSliderDialog(
                "Điều Chỉnh Độ Bão Hòa (Saturation)",
                "Cường độ màu sắc (0.1x đến 2.0x)",
                1, 20, 10,
                v => $"{(v / 10.0):F1}x",
                out int val))
            {
                double saturation = val * 0.1;
                currentLayer.AdjustSaturation(saturation);
                UpdateFilterStatus($"✓ Đã áp dụng Bão hòa: {saturation:F1}x");
                LayersChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void UpdateFilterStatus(string message)
        {
            if (this.Tag is TextBox txtStatus)
            {
                txtStatus.AppendText($"\r\n{message}");
                string[] lines = txtStatus.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (lines.Length > 8)
                {
                    txtStatus.Text = string.Join("\r\n", lines.Skip(lines.Length - 8));
                }
                txtStatus.SelectionStart = txtStatus.Text.Length;
                txtStatus.ScrollToCaret();
            }
        }

        public void RefreshLayersList(List<Layer> newLayers)
        {
            if (newLayers == null) return;
            this.layers = newLayers;
            currentLayerIndex = Math.Max(0, Math.Min(currentLayerIndex, Math.Max(0, layers.Count - 1)));
            RefreshLayerList();
        }
    }
}
