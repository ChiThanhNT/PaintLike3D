using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PaintLike
{
    /// <summary>
    /// Hộp thoại tùy chọn xử lý ảnh OpenCV khi Import — Chuẩn Studio Dark UI
    /// </summary>
    public partial class ImageProcessingDialog : Form
    {
        public int SelectedProcessing { get; set; } = 0; // 0 = không xử lý, 1 = đơn giản, 2 = nâng cao, 3 = curve

        public bool ApplyContrast { get; set; } = false;
        public bool ApplySharpen { get; set; } = false;
        public bool ApplyBlur { get; set; } = false;
        public bool ApplyBrightness { get; set; } = false;

        public int ContrastValue { get; set; } = 100;
        public int SharpenValue { get; set; } = 1;
        public int BlurValue { get; set; } = 0;
        public int BrightnessValue { get; set; } = 0;

        private Panel? panelTabAA;
        private Panel? panelTabFilters;
        private Button? btnTabAA;
        private Button? btnTabFilters;

        public ImageProcessingDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Tùy Chọn Xử Lý Ảnh Import (OpenCV Studio)";
            this.ClientSize = new Size(520, 540);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = UITheme.BgPanel;
            this.ForeColor = UITheme.TextPrimary;
            this.Font = UITheme.FontBody;

            // 1. Header Banner
            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = UITheme.BgToolbar,
                Padding = new Padding(18, 10, 18, 10)
            };
            headerPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UITheme.BorderDefault, 1f))
                {
                    e.Graphics.DrawLine(p, 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
                }
            };

            Label lblHeaderTitle = new Label
            {
                Text = "TÙY CHỌN TIỀN XỬ LÝ ẢNH IMPORT",
                Font = UITheme.FontTitle,
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Location = new Point(18, 12)
            };
            headerPanel.Controls.Add(lblHeaderTitle);

            Label lblHeaderSub = new Label
            {
                Text = "Khử răng cưa và tinh chỉnh chất lượng ảnh bằng OpenCV trước khi đưa vào Layer",
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                AutoSize = true,
                Location = new Point(18, 36)
            };
            headerPanel.Controls.Add(lblHeaderSub);

            // 2. Custom Segmented Tab Bar
            Panel tabSwitchBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = UITheme.BgPanel,
                Padding = new Padding(18, 8, 18, 6)
            };

            btnTabAA = new Button
            {
                Location = new Point(18, 8),
                Size = new Size(236, 32)
            };
            UITheme.StyleButton(btnTabAA, IconType.SmartSelect, "1. Khử Răng Cưa (Anti-Aliasing)", ButtonVariant.Default);
            UITheme.SetButtonSelected(btnTabAA, true);

            btnTabFilters = new Button
            {
                Location = new Point(264, 8),
                Size = new Size(238, 32)
            };
            UITheme.StyleButton(btnTabFilters, IconType.Filter, "2. Bộ Lọc Màu & Ánh Sáng", ButtonVariant.Default);

            btnTabAA.Click += (s, e) => SwitchTab(0);
            btnTabFilters.Click += (s, e) => SwitchTab(1);

            tabSwitchBar.Controls.Add(btnTabAA);
            tabSwitchBar.Controls.Add(btnTabFilters);

            // 3. Content Area
            Panel contentContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UITheme.BgPanel,
                Padding = new Padding(18, 6, 18, 6)
            };

            panelTabAA = CreateAntiAliasingPanel();
            panelTabFilters = CreateFiltersPanel();
            panelTabFilters.Visible = false;

            contentContainer.Controls.Add(panelTabAA);
            contentContainer.Controls.Add(panelTabFilters);

            // 4. Bottom Action Bar
            Panel bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                BackColor = UITheme.BgToolbar,
                Padding = new Padding(18, 12, 18, 12)
            };
            bottomPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UITheme.BorderDefault, 1f))
                {
                    e.Graphics.DrawLine(p, 0, 0, bottomPanel.Width, 0);
                }
            };

            Button btnCancel = new Button
            {
                Size = new Size(140, 34),
                Location = new Point(216, 12),
                DialogResult = DialogResult.Cancel
            };
            UITheme.StyleButton(btnCancel, IconType.None, "Giữ nguyên ảnh gốc", ButtonVariant.Default);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            Button btnApply = new Button
            {
                Size = new Size(138, 34),
                Location = new Point(364, 12),
                DialogResult = DialogResult.OK
            };
            UITheme.StyleButton(btnApply, IconType.Filter, "Áp dụng & Import", ButtonVariant.Primary);
            btnApply.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };

            bottomPanel.Controls.Add(btnCancel);
            bottomPanel.Controls.Add(btnApply);

            this.Controls.Add(contentContainer);
            this.Controls.Add(tabSwitchBar);
            this.Controls.Add(headerPanel);
            this.Controls.Add(bottomPanel);

            this.AcceptButton = btnApply;
            this.CancelButton = btnCancel;
        }

        private void SwitchTab(int tabIndex)
        {
            if (panelTabAA == null || panelTabFilters == null || btnTabAA == null || btnTabFilters == null)
                return;

            bool isAA = (tabIndex == 0);
            panelTabAA.Visible = isAA;
            panelTabFilters.Visible = !isAA;
            UITheme.SetButtonSelected(btnTabAA, isAA);
            UITheme.SetButtonSelected(btnTabFilters, !isAA);
        }

        private Panel CreateAntiAliasingPanel()
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UITheme.BgPanel
            };

            Label lblTitle = new Label
            {
                Text = "CHỌN THUẬT TOÁN KHỬ RĂNG CƯA:",
                Location = new Point(18, 8),
                Size = new Size(460, 20),
                Font = UITheme.FontGroupHeader,
                ForeColor = UITheme.AccentSecondaryHover
            };
            panel.Controls.Add(lblTitle);

            var options = new (int Mode, string Title, string Desc)[]
            {
                (0, "Không xử lý (Giữ nguyên pixel gốc)", "Nhập trực tiếp ảnh gốc không qua bộ lọc làm mịn biên."),
                (1, "Khử răng cưa Cơ bản (Bilateral Filter + Open)", "Khử nhiễu nhẹ, giữ lại độ sắc nét của các cạnh hình học."),
                (2, "Khử răng cưa Nâng cao (Bilateral + Open/Close + Median)", "Kết hợp đa tầng giúp làm mịn tối đa các vùng răng cưa phức tạp."),
                (3, "Khử răng cưa Đường cong (Gaussian + Bilateral + Close)", "Tối ưu chuyên biệt cho nét vẽ tay, đường tròn và đường cong Bezier.")
            };

            int yPos = 34;
            var radioButtons = new RadioButton[options.Length];

            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var opt = options[i];

                Panel card = new Panel
                {
                    Location = new Point(18, yPos),
                    Size = new Size(484, 72),
                    BackColor = UITheme.BgCard,
                    Cursor = Cursors.Hand
                };

                RadioButton rb = new RadioButton
                {
                    Text = opt.Title,
                    Location = new Point(14, 12),
                    Size = new Size(450, 24),
                    Font = UITheme.FontBodyBold,
                    ForeColor = UITheme.TextPrimary,
                    BackColor = Color.Transparent,
                    Checked = (opt.Mode == 0),
                    Cursor = Cursors.Hand
                };
                radioButtons[i] = rb;

                Label lblDesc = new Label
                {
                    Text = opt.Desc,
                    Location = new Point(34, 38),
                    Size = new Size(436, 22),
                    Font = UITheme.FontSmall,
                    ForeColor = UITheme.TextSecondary,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };

                rb.CheckedChanged += (s, e) =>
                {
                    card.BackColor = rb.Checked ? Color.FromArgb(238, 242, 255) : UITheme.BgCard;
                    if (rb.Checked)
                    {
                        SelectedProcessing = opt.Mode;
                        foreach (Control c in panel.Controls)
                            c.Invalidate();
                    }
                };

                card.BackColor = rb.Checked ? Color.FromArgb(238, 242, 255) : UITheme.BgCard;
                lblDesc.Click += (s, e) => rb.Checked = true;
                card.Click += (s, e) => rb.Checked = true;

                card.Paint += (s, pe) =>
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    bool sel = rb.Checked;
                    using (Pen bp = new Pen(sel ? UITheme.BorderFocus : UITheme.BorderSubtle, sel ? 1.5f : 1f))
                    {
                        pe.Graphics.DrawRectangle(bp, 0, 0, card.Width - 1, card.Height - 1);
                    }
                };

                card.Controls.Add(rb);
                card.Controls.Add(lblDesc);
                panel.Controls.Add(card);

                yPos += 82;
            }

            return panel;
        }

        private Panel CreateFiltersPanel()
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UITheme.BgPanel
            };

            int yPos = 6;

            // 1. Contrast
            CreateFilterSliderCard(
                panel,
                ref yPos,
                "Điều chỉnh Độ Tương Phản (Contrast)",
                50, 300, ContrastValue,
                v => $"{v}%",
                checkedVal => ApplyContrast = checkedVal,
                val => ContrastValue = val);

            // 2. Sharpness
            CreateFilterSliderCard(
                panel,
                ref yPos,
                "Làm Sắc Nét Chi Tiết (Sharpen)",
                1, 5, SharpenValue,
                v => $"Mức {v}/5",
                checkedVal => ApplySharpen = checkedVal,
                val => SharpenValue = val);

            // 3. Blur
            CreateFilterSliderCard(
                panel,
                ref yPos,
                "Làm Mờ Mịn (Gaussian Blur)",
                0, 10, BlurValue,
                v => v == 0 ? "Tắt (0)" : $"Mức {v}/10",
                checkedVal => ApplyBlur = checkedVal,
                val => BlurValue = val);

            // 4. Brightness
            CreateFilterSliderCard(
                panel,
                ref yPos,
                "Điều Chỉnh Độ Sáng (Brightness)",
                -100, 100, BrightnessValue,
                v => v > 0 ? $"+{v}" : $"{v}",
                checkedVal => ApplyBrightness = checkedVal,
                val => BrightnessValue = val);

            return panel;
        }

        private void CreateFilterSliderCard(
            Panel parent,
            ref int yPos,
            string title,
            int min,
            int max,
            int initial,
            Func<int, string> formatValue,
            Action<bool> onCheckChanged,
            Action<int> onValueChanged)
        {
            Panel card = new Panel
            {
                Location = new Point(18, yPos),
                Size = new Size(484, 82),
                BackColor = UITheme.BgCard
            };

            CheckBox cb = new CheckBox
            {
                Text = title,
                Location = new Point(14, 10),
                Size = new Size(340, 24),
                Font = UITheme.FontBodyBold,
                ForeColor = UITheme.TextPrimary,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            Label lblVal = new Label
            {
                Text = formatValue(initial),
                Location = new Point(360, 10),
                Size = new Size(110, 24),
                Font = UITheme.FontMonoBold,
                ForeColor = UITheme.AccentSecondaryHover,
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };

            TrackBar trk = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                Value = initial,
                Location = new Point(10, 36),
                Size = new Size(464, 40),
                TickStyle = TickStyle.None,
                BackColor = UITheme.BgCard
            };

            cb.CheckedChanged += (s, e) =>
            {
                onCheckChanged(cb.Checked);
                card.Invalidate();
            };

            trk.ValueChanged += (s, e) =>
            {
                onValueChanged(trk.Value);
                lblVal.Text = formatValue(trk.Value);
                if (!cb.Checked) cb.Checked = true;
            };

            card.Paint += (s, pe) =>
            {
                using (Pen bp = new Pen(cb.Checked ? UITheme.AccentPrimaryHover : UITheme.BorderDefault, cb.Checked ? 1.5f : 1f))
                {
                    pe.Graphics.DrawRectangle(bp, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            card.Controls.Add(cb);
            card.Controls.Add(lblVal);
            card.Controls.Add(trk);
            parent.Controls.Add(card);

            yPos += 90;
        }
    }
}
